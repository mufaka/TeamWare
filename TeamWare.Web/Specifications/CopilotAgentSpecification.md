# TeamWare - Copilot Agent Specification

## 1. Introduction

### 1.1 Purpose

This document provides the formal specification for the Copilot Agent, an external .NET console application that uses the [GitHub Copilot SDK](https://github.com/github/copilot-sdk) to autonomously work on TeamWare tasks. It defines the functional requirements, configuration model, process lifecycle, safety guardrails, and testing strategy for the agent. This specification is a companion to the [main TeamWare specification](Specification.md), the [MCP Server specification](McpServerSpecification.md), and the [Agent Users specification](AgentUsersSpecification.md).

### 1.2 Scope

The Copilot Agent is a standalone .NET console application (`TeamWare.Agent`) that runs outside the TeamWare web application. It authenticates to TeamWare's MCP server as an Agent user, polls for assigned tasks, uses LLM reasoning to read code, make changes, run builds and tests, commit to feature branches, and report results back to TeamWare.

The specification covers:

- **Area A** — Process architecture, configuration model, and process lifecycle.
- **Area B** — Task processing pipeline: polling, session creation, code execution, status transitions, and reporting.
- **Area C** — Safety guardrails, error handling, and lounge notifications.
- **Area D** — Development, testing, and rollout strategy.

This specification covers only the external agent process. Changes to the TeamWare web application required to support the agent (new task statuses, data model changes) are specified separately and referenced where applicable.

### 1.3 Definitions and Acronyms

| Term | Definition |
|------|-----------|
| Agent Process | The `TeamWare.Agent` console application. A long-lived daemon that polls TeamWare for work and uses the Copilot SDK to perform it. |
| Agent Identity | A configured profile within the agent process, corresponding to a single Agent User in TeamWare. Each identity has its own PAT, system prompt, working directory, and MCP servers. |
| Copilot CLI | The standalone `copilot` binary distributed by GitHub. The Copilot SDK manages its lifecycle via JSON-RPC. Not the `gh` CLI. |
| Copilot SDK | The `GitHub.Copilot.SDK` NuGet package. Provides `CopilotClient` and `SessionConfig` for embedding agentic AI workflows. |
| CopilotClient | The SDK class that manages the Copilot CLI process lifecycle over JSON-RPC. |
| Session | A single conversation with the LLM, created via `CopilotClient.CreateSessionAsync()`. Each task gets its own session. |
| Built-in Tools | First-party tools shipped with the Copilot CLI: `view`, `edit`, `grep`, `glob`, `bash`, git operations, and web requests. Available by default via `--allow-all`. |
| MCP | Model Context Protocol. The protocol used to connect to TeamWare's tool server. |
| PAT | Personal Access Token. The authentication mechanism used by agent identities to access the TeamWare MCP endpoint. |
| BYOK | Bring Your Own Key. The Copilot SDK's support for using API keys from OpenAI, Azure AI Foundry, or Anthropic instead of a GitHub Copilot subscription. |
| Dry Run | A configuration mode where the agent reads tasks and reasons about them but does not call any write tools. |
| Kill Switch | The `IsActive = false` flag on an Agent User in TeamWare. Causes the agent identity to stop processing on its next polling cycle. |

### 1.4 Design Principles

- **Separate process, not part of TeamWare** — The agent is a standalone .NET console application. TeamWare remains a pure web application with no new external dependencies. The agent is a client that connects to TeamWare's MCP endpoint.
- **Single agent, not sub-agents** — Each agent identity is a single LLM session with a well-crafted system prompt and access to all tools. There is no sub-agent routing. The system prompt controls behavior; the LLM decides which tools to invoke.
- **Human oversight** — The agent moves tasks to InReview, never to Done. It posts comments explaining its work. Humans review and approve. The agent cannot create tasks, delete tasks, delete comments, or reassign tasks.
- **Simplicity** — Polling over webhooks. One task at a time per identity. No rate limiting, no idle detection, no sub-agent routing. The LLM handles build/test discovery. The system prompt is the primary tuning parameter.
- **Fail safely** — Errors move tasks to Error status with a comment. Unclear tasks move to Blocked status with a comment. The agent never retries failed tasks. Humans triage failures.

---

## 2. Technology Stack

| Layer | Technology | Purpose |
|-------|-----------|---------|
| Runtime | .NET 10 Console Application | Process host |
| LLM Integration | `GitHub.Copilot.SDK` NuGet package | CopilotClient, sessions, tool execution |
| LLM Inference | GitHub Copilot API (or BYOK provider) | LLM reasoning for code analysis and generation |
| Agent Runtime | Copilot CLI binary | Built-in tools (view, edit, grep, glob, bash, git) via JSON-RPC |
| TeamWare Integration | MCP over HTTP | Task management, comments, lounge, activity tools via PAT authentication |
| Configuration | `appsettings.json` / environment variables | Agent identity configuration |
| Hosting | systemd / Docker / terminal | Long-lived daemon process |

### 2.1 External Dependencies

| Dependency | Required | Notes |
|------------|----------|-------|
| GitHub Copilot subscription | Yes (unless BYOK) | Required for the Copilot CLI to access GitHub's LLM API |
| BYOK API keys | Alternative to Copilot subscription | OpenAI, Azure AI Foundry, or Anthropic API keys configured in the SDK |
| Network: TeamWare MCP endpoint | Yes | Local network or same host. HTTP connection to TeamWare's `/mcp` endpoint |
| Network: GitHub Copilot API | Yes (unless BYOK) | Internet access to `api.githubcopilot.com` |
| Network: BYOK provider | If using BYOK | Internet access to the configured LLM provider's API endpoint |
| Git | Yes | The Copilot CLI's built-in git tools require git to be installed on the host |

### 2.2 Relationship to TeamWare

The agent process is a client of TeamWare. It interacts with TeamWare exclusively through the MCP endpoint using PAT authentication. The agent process does not share a database, filesystem, or process space with TeamWare.

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

---

## 3. Functional Requirements

### 3.1 Process Lifecycle

| ID | Requirement |
|----|------------|
| CA-01 | The agent shall be a standalone .NET 10 console application (`TeamWare.Agent`) that runs as a long-lived daemon process |
| CA-02 | The agent shall support graceful shutdown via `CancellationToken` and SIGTERM. When shutdown is requested, the agent shall finish its current task (or abandon it cleanly with an Error status comment) before exiting |
| CA-03 | The agent shall be deployable as a systemd service, Docker container, or foreground terminal process |
| CA-04 | The agent shall not be a scheduled job (no cron, no Windows Task Scheduler). The polling loop runs inside the process with `Task.Delay` between cycles |

### 3.2 Agent Identity Configuration

| ID | Requirement |
|----|------------|
| CA-10 | The agent shall accept an array of agent identity configurations, each defining an independent agent profile |
| CA-11 | Each agent identity configuration shall include the following required fields: `Name` (display name), `WorkingDirectory` (local filesystem path), `PersonalAccessToken` (PAT for TeamWare MCP authentication), and `McpServers` (array of MCP server connections, at least one pointing to TeamWare) |
| CA-12 | Each agent identity configuration shall include the following optional fields: `RepositoryUrl`, `RepositoryBranch`, `RepositoryAccessToken`, `PollingIntervalSeconds` (default: 60), `Model` (LLM model name, default determined by SDK), `AutoApproveTools` (boolean, default: `true`), `SystemPrompt` (agent behavior instructions), and `DryRun` (boolean, default: `false`) |
| CA-13 | Each MCP server entry shall include: `Name`, `Type` (`http` or `stdio`), `Url` (for HTTP servers), and `AuthHeader` (for authenticated servers) |
| CA-14 | The agent shall support configuration via `appsettings.json`, environment variables, or any standard .NET configuration provider |

### 3.3 Multiple Agent Identities

| ID | Requirement |
|----|------------|
| CA-20 | The agent process shall run an independent polling loop per configured agent identity |
| CA-21 | Each agent identity shall process one task at a time. The identity shall finish (or explicitly defer) its current task before picking up the next one |
| CA-22 | Agent identities within the same process shall not share state. Each identity has its own `CopilotClient`, working directory, PAT, and polling interval |
| CA-23 | Different agent identities may have different system prompts, models, working directories, and MCP server configurations. This allows specialization (e.g., one identity for backend .NET, another for frontend JavaScript, another for documentation) |

### 3.4 Polling and Task Discovery

| ID | Requirement |
|----|------------|
| CA-30 | At the start of each polling cycle, the agent identity shall call `get_my_profile` via the TeamWare MCP endpoint to verify its identity and check its active status |
| CA-31 | If `get_my_profile` returns `isAgentActive = false`, the agent identity shall log a message and skip the current cycle without processing any tasks. The identity continues polling at its normal interval in case the admin re-enables it |
| CA-32 | If `get_my_profile` succeeds and the agent is active, the identity shall call `my_assignments` to discover tasks assigned to it |
| CA-33 | The identity shall filter `my_assignments` results to tasks with status `ToDo`. Tasks in other statuses are not picked up |
| CA-34 | If multiple `ToDo` tasks are returned, the identity shall process them one at a time in the order returned by `my_assignments` |
| CA-35 | After processing all available tasks (or if no tasks are available), the identity shall wait for `PollingIntervalSeconds` before the next cycle |
| CA-36 | The polling interval shall remain constant regardless of whether tasks are available. There shall be no adaptive polling (no backoff when idle, no acceleration when busy) |

### 3.5 Task Processing Pipeline

| ID | Requirement |
|----|------------|
| CA-40 | For each task, the agent identity shall create a new Copilot SDK session via `CopilotClient.CreateSessionAsync()` |
| CA-41 | The session shall be configured with: the identity's model selection, the identity's system prompt (appended to the SDK's default via `SystemMessageMode.Append`), all configured MCP servers, and the identity's permission handler |
| CA-42 | The agent shall construct a task prompt containing the task's ID, title, description, priority, current status, project name, and existing comments. This prompt is sent to the session via `SendAndWaitAsync` |
| CA-43 | The LLM shall have access to all Copilot CLI built-in tools and all tools from configured MCP servers. No tools shall be artificially restricted at the SDK level |
| CA-44 | The Copilot CLI's `Cwd` (working directory) shall be set to the identity's `WorkingDirectory`. All file operations occur within this directory |
| CA-45 | The LLM shall discover and execute build and test commands through reasoning (exploring project structure, finding `*.sln`, `Makefile`, `package.json`, etc.) using built-in tools. No explicit `BuildCommand` or `TestCommand` configuration is required |

### 3.6 Repository Management

| ID | Requirement |
|----|------------|
| CA-50 | If `RepositoryUrl` is configured on the agent identity, the agent process (not the LLM) shall ensure the repository is cloned into `WorkingDirectory` before creating a session |
| CA-51 | If `RepositoryUrl` is configured and the repository already exists in `WorkingDirectory`, the agent process shall pull the latest changes from `RepositoryBranch` (defaulting to the repository's default branch) before each task |
| CA-52 | If `RepositoryAccessToken` is configured, the agent process shall use it for clone and pull operations (supporting private repositories) |
| CA-53 | If `RepositoryUrl` is not configured, the agent process shall assume `WorkingDirectory` already contains the codebase. No clone or pull operations are performed |
| CA-54 | Git branching, committing, and pushing during task execution are handled by the Copilot CLI's built-in git tools under LLM control — not by the agent process |

### 3.7 Status Transitions

| ID | Requirement |
|----|------------|
| CA-60 | When the agent picks up a task, it shall update the task status from `ToDo` to `InProgress` via the `update_task_status` MCP tool |
| CA-61 | When the agent successfully completes its work on a task, it shall post a summary comment via `add_comment` and then update the task status from `InProgress` to `InReview` |
| CA-62 | The agent shall never set a task status to `Done`. Only human users may approve agent work |
| CA-63 | When the agent determines a task is unclear, ambiguous, missing acceptance criteria, or otherwise lacks sufficient information to proceed, it shall post a comment explaining what information it needs and update the task status to `Blocked` |
| CA-64 | When the agent encounters an unrecoverable error during task processing (API error, LLM timeout, build failure it cannot resolve, etc.), it shall post a comment describing the error and update the task status to `Error` |
| CA-65 | The agent shall always post a comment before changing task status. The comment provides context for the status change |
| CA-66 | After moving a task to `Blocked`, `Error`, or `InReview`, the agent shall proceed to the next task. The agent does not retry failed or blocked tasks |

The following table summarizes all valid agent-initiated status transitions:

| Current Status | Agent Action | New Status |
|---------------|-------------|------------|
| ToDo | Agent picks up the task | InProgress |
| InProgress | Agent completes its work | InReview |
| InProgress | Agent encounters an error | Error |
| InProgress | Agent needs clarification | Blocked |

The following transitions are human-initiated and are not performed by the agent:

| Current Status | Human Action | New Status |
|---------------|-------------|------------|
| InReview | Human reviews and approves | Done |
| InReview | Human requests changes | ToDo (re-queued for agent) |
| Error | Human triages the failure | ToDo or Done |
| Blocked | Human provides clarification | ToDo (re-queued for agent) |

### 3.8 Reporting and Communication

| ID | Requirement |
|----|------------|
| CA-70 | The agent shall post a comment on the task via `add_comment` summarizing what it accomplished before moving the task to `InReview`. The comment shall describe the changes made, files modified, and any relevant build/test results |
| CA-71 | The agent shall post a comment on the task before moving it to `Blocked`, explaining exactly what information is missing or what clarification is needed |
| CA-72 | The agent shall post a comment on the task before moving it to `Error`, including any error messages, the step it was on, and what it had accomplished up to that point |
| CA-73 | When the agent moves a task to `Blocked`, it shall post a message to the project lounge via `post_lounge_message` indicating it needs help and directing team members to the task comment for details |
| CA-74 | When the agent moves a task to `Error`, it shall post a message to the project lounge via `post_lounge_message` indicating it encountered a problem and directing team members to the task comment for details |
| CA-75 | Lounge messages shall be posted to the project lounge (the project the task belongs to), not the global lounge |
| CA-76 | Lounge messages shall be plain text with no icons, emoticons, or decorative formatting. The agent user's bot avatar provides visual distinction |
| CA-77 | The agent shall not post lounge messages for successful task completions (InReview transitions), task pickups (ToDo → InProgress), or idle/startup announcements. The lounge is reserved for situations requiring human attention |

### 3.9 System Prompt

| ID | Requirement |
|----|------------|
| CA-80 | The system prompt shall be the primary mechanism for controlling agent behavior. It defines how the agent approaches tasks, what steps to follow, and what rules to obey |
| CA-81 | The system prompt shall be configurable per agent identity. Different identities may have different prompts optimized for different types of work |
| CA-82 | The default system prompt shall instruct the agent to: (1) read task details and comments, (2) assess scope and request breakdown if too broad, (3) explore the codebase, (4) make minimal targeted changes, (5) run build and test commands, (6) commit to a feature branch, (7) post a summary comment, and (8) update the task status to InReview |
| CA-83 | The default system prompt shall include the following rules: never set a task to Done; never create or delete tasks; never reassign tasks to other users; never delete comments; always post a comment before changing task status; commit to a feature branch named `agent/<task-id>`; move unclear tasks to Blocked; move too-large tasks to Blocked with a breakdown recommendation |

The reference default system prompt is:

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

---

## 4. Safety and Guardrails

### 4.1 Action Restrictions

| ID | Requirement |
|----|------------|
| CA-90 | The agent shall not create tasks via the `create_task` MCP tool. It works only on tasks explicitly assigned to it by human users |
| CA-91 | The agent shall not delete tasks. No MCP tool for task deletion exists, and the agent shall not attempt deletion through other means |
| CA-92 | The agent shall not delete comments. The agent may only add comments via `add_comment` |
| CA-93 | The agent shall not reassign tasks to other users via the `assign_task` MCP tool. The agent works on its own assignments and does not delegate work |
| CA-94 | The agent shall not move tasks to `Done` status. Only human users may approve and close agent work |

### 4.2 Behavioral Guardrails

| ID | Requirement |
|----|------------|
| CA-100 | **Read-before-write** — The agent shall read the current task state (via `get_task`) before making any changes. This prevents stale-state updates |
| CA-101 | **Comment-before-status** — The agent shall post a comment explaining its action before changing task status. This creates an audit trail for every status transition |
| CA-102 | **Scope awareness** — The agent shall assess whether a task is appropriately scoped before starting work. If a task is too broad or would require changes across too many files or systems, the agent shall post a comment explaining why the task should be broken down and move it to Blocked. The LLM makes this judgment — there is no hard-coded file or line limit |
| CA-103 | **Feature branches only** — The agent shall commit changes to a feature branch (e.g., `agent/task-42`), never to main or master. The system prompt enforces this; the `OnPermissionRequest` handler may additionally inspect git commands to verify |

### 4.3 Kill Switch

| ID | Requirement |
|----|------------|
| CA-110 | Setting `IsActive = false` on the agent user in TeamWare shall cause the corresponding agent identity to stop processing tasks on its next polling cycle |
| CA-111 | The kill switch is checked by calling `get_my_profile` at the start of each polling cycle (see CA-30, CA-31) |
| CA-112 | Additionally, `PatAuthenticationHandler` in TeamWare rejects all MCP requests from paused agents. This provides a server-side enforcement layer even if the agent process does not check `get_my_profile` |
| CA-113 | The kill switch is reversible. Setting `IsActive = true` re-enables the agent identity on its next polling cycle |

### 4.4 Dry Run Mode

| ID | Requirement |
|----|------------|
| CA-120 | The agent shall support a dry run mode, configurable per agent identity via the `DryRun` configuration flag |
| CA-121 | In dry run mode, the agent shall execute the full pipeline — polling, task discovery, session creation, LLM reasoning — but shall not call any write tools (MCP write tools or Copilot CLI write tools) |
| CA-122 | In dry run mode, the agent shall log all tool calls that would have been made, including the tool name, parameters, and the LLM's reasoning for invoking the tool |
| CA-123 | Dry run mode is the recommended default for initial deployment and prompt tuning |

### 4.5 Permission Handling

| ID | Requirement |
|----|------------|
| CA-130 | When `AutoApproveTools` is `true` (the default), the agent shall use `PermissionHandler.ApproveAll` to allow all tool invocations without prompting |
| CA-131 | When `AutoApproveTools` is `false`, the agent shall use a custom permission handler that can inspect tool calls before approving them. This handler may enforce additional guardrails such as blocking commits to protected branches |
| CA-132 | The permission handler configuration is independent of dry run mode. In dry run mode, write tools are blocked regardless of the permission handler setting |

---

## 5. Error Handling

### 5.1 Task-Level Errors

| ID | Requirement |
|----|------------|
| CA-140 | When the agent encounters an unrecoverable error during task processing, it shall post a comment on the task describing the error and move the task to `Error` status (see CA-64) |
| CA-141 | The agent shall not retry failed tasks. After moving a task to `Error`, it proceeds to the next task. A human must triage the error and move the task back to `ToDo` if the agent should retry |
| CA-142 | Error comments shall include: the error message or exception details, the step the agent was on when the error occurred, and a summary of what the agent had accomplished up to that point |

### 5.2 Infrastructure Errors

| ID | Requirement |
|----|------------|
| CA-150 | If the agent cannot reach the TeamWare MCP endpoint during a polling cycle (network error, server down), it shall log the error and wait for the next polling cycle. It shall not crash |
| CA-151 | If the Copilot CLI process fails to start or crashes, the `CopilotClient` shall handle the error. The agent shall log the error and attempt to recreate the client on the next polling cycle |
| CA-152 | If the LLM provider returns an error (rate limit, authentication failure, service outage), the agent shall treat it as a task-level error for the current task (CA-140) and continue polling |

### 5.3 Stuck Detection

| ID | Requirement |
|----|------------|
| CA-160 | Stuck detection is not the agent's responsibility. The agent does not monitor how long tasks have been in a given status |
| CA-161 | Stuck detection shall be implemented as a background job in TeamWare.Web (e.g., a Hangfire job) that periodically scans for tasks assigned to agent users that have been in `InProgress` beyond a configurable threshold |
| CA-162 | The stuck detection job may post a warning comment, send a notification to the project owner, or auto-transition the task to `Error` |

---

## 6. Lounge Integration

### 6.1 When to Post

| ID | Requirement |
|----|------------|
| CA-170 | The agent shall post to the project lounge only when it needs human intervention: `Blocked` and `Error` task transitions |
| CA-171 | The agent shall not post lounge messages for: startup, shutdown, task pickup, task completion (InReview), idle periods, daily summaries, or any other informational purpose |

### 6.2 Message Format

| ID | Requirement |
|----|------------|
| CA-175 | Lounge messages shall be plain text. No icons, emoticons, or decorative markdown formatting |
| CA-176 | Blocked message format: "I need help with Task #`{id}` — `{title}`. I've posted a comment explaining what information I need. Can someone take a look?" |
| CA-177 | Error message format: "I ran into a problem on Task #`{id}` — `{title}`. I've posted a comment with the error details. Someone will need to triage this." |
| CA-178 | Lounge messages shall target the project lounge (the project the task belongs to), not the global lounge |

### 6.3 No Mention Detection

| ID | Requirement |
|----|------------|
| CA-180 | The agent shall not monitor the lounge for `@mentions` or respond to lounge messages. If someone wants the agent to do something, they assign it a task |

---

## 7. Configuration Model

### 7.1 Top-Level Configuration

```json
{
  "Agents": [
    {
      "Name": "CodeBot",
      "WorkingDirectory": "/home/agent/projects/teamware",
      "RepositoryUrl": "https://github.com/mufaka/TeamWare",
      "RepositoryBranch": "master",
      "RepositoryAccessToken": "ghp_xxxx",
      "PersonalAccessToken": "pat_codebot_xxxxxxxx",
      "PollingIntervalSeconds": 60,
      "Model": "gpt-4.1",
      "AutoApproveTools": true,
      "DryRun": false,
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

### 7.2 Configuration Fields

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `Name` | `string` | Yes | — | Display name for the agent identity. Used in logging |
| `WorkingDirectory` | `string` | Yes | — | Local filesystem path. Set as `Cwd` on `CopilotClientOptions`. The Copilot CLI operates on files within this directory |
| `RepositoryUrl` | `string` | No | `null` | Git repository URL. If set, the agent process clones/pulls the repo into `WorkingDirectory` before each task |
| `RepositoryBranch` | `string` | No | Repository default | Default branch to pull from |
| `RepositoryAccessToken` | `string` | No | `null` | Git credential for private repositories |
| `PersonalAccessToken` | `string` | Yes | — | TeamWare PAT for MCP authentication. Must match an Agent User in TeamWare |
| `PollingIntervalSeconds` | `int` | No | `60` | Seconds between polling cycles |
| `Model` | `string` | No | SDK default | LLM model name (e.g., `gpt-4.1`, `claude-sonnet-4`) |
| `AutoApproveTools` | `bool` | No | `true` | If `true`, use `PermissionHandler.ApproveAll`. If `false`, use a custom permission handler |
| `DryRun` | `bool` | No | `false` | If `true`, the agent reads and reasons but does not call write tools |
| `SystemPrompt` | `string` | No | See CA-82/CA-83 | Custom system prompt. Appended to the SDK default via `SystemMessageMode.Append` |
| `McpServers` | `array` | Yes | — | Array of MCP server configurations. At least one must point to TeamWare |

### 7.3 MCP Server Configuration Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `Name` | `string` | Yes | Identifier for the MCP server |
| `Type` | `string` | Yes | `http` for remote HTTP MCP servers, `stdio` for local stdio MCP servers |
| `Url` | `string` | Yes (for `http`) | MCP server endpoint URL |
| `AuthHeader` | `string` | No | Authorization header value (e.g., `Bearer pat_xxxx`) |

---

## 8. Tool Access

### 8.1 Built-in Tools (Copilot CLI)

The Copilot CLI provides all tools needed for code interaction. These are available by default:

| Tool | Purpose |
|------|---------|
| `view` / `read_file` | Read file contents |
| `edit` / `edit_file` | Modify files |
| `grep` | Search for patterns in code |
| `glob` | Find files by pattern |
| `bash` | Execute shell commands (build, test, etc.) |
| Git operations | Branch, commit, push, etc. |
| Web requests | HTTP capabilities |

No external filesystem MCP server is needed. The `Cwd` option on `CopilotClientOptions` controls where the CLI operates.

### 8.2 TeamWare MCP Tools

The agent connects to TeamWare's MCP endpoint for task management and team communication:

| Tool | Purpose |
|------|---------|
| `get_my_profile` | Verify identity and check active status |
| `my_assignments` | Discover assigned tasks |
| `get_task` | Read task details |
| `update_task_status` | Change task status |
| `add_comment` | Post comments on tasks |
| `list_tasks` | List tasks in a project |
| `get_project_summary` | Project-level statistics and activity |
| `get_activity` | Recent activity entries |
| `post_lounge_message` | Post to the project lounge |
| `list_lounge_messages` | Read lounge messages |
| `search_lounge_messages` | Search lounge messages |

### 8.3 Additional MCP Servers (Operator-Configured)

Operators may configure additional MCP servers per agent identity for capabilities beyond TeamWare and code interaction. Examples include:

- **GitHub MCP server** (`https://api.githubcopilot.com/mcp/`) for pull request creation, issue management, and repository metadata
- **Database MCP servers** for data access
- **Deployment MCP servers** for CI/CD operations

No tools are artificially restricted. If an operator wants to limit agent behavior, they control it through the system prompt and the MCP servers they configure.

---

## 9. Changes to TeamWare (Cross-References)

The following changes to TeamWare.Web are required to fully support the Copilot Agent. These changes should be specified and implemented in their respective areas:

### 9.1 New Task Statuses

| Change | Description | Current State |
|--------|-------------|---------------|
| Add `Blocked` status to `TaskItemStatus` enum | Value `4`. Used when the agent (or a human) cannot proceed without additional information | Not yet implemented. Current values: ToDo (0), InProgress (1), InReview (2), Done (3) |
| Add `Error` status to `TaskItemStatus` enum | Value `5`. Used when the agent encounters an unrecoverable error during task processing | Not yet implemented |

These new statuses must be supported throughout the TeamWare web application: task list views, task detail views, status filter dropdowns, Kanban boards (if applicable), activity log formatting, and MCP tool responses.

### 9.2 Stuck Detection Background Job

| Change | Description |
|--------|-------------|
| Hangfire background job for stuck task detection | Periodically scans for tasks assigned to agent users that have been in `InProgress` beyond a configurable threshold. Posts warning comment, notifies project owner, or auto-transitions to `Error` |

### 9.3 Existing Infrastructure (No Changes Required)

The following TeamWare infrastructure is used by the agent as-is:

| Component | Usage |
|-----------|-------|
| MCP Server and HTTP transport | Agent connects via HTTP MCP |
| PAT authentication | Agent authenticates with PAT |
| Agent Users (`IsAgent`, `IsActive`) | Agent identity verification and kill switch |
| `get_my_profile` MCP tool | Identity and active status check |
| `my_assignments` MCP tool (agent filtering) | Task discovery (ToDo/InProgress only for agents) |
| All existing MCP tools | Task management, comments, lounge, activity |

---

## 10. Non-Functional Requirements

| ID | Requirement |
|----|------------|
| CA-NF-01 | The agent process shall have no effect on TeamWare's performance or availability. It is an external client. A misbehaving agent cannot degrade the web experience for human users |
| CA-NF-02 | The agent process shall log all significant events: polling cycles, task pickups, status transitions, tool invocations, errors, and shutdown events. Logs shall use structured logging (e.g., `ILogger` / Serilog) |
| CA-NF-03 | The agent process shall be stateless between polling cycles. All task state is stored in TeamWare. If the agent process crashes and restarts, it resumes from the current task state without data loss |
| CA-NF-04 | Token/cost awareness is the operator's responsibility. The agent process does not track, log, or limit its own LLM token consumption. Operators monitor usage via the LLM provider's dashboard or billing API |
| CA-NF-05 | The agent process shall handle concurrent identities efficiently. Each identity runs an independent async loop; identities do not block each other |
| CA-NF-06 | The agent shall be idempotent. Re-running the agent against the same task list shall produce no harmful side effects. If a task is already in `InProgress`, `InReview`, `Blocked`, `Error`, or `Done` status, the agent shall skip it |

---

## 11. Security Considerations

| ID | Consideration |
|----|--------------|
| CA-SEC-01 | The agent authenticates to TeamWare exclusively via PAT. No interactive login, no session cookies, no API keys stored in TeamWare's database |
| CA-SEC-02 | PATs shall be stored securely in the agent's configuration. They shall not be logged, committed to source control, or included in error messages |
| CA-SEC-03 | The `RepositoryAccessToken` (for private git repos) shall follow the same security practices as PATs |
| CA-SEC-04 | Prompt injection is mitigated by action restrictions, not detection. The agent cannot create or delete tasks, cannot delete comments, and cannot reassign tasks to other users. These restrictions limit the blast radius of any successful injection to the agent's own assigned work. The Copilot SDK's built-in safety filters provide an additional layer. Dedicated prompt injection detection is out of scope and will be addressed separately if needed |
| CA-SEC-05 | The agent is subject to the same project membership authorization as human users. It can only access projects it has been explicitly added to in TeamWare |
| CA-SEC-06 | The `OnPermissionRequest` handler provides an additional enforcement layer. When `AutoApproveTools` is `false`, the handler can block dangerous operations (e.g., `rm -rf`, force pushes, commits to protected branches) before they execute |
| CA-SEC-07 | The kill switch (`IsActive = false`) provides immediate suspension at two levels: the agent's own `get_my_profile` check (client-side) and `PatAuthenticationHandler`'s rejection of paused agents (server-side) |

---

## 12. Testing Requirements

### 12.1 Unit Tests

| ID | Requirement |
|----|------------|
| CA-TEST-01 | Unit tests shall verify that the polling loop calls `get_my_profile` at the start of each cycle and skips processing when `isAgentActive` is `false` |
| CA-TEST-02 | Unit tests shall verify that the polling loop calls `my_assignments` and filters for `ToDo` tasks only |
| CA-TEST-03 | Unit tests shall verify that the task processing pipeline creates a Copilot session with the correct model, system prompt, and MCP server configuration |
| CA-TEST-04 | Unit tests shall verify that the task prompt includes all required fields: task ID, title, description, priority, status, project name, and comments |
| CA-TEST-05 | Unit tests shall verify that the agent posts a comment before every status transition |
| CA-TEST-06 | Unit tests shall verify correct status transitions: ToDo → InProgress, InProgress → InReview, InProgress → Error, InProgress → Blocked |
| CA-TEST-07 | Unit tests shall verify that the agent posts a lounge message only for Blocked and Error transitions, and that the message targets the project lounge |
| CA-TEST-08 | Unit tests shall verify that dry run mode prevents all write tool invocations while still executing the read/reasoning pipeline |
| CA-TEST-09 | Unit tests shall verify that infrastructure errors (network, Copilot CLI crash) are handled gracefully without crashing the polling loop |
| CA-TEST-10 | Unit tests shall verify that the agent skips tasks not in `ToDo` status (idempotency) |

### 12.2 Integration Tests

| ID | Requirement |
|----|------------|
| CA-TEST-20 | Integration tests shall verify end-to-end authentication: agent identity → PAT → TeamWare MCP endpoint → `get_my_profile` |
| CA-TEST-21 | Integration tests shall verify the full task lifecycle: `my_assignments` → session → status transitions → comments → lounge messages |
| CA-TEST-22 | Integration tests shall verify that the kill switch (`IsActive = false`) prevents task processing at both the client and server level |
| CA-TEST-23 | Integration tests shall verify that multiple agent identities can run concurrently within the same process without interference |
| CA-TEST-24 | Integration tests shall use a dedicated "Sandbox" project in TeamWare with test tasks |

### 12.3 Testing Strategy

| ID | Requirement |
|----|------------|
| CA-TEST-30 | Copilot SDK responses shall be mockable for unit and integration tests, isolating pipeline logic from LLM quality |
| CA-TEST-31 | The TeamWare MCP endpoint shall be testable using a test server instance with seeded data |
| CA-TEST-32 | Dry run mode shall be the default for all initial deployments and prompt tuning iterations |
| CA-TEST-33 | A dedicated "Sandbox" project shall be created in TeamWare for safe agent testing. Mistakes in this project have no impact on real work |
| CA-TEST-34 | Prompt engineering shall be iterated based on observed agent behavior in the sandbox project, using dry run mode to validate reasoning before enabling writes |

### 12.4 Rollout Strategy

| ID | Requirement |
|----|------------|
| CA-TEST-40 | Initial deployment shall use dry run mode exclusively. The agent reads tasks, reasons about them, and logs planned actions without executing writes |
| CA-TEST-41 | After dry run validation, the agent shall be tested on simple, low-risk tasks (documentation updates, test additions) in the sandbox project |
| CA-TEST-42 | Task complexity shall be increased gradually as confidence in agent behavior grows |
| CA-TEST-43 | Production use on real project tasks shall only begin after successful sandbox testing with writes enabled |

---

## 13. File Organization

```
TeamWare.Agent/
  Program.cs                              (entry point, host builder, configuration)
  AgentHostedService.cs                   (IHostedService managing polling loops)
  Configuration/
    AgentIdentityOptions.cs               (strongly-typed config for one agent identity)
    McpServerOptions.cs                   (strongly-typed config for one MCP server entry)
  Pipeline/
    AgentPollingLoop.cs                   (per-identity polling loop: profile check, task discovery)
    TaskProcessor.cs                      (session creation, prompt construction, result handling)
    StatusTransitionHandler.cs            (comment + status update + lounge notification logic)
  Repository/
    RepositoryManager.cs                  (clone/pull logic for WorkingDirectory setup)
  Permissions/
    AgentPermissionHandler.cs             (custom OnPermissionRequest handler for non-auto-approve mode)
  Logging/
    DryRunLogger.cs                       (logs tool calls that would have been made in dry run mode)
TeamWare.Agent.Tests/
  Pipeline/
    AgentPollingLoopTests.cs
    TaskProcessorTests.cs
    StatusTransitionHandlerTests.cs
  Repository/
    RepositoryManagerTests.cs
  Permissions/
    AgentPermissionHandlerTests.cs
  Integration/
    EndToEndAgentTests.cs
```

---

## 14. Relationship to Other Specifications

| Specification | Relationship |
|---------------|-------------|
| [Main Specification](Specification.md) | The Copilot Agent is an external client of the system specified in the main specification. It interacts with TeamWare through the MCP endpoint, not through internal APIs. |
| [MCP Server Specification](McpServerSpecification.md) | The agent uses all MCP tools defined in this specification. PAT authentication, tool definitions, and response formats are governed by the MCP specification. |
| [Agent Users Specification](AgentUsersSpecification.md) | The agent authenticates as an Agent User defined in this specification. The `IsAgent`, `IsActive`, `get_my_profile`, and `my_assignments` filtering behaviors are all specified there. |
| [Ollama Integration Specification](OllamaIntegrationSpecification.md) | No relationship. The Ollama integration provides AI features for human users in the TeamWare web UI. The Copilot Agent uses the GitHub Copilot SDK for its own LLM inference. |

---

## 15. Out of Scope

The following items are explicitly out of scope for this specification:

| Item | Rationale |
|------|-----------|
| Pull request creation | Requires the GitHub MCP server (or Gitea equivalent). The agent pushes feature branches; PR creation is a future enhancement via an optional MCP server in configuration |
| Multi-repository tasks | Tasks spanning multiple repositories require switching `CopilotClient` working directories. This is a configuration concern for a future iteration |
| Webhook-based task notification | Polling is simpler and avoids adding infrastructure to TeamWare. Webhooks are a future performance optimization |
| Prompt injection detection | Mitigated by action restrictions (no create/delete/reassign). Dedicated detection is a separate concern |
| Token/cost tracking | Operator's responsibility, monitored externally via LLM provider dashboards |
| Mention detection in lounge | No clear use case beyond what task assignment already covers. Adds polling complexity for marginal value |
| Sub-agent routing | Over-engineered for the initial design. The SDK supports it if a future use case requires it |
| Code guardrails (max files, max lines) | The LLM's scope awareness (via system prompt) is the guardrail. Hard-coded limits cause mid-task failures. The `OnPermissionRequest` handler provides an escape hatch for operators who want branch protection enforcement |
