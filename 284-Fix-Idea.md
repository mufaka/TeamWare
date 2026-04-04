# Fix Idea: Issue #284 — Agents need to switch to the configured branch before pulling

**Issue:** [#284](https://github.com/mufaka/TeamWare/issues/284)
**Severity:** High — agents fail to pull, blocking all subsequent task processing
**Trigger:** Agent finishes a task on a feature branch, then tries to pull the configured branch (e.g., `main`) for the next task without switching back first

---

## Root Cause Analysis

All three agent implementations have the same bug in their repository sync logic. The flow is:

1. **Clone** (first run): `git clone --branch main --single-branch <url> <dir>` — leaves HEAD on `main`. ✅
2. **Task processing**: The LLM (via system prompt) is instructed to create and commit to a feature branch named `agent/task-<id>`. After processing, the working directory's HEAD is on `agent/task-<id>`. ✅
3. **Next task arrives**: `EnsureRepositoryAsync` / `ensureRepository` sees `.git` exists and runs `git pull origin main`. ❌

Step 3 fails because `git pull origin main` while on branch `agent/task-42` attempts to merge `origin/main` into the current feature branch. This can:

- **Cause merge conflicts** — the feature branch has diverged from `main`.
- **Silently merge unrelated changes** — `main` commits get merged into the agent's feature branch.
- **Fail with "refusing to merge unrelated histories"** — if the feature branch was rebased or has no common ancestor.
- **Leave dirty working tree state** — uncommitted changes from the previous task block the pull entirely.

### C# Agent — `RepositoryManager.PullLatestAsync`

```csharp
// RepositoryManager.cs:107-111
await RunGitCommandAsync(
    $"pull origin {repo.Branch}",   // "pull origin main" while on agent/task-42
    repo.WorkingDirectory,
    agentName,
    cancellationToken);
```

No `git checkout` before the pull. No handling of dirty working tree.

### TypeScript Agents — `ensureRepository`

```typescript
// repository/manager.ts:86-87  (both Claude and Codex, identical)
await git(repo.workingDirectory, ["remote", "set-url", "origin", authenticatedUrl]);
await git(repo.workingDirectory, ["pull", "origin", repo.branch]);  // same problem
```

Same issue: no checkout, no dirty-state handling.

---

## Proposed Fix

### Overview

Before pulling, the agent must:

1. **Discard any uncommitted changes** (safety reset — agent work should already be committed).
2. **Checkout the configured branch** (`main` or whatever `repo.Branch` is).
3. **Then pull** from origin.

After pulling and before handing off to the LLM, no additional branch manipulation is needed — the system prompt instructs the LLM to create its own `agent/task-<id>` branch from the current HEAD.

### Why not preserve the feature branch state?

The issue description mentions "The agent should be smart enough to switch back to a local copy of a branch if it exists before working." This refers to the scenario where an agent is re-assigned a task it previously worked on (e.g., after review feedback). However, this is actually **already handled** by the system prompt convention:

- The LLM is instructed to commit to `agent/task-<id>`.
- If that branch already exists locally from previous work, the LLM can check it out and continue.
- The important thing is that `EnsureRepositoryAsync` puts the repo into a **clean, up-to-date state on the configured branch** before the LLM takes over.

The repository sync's job is simply: "give me a clean checkout of the configured branch with the latest remote changes." The LLM handles branching from there.

### C# Agent Changes

#### `RepositoryManager.PullLatestAsync` — Add checkout before pull, with clean/reset

```csharp
private async Task PullLatestAsync(ResolvedRepository repo, string agentName, CancellationToken cancellationToken)
{
    _logger.LogInformation(
        "Agent '{AgentName}': Pulling latest from branch '{Branch}' in '{WorkingDirectory}'",
        agentName, repo.Branch, repo.WorkingDirectory);

    // If access token is configured, update the remote URL
    if (!string.IsNullOrWhiteSpace(repo.AccessToken) &&
        !string.IsNullOrWhiteSpace(repo.RepositoryUrl))
    {
        var authenticatedUrl = BuildAuthenticatedUrl(repo.RepositoryUrl, repo.AccessToken);
        await RunGitCommandAsync(
            $"remote set-url origin {authenticatedUrl}",
            repo.WorkingDirectory, agentName, cancellationToken);
    }

    // NEW: Discard any uncommitted changes from previous task processing
    await RunGitCommandAsync(
        "reset --hard HEAD",
        repo.WorkingDirectory, agentName, cancellationToken);

    await RunGitCommandAsync(
        "clean -fd",
        repo.WorkingDirectory, agentName, cancellationToken);

    // NEW: Switch to the configured branch before pulling
    await RunGitCommandAsync(
        $"checkout {repo.Branch}",
        repo.WorkingDirectory, agentName, cancellationToken);

    // Now pull — we're guaranteed to be on the correct branch
    await RunGitCommandAsync(
        $"pull origin {repo.Branch}",
        repo.WorkingDirectory, agentName, cancellationToken);

    _logger.LogInformation(
        "Agent '{AgentName}': Pull completed successfully",
        agentName);
}
```

#### Key details

| Step | Command | Purpose |
|---|---|---|
| Reset | `git reset --hard HEAD` | Discard any uncommitted changes or staged files left by the LLM |
| Clean | `git clean -fd` | Remove untracked files/directories created during task work |
| Checkout | `git checkout {branch}` | Switch from `agent/task-<id>` back to the configured branch |
| Pull | `git pull origin {branch}` | Fast-forward to latest remote state |

#### Edge case: configured branch doesn't exist locally yet

After the initial `--single-branch` clone, only the configured branch is tracked. If somehow the local branch reference is missing, `git checkout main` would fail. However, this shouldn't happen in practice because:

- Clone creates the branch tracking ref.
- The agent never deletes local branches.

If we want to be defensive, we could use `git checkout -B {branch} origin/{branch}` which force-creates/resets the branch to match the remote. This is even safer:

```
git checkout -B main origin/main
```

This handles the edge case where the local `main` has somehow diverged and also works if the local tracking branch doesn't exist. **Recommendation: use `-B` variant for maximum robustness.**

### TypeScript Agent Changes (Claude & Codex)

Both `repository/manager.ts` files are identical and need the same fix.

#### `ensureRepository` — Add reset + checkout before pull

```typescript
export async function ensureRepository(repo: ResolvedRepository): Promise<void> {
  mkdirSync(repo.workingDirectory, { recursive: true });

  const gitDir = join(repo.workingDirectory, ".git");
  const authenticatedUrl = buildAuthenticatedUrl(repo.url, repo.accessToken);

  if (existsSync(gitDir)) {
    // Update remote URL in case token changed
    await git(repo.workingDirectory, ["remote", "set-url", "origin", authenticatedUrl]);

    // NEW: Discard uncommitted changes from previous task
    await git(repo.workingDirectory, ["reset", "--hard", "HEAD"]);
    await git(repo.workingDirectory, ["clean", "-fd"]);

    // NEW: Switch to configured branch before pulling
    await git(repo.workingDirectory, ["checkout", "-B", repo.branch, `origin/${repo.branch}`]);

    // Fetch + merge (pull) on the correct branch
    await git(repo.workingDirectory, ["pull", "origin", repo.branch]);
  } else {
    await git(".", [
      "clone",
      "--branch", repo.branch,
      "--single-branch",
      authenticatedUrl,
      repo.workingDirectory,
    ]);
  }
}
```

---

## Validation of the Proposed Fix

### Does `git reset --hard HEAD` + `git clean -fd` risk losing agent work?

**No.** By the time `EnsureRepositoryAsync` runs for a *new* task, the previous task has already completed its processing cycle. The system prompt instructs the LLM to commit and push all changes. Any uncommitted leftovers are either:

- Build artifacts / generated files (safe to discard).
- Partial work from a failed task (already reported as Error status).

### Does `git checkout -B main origin/main` destroy local feature branches?

**No.** It only resets the `main` branch pointer. Feature branches like `agent/task-42` remain intact as local refs. The LLM can still check them out if re-assigned to the same task.

### Does `--single-branch` clone cause issues with `origin/main` ref?

After `git clone --single-branch`, the remote tracking ref `origin/main` exists and is updated by `git fetch`/`git pull`. The `-B` checkout works because `origin/main` is always available. No need to change the clone command.

### What about concurrent tasks on the same repo?

The current architecture processes tasks **sequentially** within each agent identity (confirmed in `AgentPollingLoop.ExecuteCycleAsync` — the `foreach` loop). There is no concurrent access to the same working directory, so the reset/checkout/pull sequence is safe.

### What if `git clean -fd` removes something important?

The `-fd` flags remove untracked files and directories. In an agent working directory, untracked files are either:

- Files the LLM created but didn't commit (should have been committed).
- Build artifacts.
- Downloaded dependencies (will be restored by build commands).

This is safe. If we wanted extra safety, we could add `-x` to also clean gitignored files (like `bin/`, `obj/`, `node_modules/`), but `-fd` is sufficient for the core problem.

---

## Files to Modify

### TeamWare.Agent (C#)

| File | Change |
|---|---|
| `Repository/RepositoryManager.cs` | Add `reset --hard`, `clean -fd`, `checkout -B` before `pull` in `PullLatestAsync` |

### TeamWare.Agent.Claude (TypeScript)

| File | Change |
|---|---|
| `src/repository/manager.ts` | Add `reset --hard`, `clean -fd`, `checkout -B` before `pull` in `ensureRepository` |

### TeamWare.Agent.Codex (TypeScript)

| File | Change |
|---|---|
| `src/repository/manager.ts` | Add `reset --hard`, `clean -fd`, `checkout -B` before `pull` in `ensureRepository` |

### Tests

| File | Change |
|---|---|
| `TeamWare.Agent.Tests/Repository/RepositoryManagerTests.cs` | Add test verifying that `PullLatestAsync` runs reset, clean, and checkout commands before pull |

---

## Testing Plan

1. **Unit Test (C#):** Create a testable subclass or use a mock/spy on `RunGitCommandAsync` to verify the exact sequence of git commands: `reset --hard HEAD` → `clean -fd` → `checkout -B {branch} origin/{branch}` → `pull origin {branch}`.
2. **Integration Test:** Clone a repo, checkout a feature branch, add uncommitted changes, then run `EnsureRepositoryAsync`. Verify the working directory ends up on the configured branch with no uncommitted changes.
3. **Manual Test:** Run the agent, let it process a task (which creates `agent/task-<id>` branch), then assign a second task. Verify the agent successfully pulls `main` and processes the second task without git errors.

---

## Risks & Mitigations

| Risk | Mitigation |
|---|---|
| `git reset --hard` discards uncommitted LLM work | By design — previous task is already complete. If the LLM failed to commit, the task was already marked Error. |
| `git clean -fd` removes needed untracked files | Agent working directories are ephemeral task-processing spaces, not human development environments. |
| `git checkout -B` fails if remote ref doesn't exist | Only possible if the configured branch was deleted on the remote — a misconfiguration, not an agent bug. Log a clear error. |
| Increased git command count per cycle (4 vs 1) | Negligible — these are local operations (except pull) taking milliseconds. |
| Feature branch work lost on re-assignment | Not lost — local branch refs survive. The LLM re-checks out the existing `agent/task-<id>` branch per the system prompt. |
