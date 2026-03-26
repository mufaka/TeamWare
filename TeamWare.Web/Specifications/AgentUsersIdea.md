# Agent Users - Ideas

This document is for brainstorming and discussion around introducing "Agent" users to TeamWare. The goal is to make TeamWare **agent-ready** - meaning external autonomous processes can participate as first-class team members, picking up assigned tasks, performing work, and reporting results back through TeamWare's existing MCP server.

The actual agent process (built with the [GitHub Copilot SDK](https://github.com/github/copilot-sdk) or similar) is a separate project and out of scope for this document. This document focuses exclusively on what TeamWare itself needs to support agent workflows.

---

## Context

TeamWare already has the building blocks for agent integration:

- **MCP Server** - Exposes tools for listing projects, reading/creating tasks, managing inbox items, posting lounge messages, and querying activity. Authenticated via Personal Access Tokens (PATs).
- **Personal Access Tokens** - Each user can create PATs. An agent user would authenticate to the MCP endpoint using its own PAT.
- **Task Assignments** - Tasks can be assigned to any `ApplicationUser`. An agent user could receive assignments just like a human.

The missing piece is a way to distinguish agent users from human users and establish the conventions by which an agent discovers, works on, and completes tasks.

### Out of Scope

- **Agent runtime implementation** - The external agent process (expected to use the GitHub Copilot SDK) is a separate project. TeamWare does not host, schedule, or manage the agent's execution.
- **LLM model selection and inference** - All LLM reasoning happens in the external agent process. TeamWare's existing Ollama integration (for content rewriting and summaries) is unrelated to agent execution. TeamWare does not need to know which model an agent uses or how it performs inference.
- **Code generation, file system access, git operations** - These are concerns of the external agent process, not TeamWare.

### Key Constraints

- **Optional feature** - Agent users must be entirely optional. Teams that do not use agents should see no difference in their experience.
- **Self-hosted alignment** - No external service dependencies for TeamWare itself. Agents connect to TeamWare's MCP endpoint, which runs in-process. What happens on the agent side (Copilot SDK, LLM calls, etc.) is not TeamWare's concern.
- **Audit trail** - All actions taken by an agent must be fully traceable. Activity logs, comments, and status changes should clearly indicate they were performed by an agent user.
- **Human oversight** - Agents should not operate in a fully unsupervised mode by default. The workflow should include review checkpoints (e.g., agent moves task to "In Review" rather than "Done").

---

## Idea 1: Agent User Type

### Problem

There is currently no way to distinguish a human user from an automated agent. All `ApplicationUser` records are treated identically. This makes it difficult to:
- Filter agent activity from human activity in dashboards.
- Apply agent-specific policies (e.g., agents cannot approve their own work).
- Display agent-specific UI elements (e.g., a bot badge on comments).

### Approach

Add an `IsAgent` boolean flag to `ApplicationUser`. Agent users are created through the admin panel, not through self-registration. An agent user:
- Has a `DisplayName` (e.g., "CodeBot", "ReviewAgent").
- Has a distinctive avatar or badge in the UI (e.g., a robot icon).
- Can be assigned to projects as a `ProjectMember` with role `Member`.
- Authenticates exclusively via PAT (no interactive login).
- Cannot access the web UI (or sees a minimal status-only dashboard).

### Decisions

- **Team size limits:** There is no concept of team size in TeamWare currently. Agent users do not count toward any limit, and this is not a concern unless team size limits are introduced in the future.
- **Entity model:** Agent users are regular `ApplicationUser` records with `IsAgent = true`. A separate entity is not needed. Keeping agents as `ApplicationUser` means the existing audit trail, activity logs, task assignments, comments, and project membership all work without modification. The `IsAgent` flag is the only addition.
- **Roles:** No agent-specific role (e.g., `ProjectRole.Agent`) for now. Agents are assigned the standard `Member` role on projects. The MCP server already defines what operations are allowed for authenticated users, and adding an agent role would create a second authorization layer that could conflict or cause confusion. This can be revisited in the future if finer-grained agent permissions are needed.

---

## Idea 2: Agent Metadata and Configuration

### Problem

When an admin creates an agent user, what additional metadata should TeamWare store beyond the standard `ApplicationUser` fields? Since all LLM inference happens in the external agent process (via the GitHub Copilot SDK or similar), TeamWare does not need to know which model an agent uses. But there may still be useful metadata to track.

### Proposed Fields on Agent Users

- **`AgentDescription`** (string, optional) - A human-readable description of the agent's purpose and capabilities (e.g., "Handles code review tasks using GitHub Copilot"). Displayed in the admin panel and when viewing agent profiles.
- **`IsActive`** (bool) - Whether the agent is currently enabled. When false, the agent's PATs are effectively suspended. Provides a pause/resume mechanism without revoking tokens.

### What TeamWare Does NOT Need to Store

- **LLM model name** - The external agent process owns this decision entirely. TeamWare does not need to know or care whether the agent uses GPT-4, Claude, Codex, or a local Ollama model. This is configuration for the agent process, not for TeamWare.
- **Agent runtime configuration** - Polling intervals, concurrency limits, retry policies, etc. are all concerns of the external process.

### Decisions

- **`AgentDescription` format:** Free-text field. This keeps it flexible and avoids prematurely committing to a structure. If the external agent process evolves a convention for describing personas or capabilities, we can revisit and add structured metadata later.
- **Pause/resume mechanism:** `IsActive` is sufficient. Time-based scheduling (e.g., business hours only) is overkill for now and is better handled by the external agent process if needed.
- **MCP profile tool:** Yes. TeamWare will expose a `get_my_profile` MCP tool that returns the authenticated user's profile, including `IsAgent`, `AgentDescription`, and `IsActive`. This lets a centralized agent runner support multiple agent identities from a single process and allows the agent to confirm its own identity and read its metadata at startup.

---

## Idea 3: Agent Workflow

### Problem

How does an agent discover work, perform it, and report results? The MCP server provides the tools, but the workflow conventions need definition.

### Proposed Workflow

```
1. Agent authenticates via PAT to MCP endpoint
2. Agent calls `my_assignments` to find tasks assigned to it
3. Agent filters for tasks in "ToDo" status
4. For each task:
   a. Agent calls `get_task` to read title, description, and comments
   b. Agent moves task to "InProgress" via `update_task_status`
   c. Agent performs work (external process - code generation, analysis, etc.)
   d. Agent posts results as a comment via `add_comment`
   e. Agent moves task to "InReview" via `update_task_status`
5. Human reviewer reviews the agent's work
6. Human moves task to "Done" or back to "ToDo" with feedback comments
7. If moved back to "ToDo", agent picks it up again in the next cycle
```

### Decisions

- **Polling:** The external agent process polls for work by calling `my_assignments`, focuses on a single task at a time, and polls again when done. This is simple, stateless, and keeps the agent process in full control of its own pacing. Event-driven notifications (webhooks, SignalR) could be added in a future iteration but are not needed to start.
- **Agent comments:** TeamWare's responsibility is the bot badge - the UI renders comments from agent users with a visual indicator based on the `IsAgent` flag. Any structured comment format (e.g., `## Agent Report` with sections) is a convention for the external agent process to follow; TeamWare does not enforce or parse it.

---

## Idea 4: Agent Management UI

### Problem

Admins need a way to create, configure, and monitor agent users.

### Features

- **Create Agent User** - Admin form to create an agent user with display name, description of the agent's purpose, and project memberships.
- **Generate PAT** - Automatic PAT generation when creating an agent, with the token displayed once for copying into the external process configuration.
- **Agent Dashboard** - A view showing all agent users, their current task counts, last active time, and recent activity.
- **Agent Activity Feed** - Filter the existing activity log to show only agent actions.
- **Pause/Resume** - Ability to deactivate an agent user temporarily (revoke its PAT or set a flag) without deleting it.

---

## Idea 5: Agent Task Scope

### Problem

Not all tasks are suitable for agents. We need a way to indicate which tasks an agent can work on and what kind of work is expected.

### Decision

**Assignment-based only.** Agents only work on tasks that a human user has explicitly assigned to them. The task creator or project lead is responsible for deciding which tasks are appropriate for an agent and assigning accordingly.

This means:
- No tags, labels, or task type fields needed on `TaskItem` for agent eligibility.
- No auto-assignment mechanism. Agents do not self-assign from backlogs.
- No changes to the `TaskItem` model.
- The existing `my_assignments` MCP tool is the only discovery mechanism an agent uses.

### Future Considerations

Tag/label-based eligibility and auto-assignment could be revisited in a future iteration if the assignment-based approach proves too limiting. For now, keeping humans in the loop for task assignment aligns with the human oversight constraint.

---

## Idea 6: Integration Surface and External Process

### Problem

TeamWare is the task management system, not the agent runtime. The actual agent logic runs in an external process built with the GitHub Copilot SDK. What does TeamWare need to provide to support that integration?

### Architecture

```
+------------------+       MCP (HTTP + PAT)       +---------------------------+
|                  | <---------------------------> |                           |
|    TeamWare      |   my_assignments, get_task,   |   Agent Process           |
|    MCP Server    |   update_task_status,          |   (GitHub Copilot SDK)    |
|                  |   add_comment, etc.            |                           |
+------------------+                               +---------------------------+
```

- The agent process is a **separate application** built with the GitHub Copilot SDK.
- It authenticates to TeamWare via PAT.
- It calls MCP tools to discover and update tasks.
- All LLM inference, code generation, and external operations happen in the agent process.
- TeamWare does not need to know the implementation details of the agent process.

### What TeamWare Provides

The existing MCP tools are already sufficient for the core agent workflow:
- `my_assignments` - Agent discovers its assigned tasks.
- `get_task` - Agent reads task details and comments.
- `update_task_status` - Agent moves tasks through the workflow.
- `add_comment` - Agent reports results.
- `create_task` - Agent can create follow-up tasks if needed.
- `list_tasks` - Agent can browse project backlogs.

### New MCP Tools for Agents

- **`get_my_profile`** - Returns the authenticated user's profile, including the `IsAgent` flag, `AgentDescription`, and `IsActive` status. Lets the agent process confirm its identity and read its own metadata. A centralized agent runner can use this to support multiple agent identities from a single process.
- **`get_project_context`** (existing prompt, but could be enhanced) - Returns project description, recent activity, and team members. Useful for giving the agent process context when working on a task.

### What TeamWare Does NOT Do

- Does not host, schedule, or manage the agent process.
- Does not perform LLM inference on behalf of agents.
- Does not have any dependency on the GitHub Copilot SDK.

---

## Summary of Key Decisions

| Decision | Options Considered | Decision |
|----------|-------------------|----------|
| How to identify agent users | `IsAgent` flag vs. `UserType` enum vs. separate entity | `IsAgent` flag on `ApplicationUser` |
| Agent metadata | Description only vs. structured capabilities | Free-text `AgentDescription` + `IsActive` flag |
| Task discovery | Polling vs. event-driven | Polling via `my_assignments` (one task at a time) |
| Task eligibility | Assignment-based vs. tag-based vs. type field | Assignment-based (human assigns tasks to agents) |
| Agent status reporting | Comments vs. structured fields | Bot badge in UI (TeamWare); comment format is agent convention |
| New MCP tools needed | None vs. `get_my_profile` vs. more | `get_my_profile` confirmed |

---

## Next Steps

1. ~~Discuss and refine the ideas above.~~ All ideas decided.
2. ~~Decide on the key options listed in the summary table.~~ All decisions confirmed.
3. ~~Create a formal specification (`AgentUsersSpecification.md`) based on the decisions above.~~ Complete.
4. ~~Define an implementation plan with phases, building on existing MCP infrastructure.~~ Complete.
5. Separately, begin designing the external agent process using the GitHub Copilot SDK (separate repository/project).
