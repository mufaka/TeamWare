# Copilot Agent - Ideas

This document is for brainstorming and discussion around building an external agent process that uses the [GitHub Copilot SDK](https://github.com/github/copilot-sdk) to autonomously work on TeamWare tasks. The agent would authenticate to TeamWare's MCP server as an Agent user, pick up assigned tasks, perform work, and report results back — creating a genuine AI team member.

---

## Context

TeamWare already has all the server-side infrastructure for agent workflows:

- **MCP Server** — Exposes tools for listing projects, reading/creating tasks, managing inbox items, posting lounge messages, querying activity, and reading the agent's own profile. Authenticated via Personal Access Tokens (PATs).
- **Agent Users** — `ApplicationUser` records with `IsAgent = true`, created by admins, with `AgentDescription` metadata and `IsActive` pause/resume control. Agents authenticate exclusively via PAT.
- **ProfileTools** — The `get_my_profile` MCP tool lets an agent confirm its own identity, read its description, and check its active status at startup.
- **TaskTools** — `list_tasks`, `get_task`, `create_task`, `update_task_status`, `assign_task` tools are all available.
- **InboxTools** — `my_inbox`, `capture_inbox`, `process_inbox_item` for GTD-style workflow.
- **ActivityTools** — `get_activity`, `get_project_summary`, `my_assignments` for situational awareness.
- **LoungeTools** — `list_lounge_messages`, `post_lounge_message`, `search_lounge_messages` for team communication.
- **CommentTools** — `add_comment` for posting updates to tasks.

The missing piece is the **external agent process** that ties these tools together with LLM reasoning. The GitHub Copilot SDK provides a .NET-native way to build this.

### What Is the GitHub Copilot SDK?

The GitHub Copilot SDK enables developers to embed production-ready agentic AI workflows into any application. It wraps the **GitHub Copilot CLI** — a standalone `copilot` binary (not the `gh` CLI) — communicating with it over JSON-RPC:

```
Your Application → SDK Client → JSON-RPC → Copilot CLI (server mode) → GitHub Copilot API (LLM)
```

The SDK manages the CLI process lifecycle automatically. The Copilot CLI is described by GitHub as "the same engine behind Copilot CLI: a production-tested agent runtime you can invoke programmatically."

Key capabilities:

- **CopilotClient / Sessions** — A client manages the Copilot CLI process lifecycle via JSON-RPC. Sessions are created with model selection, custom tools, and MCP server configurations.
- **Built-in First-Party Tools** — The Copilot CLI ships with a comprehensive set of built-in tools for code interaction. By default, the SDK operates with `--allow-all`, enabling all first-party tools. These include:

  | Tool | Purpose |
  |------|---------|
  | `view` / `read_file` | Read file contents |
  | `edit` / `edit_file` | Modify files |
  | `grep` | Search for patterns in code |
  | `glob` | Find files by pattern |
  | `bash` | Execute arbitrary shell commands |
  | Git operations | Commit, branch, push, etc. |
  | Web requests | Built-in HTTP capabilities |

  The low-level RPC API also exposes `session.rpc.shell.exec(command)` and `session.rpc.shell.kill(pid)` directly. **This means the agent can read, write, build, test, and commit code without any external MCP server for filesystem access.** The `Cwd` option on `CopilotClientOptions` controls the CLI's working directory.

- **Custom Tools** — Define additional tools as C# functions using `AIFunctionFactory.Create()` from `Microsoft.Extensions.AI`. The SDK handles schema generation, parameter binding, and result serialization. Custom tools with the same name as built-in tools can override them (with `is_override = true`).
- **MCP Server Integration** — Sessions can connect to external MCP servers (both local stdio and remote HTTP). This means our agent can connect directly to TeamWare's MCP endpoint as a tool provider. External MCP servers are for **extending** the agent's capabilities beyond what the built-in tools provide — not for replacing them.
- **Custom Agents (Sub-Agents)** — Define specialized personas with their own system prompts, tool restrictions, and MCP servers. The runtime can automatically delegate to the appropriate sub-agent based on user intent.
- **Streaming and Events** — Real-time event handling for tool execution, agent switching, and message deltas.
- **Permission Handling** — Configurable callbacks for tool execution approval. Each tool invocation triggers a permission check. Use `PermissionHandler.ApproveAll` for full autonomy, or provide a custom handler for fine-grained control (e.g., blocking dangerous shell commands).
- **BYOK (Bring Your Own Key)** — The SDK supports using your own API keys from OpenAI, Azure AI Foundry, Anthropic, etc. This means a GitHub Copilot subscription is not strictly required if you provide your own LLM endpoint.

### Key Constraints

- **Separate project** — The agent is a standalone .NET console application (or hosted service). It is not part of the TeamWare web application. TeamWare remains a pure web app; the agent is a client that connects to it.
- **GitHub Copilot subscription or BYOK** — The Copilot SDK requires either an active GitHub Copilot subscription or a BYOK (Bring Your Own Key) configuration with your own LLM provider API keys. Standard usage is a dependency on GitHub's service; BYOK allows using OpenAI, Azure AI Foundry, or Anthropic directly. Either way, this is a departure from TeamWare's fully self-hosted philosophy and should be clearly documented.
- **Network access** — The agent process needs network access to both the TeamWare MCP endpoint (local network) and GitHub's Copilot API (internet). The TeamWare server itself remains fully self-hosted with no new external dependencies.
- **Human oversight** — The agent should operate within guardrails. It moves tasks to "In Review" rather than "Done." It posts comments explaining what it did. Humans remain in the loop.
- **Idempotent and safe** — The agent should be designed so that re-running it against the same task list produces no harmful side effects. If a task is already in the correct state, the agent should skip it.

---

## Idea 1: Agent Runner Console Application

### Problem

There is no process that acts as the "brain" for agent users. TeamWare stores agent metadata and exposes MCP tools, but nothing actually picks up tasks, reasons about them, and performs work.

### Approach

Build a .NET console application (`TeamWare.Agent`) that:

1. **Starts up** — Initializes a `CopilotClient`, authenticates to TeamWare's MCP endpoint using a PAT, and calls `get_my_profile` to confirm identity.
2. **Polls for work** — Calls `my_assignments` to discover tasks assigned to this agent. Filters for tasks in "ToDo" status (the agent's work queue).
3. **Processes each task** — For each task, creates a Copilot session with the task context (title, description, project info, comments) as the prompt. The LLM has access to all built-in tools and all configured MCP server tools. It reads code, makes changes, runs builds/tests, commits, and reports back.
4. **Reports results** — Posts a comment on the task with what was accomplished, updates the task status (e.g., "ToDo" → "InProgress" → "InReview"), and optionally posts a lounge message.
5. **Sleeps and repeats** — Waits for a configurable interval before polling again.

### Architecture

```
┌──────────────────────┐       ┌──────────────────────┐
│   TeamWare.Agent     │       │   TeamWare.Web       │
│   (Console App)      │       │   (ASP.NET Core)     │
│                      │       │                      │
│  ┌────────────────┐  │  MCP  │  ┌────────────────┐  │
│  │ CopilotClient  │──┼───────┼──│ MCP Server     │  │
│  │ (Copilot SDK)  │  │ HTTP  │  │ (Tools/Prompts)│  │
│  └────────────────┘  │       │  └────────────────┘  │
│         │            │       │         │            │
│         │ JSON-RPC   │       │         │            │
│         ▼            │       │         ▼            │
│  ┌────────────────┐  │       │  ┌────────────────┐  │
│  │ Copilot CLI    │  │       │  │ Database       │  │
│  │ (GitHub API)   │  │       │  │ (SQLite/PG)    │  │
│  └────────────────┘  │       │  └────────────────┘  │
└──────────────────────┘       └──────────────────────┘
        │
        │ HTTPS
        ▼
┌──────────────────────┐
│ GitHub Copilot API   │
│ (LLM Inference)      │
└──────────────────────┘
```

### Configuration

The process accepts an array of agent identities. Each agent defines its own connection, model, working directory, and MCP servers. Each identity runs an independent polling loop, processing one task at a time.

```json
{
  "Agents": [
    {
      "Name": "CodeBot",
      "WorkingDirectory": "/home/agent/projects/teamware",
      "RepositoryUrl": "https://github.com/mufaka/TeamWare",
      "RepositoryBranch": "master",
      "PersonalAccessToken": "pat_codebot_xxxxxxxx",
      "PollingIntervalSeconds": 60,
      "Model": "gpt-4.1",
      "AutoApproveTools": true,
      "SystemPrompt": "You are a coding agent for a .NET development team...",
      "McpServers": [
        {
          "Name": "teamware",
          "Type": "http",
          "Url": "http://localhost:5000/mcp",
          "AuthHeader": "Bearer pat_codebot_xxxxxxxx"
        }
      ]
    }
  ]
}
```

### Decisions

- **Polling vs. webhooks** — Use polling at a 60-second interval. The agent calls `my_assignments` each cycle and processes one task at a time per agent identity. Webhooks are a future enhancement but not needed initially — polling is simpler and avoids adding infrastructure to TeamWare. The key constraint is **one task at a time per agent**: the agent finishes (or explicitly defers) its current task before picking up the next one. This prevents race conditions, keeps the audit trail linear, and simplifies error handling.
- **Process lifecycle** — Long-lived daemon process. The agent runs continuously as a systemd service, Docker container, or `dotnet run` in a terminal. It is not a scheduled job (no cron, no Windows Task Scheduler). The polling loop runs inside the process with `Task.Delay` between cycles. Graceful shutdown via `CancellationToken` / SIGTERM.
- **Multiple agent identities** — Support multiple agent identities from a single process from the start. The configuration accepts an array of agent profiles, each with its own PAT, MCP servers, model, working directory, and polling interval. The process runs an independent polling loop per identity (but still one task at a time per identity). This matches the `get_my_profile` design, which was built so a centralized runner could manage multiple agents. Different agents can have different system prompts — one might be tuned for backend .NET work, another for frontend JavaScript, another for documentation. The differentiation happens in the system prompt and working directory, not in tool restrictions.

### Tool Access

Each agent has access to **all** tools — both the Copilot CLI's built-in tools and every tool exposed by its configured MCP servers. The system prompt defines behavior; the LLM decides which tools to invoke.

| Source | Tools | Purpose |
|--------|-------|---------|
| **Copilot CLI built-in** | `view`, `edit`, `grep`, `glob`, `bash`, git operations | Read/write code, run builds/tests, commit and push |
| **TeamWare MCP** | `get_task`, `add_comment`, `update_task_status`, `create_task`, `assign_task`, `list_tasks`, `my_assignments`, `get_project_summary`, `get_activity`, `post_lounge_message`, etc. | Full task lifecycle, project awareness, team communication |
| **Additional MCP servers** (operator-configured) | Varies | GitHub PR creation, database access, deployment tools, etc. |

No artificial tool restrictions per agent. If an operator wants to limit what an agent can do, they control it through the system prompt and the MCP servers they configure — not through SDK-level tool filtering.

### System Prompt

The system prompt is the single point of control for agent behavior:

```
You are a coding agent for a software development team. You are authenticated
to TeamWare, a project management system, and have access to its tools for
managing tasks, posting comments, and communicating with the team.

When assigned a task:
1. Read the task details and any existing comments to understand the context.
2. Assess the scope. If the task is too broad or would require changes across
   too many files or systems, post a comment recommending how it should be
   broken down and update the task status to Blocked.
3. Explore the codebase to understand the problem.
4. Make minimal, targeted changes to solve the task.
5. Run the appropriate build and test commands to verify your changes.
6. Commit your changes to a feature branch (never to main/master).
7. Post a comment on the task summarizing what you changed and why.
8. Update the task status to InReview.

Rules:
- Never set a task to Done. Only humans approve work.
- Never create or delete tasks. You work on what you are assigned.
- Never reassign tasks to other users.
- Never delete comments.
- Always post a comment before changing task status.
- Commit to a feature branch named agent/<task-id> (e.g., agent/task-42).
- If the task is unclear, post a comment asking for clarification and update
  the task status to Blocked.
- If the task is too large, post a comment explaining why and recommending a
  breakdown, then update the task status to Blocked.
```

This prompt is configurable per agent identity. Different agents can have different instructions — one might be tuned for backend .NET work, another for frontend, another for documentation. The differentiation happens in the prompt, not in tool restrictions.

### Example Code

```csharp
// Set the CLI's working directory to the codebase
await using var client = new CopilotClient(new CopilotClientOptions
{
    Cwd = agentConfig.WorkingDirectory,
});

await using var session = await client.CreateSessionAsync(new SessionConfig
{
    Model = agentConfig.Model,
    SystemMessage = new SystemMessageConfig
    {
        Mode = SystemMessageMode.Append,
        Content = agentConfig.SystemPrompt,
    },
    // No CustomAgents — the session itself is the agent.
    // All built-in tools + all MCP tools are available.
    McpServers = agentConfig.BuildMcpServerDictionary(),
    OnPermissionRequest = agentConfig.AutoApproveTools
        ? PermissionHandler.ApproveAll
        : CustomPermissionHandler,
});

// Send the task context — the LLM decides what to do
await session.SendAndWaitAsync(new MessageOptions
{
    Prompt = $"You have been assigned the following task in project '{task.ProjectName}'.\n\n"
           + $"**Task ID:** {task.Id}\n"
           + $"**Title:** {task.Title}\n"
           + $"**Description:** {task.Description}\n"
           + $"**Priority:** {task.Priority}\n"
           + $"**Status:** {task.Status}\n"
           + $"**Comments:**\n{task.Comments}\n\n"
           + "Work on this task. When finished, post a comment summarizing "
           + "what you did and update the task status to InReview."
});
```

### How Built-in Tools Handle Code (No Filesystem MCP Server Needed)

An earlier draft of this document proposed attaching `@modelcontextprotocol/server-filesystem` or similar MCP servers for code access. **This is unnecessary.** Investigation of the Copilot SDK reveals that the Copilot CLI already ships with built-in first-party tools for all filesystem, shell, and git operations:

> *"By default, the SDK will operate the Copilot CLI in the equivalent of `--allow-all` being passed to the CLI, enabling all first-party tools, which means that the agents can perform a wide range of actions, including file system operations, Git operations, and web requests."*
> — [GitHub Copilot SDK FAQ](https://github.com/github/copilot-sdk)

The `Cwd` (working directory) option on `CopilotClientOptions` controls where the CLI operates. This single configuration parameter replaces the need for any filesystem MCP server.

### How Build/Test Works (LLM Reasoning, Not Explicit Config)

The Copilot CLI's agent runtime handles build/test through LLM reasoning:

1. The LLM uses `glob` / `grep` / `view` to explore the project structure
2. It finds markers like `*.sln`, `Makefile`, `package.json`, `Dockerfile`, etc.
3. It uses `bash` to execute the appropriate build/test commands
4. It reads the output and decides next steps

No `BuildCommand` or `TestCommand` configuration is needed. The LLM figures it out from project conventions — the same way a human developer would. The system prompt can optionally hint at the tech stack (e.g., "This is a .NET solution — use `dotnet build` and `dotnet test`") but even without hints, the LLM will typically discover the correct commands.

### Code Location Configuration

For the agent to work on code, it needs to know **where** the code is. This is configured at the agent level:

| Config Field | Example | Purpose |
|-------------|---------|---------|
| `WorkingDirectory` | `/home/agent/projects/teamware` | Local path for the Copilot CLI's `Cwd`. The CLI operates on files within this directory using its built-in tools. |
| `RepositoryUrl` | `https://github.com/mufaka/TeamWare` | Optional. If set, the agent process clones/pulls the repo into `WorkingDirectory` before starting a task. The CLI's built-in git tools handle branching, committing, and pushing. |
| `RepositoryBranch` | `main` | Optional. Default branch to pull from (defaults to repo default). |
| `RepositoryAccessToken` | `ghp_xxxx` | Optional. For private repos. |

At least `WorkingDirectory` must be present for code tasks. If a `RepositoryUrl` is also configured, the agent process (not the LLM) handles clone/pull as a setup step before creating the Copilot session.

### GitHub MCP Server — Optional, For PR Creation

The GitHub MCP server (`https://api.githubcopilot.com/mcp/`) is **not needed** for basic code operations (reading, editing, committing, pushing). Those are all handled by the Copilot CLI's built-in git tools.

However, it **is** useful for GitHub-specific operations that go beyond local git: creating pull requests, managing GitHub issues, reading PR review comments, and repository metadata. If desired, operators add it to the agent's `McpServers` array in configuration.

### Remaining Open Questions

- **Pull request creation** — Should the agent also create a pull request? This requires the GitHub MCP server (or Gitea equivalent). **Recommendation:** Start without PR creation (just push the branch). Add GitHub MCP as an optional server in config later.
- **Guardrails for code changes** — The safety guardrails in Idea 4 need to extend to code: max files changed per task, max lines changed, mandatory branch (never commit to main), etc. The `OnPermissionRequest` handler can enforce these by inspecting tool calls before approving them.
- **Multi-repo tasks** — If a task spans multiple repositories, the agent needs to switch working directories between sessions. This is a configuration concern — the agent process can create separate `CopilotClient` instances with different `Cwd` values.

---

## Design Decision: Why Not Sub-Agents?

An earlier draft proposed six sub-agents (triage, breakdown, coder, reviewer, reporter, communicator), each with restricted tool sets and the Copilot SDK's custom agent router selecting one per task. This was over-engineered:

1. **The agent's job is to write code.** It picks up a task, reads the codebase, makes changes, commits, and reports back. That's it. A single well-crafted system prompt with access to all tools handles this.

2. **The non-coding functions belong in TeamWare.** Triage (suggesting priority, asking for clarification), task breakdown, status reporting, and lounge monitoring are all features that should be built into TeamWare's UI or run as background jobs within the web application — not outsourced to an external LLM-powered process. They don't require code access or LLM reasoning at the level the Copilot SDK provides. TeamWare already has the Ollama integration for AI-assisted features within the app.

3. **Restricting tools per sub-agent adds complexity without value.** The LLM is perfectly capable of deciding which tools to use based on the task description and system prompt. Artificially limiting tool access per sub-agent creates failure modes (wrong agent selected → missing tools → task fails) without meaningful safety benefit.

4. **Sub-agent routing is a black box.** The runtime decides which sub-agent to invoke based on prompt matching against descriptions. If it picks wrong, the task fails silently or produces poor results. A single agent with a clear system prompt is more predictable and easier to debug.

> **SDK Reference:** The Copilot SDK does support custom sub-agents — specialized personas with their own prompts and tool restrictions that the runtime selects automatically. If a future use case genuinely requires different agent personas (e.g., a security auditor with `Infer = false` that only runs on explicit request), the SDK supports it. But for the initial design, it's unnecessary complexity.

---

## Idea 2: Status Transitions and Error Handling

### Status Transitions

| Current Status | Agent Action | New Status |
|---------------|-------------|------------|
| ToDo | Agent picks up the task | InProgress |
| InProgress | Agent completes its work | InReview |
| InProgress | Agent encounters an error | Error |
| InProgress | Agent needs clarification from a human | Blocked |
| InReview | (Human reviews and approves) | Done |
| InReview | (Human requests changes) | ToDo (re-queued for agent) |
| Error | (Human triages the failure) | ToDo or Done |
| Blocked | (Human provides clarification) | ToDo (re-queued for agent) |

The agent never sets a task to "Done." That is always a human decision.

### Blocked Tasks

When the agent determines it cannot proceed because the task description is ambiguous, missing acceptance criteria, references unknown systems, or otherwise lacks the information needed to make a code change, it:

1. **Posts a comment** on the task explaining exactly what information it needs — e.g., "The task says 'fix the timeout issue' but doesn't specify which endpoint or what the expected timeout should be. Please clarify."
2. **Moves the task to Blocked status** so it does not get re-picked by the agent on the next polling cycle.
3. **Moves on to the next task.**

A human reads the comment, provides the missing information (via a reply comment or by updating the task description), and moves the task back to ToDo. The agent picks it up again on its next cycle with the new context.

This prevents the agent from entering a loop where it repeatedly picks up an unclear task, asks the same clarification question, and wastes a Copilot session each cycle.

> **Note:** TeamWare does not currently have "Blocked" or "Error" task statuses. Both will need to be added to the `TaskWorkItem` model as part of the agent specification. The existing statuses are ToDo, InProgress, InReview, and Done.

### Error Handling

When the agent fails mid-task (API error, LLM timeout, build failure it cannot resolve, etc.), it:

1. **Posts a comment** on the task explaining what went wrong — including any error messages, the step it was on, and what it had accomplished up to that point.
2. **Moves the task to Error status** so it is clearly visible to humans and does not get re-picked by the agent on the next polling cycle.
3. **Moves on to the next task.** The agent does not retry failed tasks. A human must triage the error, fix the underlying issue (or clarify the task), and move it back to ToDo if the agent should retry.

This keeps the agent's polling loop simple and prevents it from getting stuck in a retry loop on a broken task. The Error state acts as a clear signal to the team that human attention is needed.

### Stuck Detection

Stuck detection is **not** the agent's responsibility. If a task sits in InProgress or Error for too long, that is a problem for the TeamWare web application to detect and surface.

The planned approach is a **Hangfire background job** in TeamWare.Web that periodically scans for tasks assigned to agent users that have been in InProgress beyond a configurable threshold. The job would post a warning comment, send a notification to the project owner, or auto-transition the task to Error.

This keeps the agent process simple (it only cares about its current task) and puts monitoring where it belongs — in the server that owns the data.

---

## Idea 3: Lounge Integration — Human-in-the-Loop Notifications

### Problem

When the agent moves a task to **Error** or **Blocked**, the task comment alone may not be noticed quickly. The team needs a visible signal in the lounge that human attention is required.

### Approach

The agent posts to the project lounge **only** when it needs human intervention:

- **Blocked** — When the agent moves a task to Blocked: "I need help with Task #42 — Fix login timeout issue. I've posted a comment explaining what information I need. Can someone take a look?"
- **Error** — When the agent moves a task to Error: "I ran into a problem on Task #42 — Fix login timeout issue. I've posted a comment with the error details. Someone will need to triage this."

That's it. No startup announcements, no task pickup chatter, no daily summaries, no mention detection. The agent's task comments already provide a detailed audit trail of what it's doing. The lounge is reserved for the one thing that requires immediate human awareness: the agent is stuck and cannot continue.

### Decisions

- **No icons or emoticons.** Lounge messages are plain text. The agent user already has a bot avatar that visually distinguishes it from human team members — no need for robot emoji or other decorative markers in the message content.
- **No chattiness.** Lounge messages are restricted to Error and Blocked transitions. This keeps the lounge useful for humans without agent noise drowning out team conversation. If a team member wants to know what the agent is doing, they check the agent's assigned tasks or the activity feed — not the lounge.
- **No mention detection.** The agent does not monitor the lounge for `@mentions`. There is no clear use case that isn't already covered by task comments and the Blocked/Error workflow. If someone wants the agent to do something, they assign it a task. Mention detection adds polling complexity for marginal value.
- **Lounge message targets the project lounge**, not the global lounge. The message goes to the same project the task belongs to, so the right team members see it.

---

## Idea 4: Safety and Guardrails

### Problem

An autonomous agent interacting with project data needs guardrails to prevent runaway behavior, data corruption, or spammy output.

### Proposed Guardrails

| Guardrail | Description |
|-----------|-------------|
| **Read-before-write** | Agent must read current task state before making any changes. Prevents stale-state updates. |
| **Comment-before-status** | Agent must post a comment explaining its action before changing task status. Creates audit trail. |
| **No self-approval** | Agent cannot move tasks to "Done." Only humans can approve agent work. |
| **No creates, deletes, or reassignments** | Agent cannot create tasks, delete tasks, delete comments, or reassign tasks to other users. It works on what it is assigned — it does not reorganize the backlog or delegate work. If a task is too large, the agent posts a comment recommending that a human break it down, then moves the task to Blocked. |
| **Scope awareness** | The agent should assess whether a task is appropriately scoped before starting work. If a task is too broad or would require changes across too many files/systems, the agent posts a comment explaining why it thinks the task should be broken down into smaller pieces, then moves the task to Blocked for human review. The LLM makes this judgment — there is no hard-coded file or line limit. |
| **Kill switch** | Setting `IsActive = false` on the agent user in TeamWare should cause the agent to gracefully stop on next poll (via `get_my_profile` check). |
| **Dry run mode** | A configuration option where the agent runs through its pipeline, logs what it would do, but does not call any write tools. Useful for testing and tuning. |

### Decisions

- **No rate limiting.** An earlier draft proposed maximum tool calls per task and maximum tasks per polling cycle. This was removed because artificial limits cause more problems than they solve — a legitimate task that needs many tool calls (e.g., a multi-file refactor with build/test cycles) would hit the limit and fail partway through, leaving the codebase in a broken state. The agent should be free to use as many tool calls as the task requires. Scope awareness (above) is the better guardrail: if the task is too big, the agent says so upfront rather than failing mid-execution.
- **No idle detection.** The agent does not post messages when it has nothing to do. Idle messages add noise to the lounge for no value. The agent should also maintain its normal polling interval at all times so it remains responsive when new work is assigned. Reducing polling frequency when idle would delay task pickup unnecessarily.

### Resolved Questions

- **Token/cost awareness is the human's responsibility.** Each Copilot SDK session consumes tokens, but tracking and managing that cost is the operator's job — not the agent's. Token usage should be monitored externally (e.g., via the LLM provider's dashboard or billing API). The agent process does not need to track, log, or limit its own token consumption.
- **Prompt injection is mitigated by action restrictions, not detection.** Task descriptions and comments are user-generated content and could contain prompt injection attempts. Rather than attempting to detect and sanitize injection (a never-ending arms race), the agent is protected by its existing guardrails: it cannot create or delete tasks, cannot delete comments, and cannot reassign tasks to other users. These restrictions limit the blast radius of any successful injection to the agent's own assigned work. The Copilot SDK's built-in safety filters provide an additional layer. Dedicated prompt injection detection is out of scope for this document and will be tackled as a separate concern if needed in the future.

---

## Idea 5: Development and Testing Strategy

### Problem

An autonomous agent that modifies production data needs thorough testing. How do we develop and test safely?

### Approach

1. **Dry run mode first** — Implement the full pipeline with dry-run as the default. The agent reads tasks, reasons about them, and logs its planned actions — but calls no write tools. This lets us validate reasoning quality without risk.

2. **Dedicated test project** — Create a "Sandbox" project in TeamWare with test tasks for the agent to work on. Mistakes here have no impact on real work.

3. **Integration tests** — Test the agent's interaction with TeamWare's MCP endpoint using a test server. Mock the Copilot SDK responses to validate the pipeline logic independently of LLM quality.

4. **Prompt engineering iteration** — The system prompt is the most important tuning parameter. Iterate on it based on observed agent behavior in the sandbox project.

5. **Gradual rollout** — Start with simple, low-risk tasks (documentation, test additions) in the sandbox project. Increase task complexity as confidence in the agent's behavior grows.

---

## Summary of Proposed Scope

| Component | Priority | Risk |
|-----------|----------|------|
| Console app scaffold with CopilotClient | High | Low |
| MCP endpoint connection and authentication | High | Low |
| Polling loop with `my_assignments` | High | Low |
| System prompt and agent configuration | High | Low |
| Code execution via built-in CLI tools (view, edit, bash, git) | High | Medium |
| Safety guardrails (rate limits, dry-run, kill switch) | High | Low |
| Status transitions and error handling | Medium | Medium |
| Lounge integration (Error/Blocked notifications only) | Medium | Low |
| GitHub MCP server for PR creation (optional) | Low | Low |

### Next Steps

1. **Review and refine** this idea document. Identify which ideas to pursue and which to defer.
2. **Create a specification** that turns accepted ideas into concrete, testable requirements.
3. **Build an implementation plan** with phased delivery, starting with the console app scaffold and dry-run pipeline.
