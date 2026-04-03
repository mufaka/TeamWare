# TeamWare Agent Architecture Blueprint

This document provides a comprehensive overview of how the TeamWare Agent interacts with the TeamWare MCP server. Its purpose is to serve as a **blueprint for building alternative agent implementations** in any language or framework that can communicate over MCP (Model Context Protocol).

---

## Table of Contents

1. [High-Level Architecture](#1-high-level-architecture)
2. [Core Concepts](#2-core-concepts)
3. [Authentication](#3-authentication)
4. [MCP Server Endpoint and Transport](#4-mcp-server-endpoint-and-transport)
5. [MCP Tools Reference](#5-mcp-tools-reference)
6. [Agent Lifecycle](#6-agent-lifecycle)
7. [Polling Loop — Step by Step](#7-polling-loop--step-by-step)
8. [Task Processing Pipeline](#8-task-processing-pipeline)
9. [Status Transition Protocol](#9-status-transition-protocol)
10. [Repository Management](#10-repository-management)
11. [LLM Integration](#11-llm-integration)
12. [Configuration and Server-Side Merge](#12-configuration-and-server-side-merge)
13. [Safety Guardrails](#13-safety-guardrails)
14. [Error Handling](#14-error-handling)
15. [Implementing Your Own Agent](#15-implementing-your-own-agent)
16. [Sequence Diagrams](#16-sequence-diagrams)

---

## 1. High-Level Architecture

The TeamWare system consists of two independently deployed components:

```
┌──────────────────────────────────────────────────────────────┐
│                     TeamWare.Web                             │
│  ASP.NET Core MVC + MCP Server                              │
│                                                              │
│  ┌──────────────┐  ┌────────────┐  ┌──────────────────────┐ │
│  │ Web UI       │  │ REST API   │  │ MCP Endpoint (/mcp)  │ │
│  │ (Browsers)   │  │ (optional) │  │ (Streamable HTTP)    │ │
│  └──────────────┘  └────────────┘  └──────────┬───────────┘ │
│                                                │             │
│  ┌─────────────────────────────────────────────┘             │
│  │  Services Layer (Projects, Tasks, Comments,               │
│  │  Lounge, Inbox, Activity, Agent Config)                   │
│  └─────────────────────────────────────────────┐             │
│                                                │             │
│  ┌──────────────┐  ┌──────────────┐            │             │
│  │ EF Core      │  │ SignalR Hubs │            │             │
│  │ (SQLite)     │  │ (real-time)  │            │             │
│  └──────────────┘  └──────────────┘            │             │
└────────────────────────────────────────────────┼─────────────┘
                                                 │
                        MCP over Streamable HTTP  │
                        (Bearer PAT auth)         │
                                                 │
┌────────────────────────────────────────────────┼─────────────┐
│                   TeamWare.Agent               │             │
│  .NET 10 Console Application                   │             │
│                                                │             │
│  ┌──────────────────────────────────────┐      │             │
│  │           AgentHostedService         │      │             │
│  │  (manages N agent identities)        │      │             │
│  ├──────────────────────────────────────┤      │             │
│  │  ┌────────────────────────────────┐  │      │             │
│  │  │     AgentPollingLoop (×N)      │◄─┼──────┘             │
│  │  │  ┌──────────────────────────┐  │  │                    │
│  │  │  │ TeamWareMcpClient        │  │  │ MCP tool calls     │
│  │  │  │ (MCP client library)     │──┼──┼───────────────►    │
│  │  │  └──────────────────────────┘  │  │                    │
│  │  │  ┌──────────────────────────┐  │  │                    │
│  │  │  │ StatusTransitionHandler  │  │  │                    │
│  │  │  │ RepositoryManager        │  │  │                    │
│  │  │  │ TaskProcessor            │  │  │                    │
│  │  │  └──────────────────────────┘  │  │                    │
│  │  └────────────────────────────────┘  │                    │
│  └──────────────────────────────────────┘                    │
│                                                              │
│  ┌──────────────────────────────────────┐                    │
│  │   GitHub Copilot SDK (LLM engine)    │                    │
│  │   + Additional MCP servers (tools)   │                    │
│  └──────────────────────────────────────┘                    │
└──────────────────────────────────────────────────────────────┘
```

**Key design principle:** The agent is a completely separate process. It shares no code, no database, and no in-memory state with TeamWare.Web. All interaction happens through the MCP endpoint using JSON-over-HTTP with PAT authentication.

---

## 2. Core Concepts

| Concept | Description |
|---------|-------------|
| **Agent User** | A user in TeamWare with `IsAgent = true`. Created by an admin. |
| **Personal Access Token (PAT)** | A Bearer token generated for the agent user. Used for all MCP calls. |
| **Agent Identity** | A local configuration block (name, working directory, PAT, MCP servers). Multiple identities can run in one process. |
| **Polling Loop** | Each identity runs an independent polling loop, checking for assigned tasks at a configurable interval. |
| **Task Statuses** | `ToDo` → `InProgress` → `InReview` → `Done` (also `Blocked`, `Error`). The agent only picks up `ToDo` tasks and never sets `Done`. |
| **MCP (Model Context Protocol)** | An open protocol for tool-use between LLMs and external systems. TeamWare exposes its API as MCP tools. |
| **System Prompt** | Instructions given to the LLM that govern agent behavior (what to do, what not to do). |

---

## 3. Authentication

All MCP communication uses **Bearer token authentication** with Personal Access Tokens (PATs).

### How It Works

1. An admin creates an **Agent User** in TeamWare (`Admin > Agent Users`)
2. A PAT is generated for that user
3. The agent includes the PAT in every MCP request:

```
Authorization: Bearer <pat-token>
```

### Server-Side Validation

TeamWare's `PatAuthenticationHandler` validates each request:

1. Extracts the Bearer token from the `Authorization` header
2. Validates the token against stored (hashed) PATs
3. Resolves the associated user
4. **Rejects inactive agents** — if the user has `IsAgent = true` but `IsAgentActive = false`, the request is denied with `"Agent is currently paused"`
5. Adds claims to the request context: `NameIdentifier`, `Name`, `Email`, `IsAgent`, and any roles

### Authentication for Alternative Implementations

Any agent implementation needs to:

1. Obtain a PAT from TeamWare's admin panel
2. Include `Authorization: Bearer <pat>` on every HTTP request to the MCP endpoint
3. Handle `401 Unauthorized` responses (expired or invalid PAT)
4. Handle `403 Forbidden` or authentication failure when the agent is paused

---

## 4. MCP Server Endpoint and Transport

### Endpoint

```
POST https://<teamware-host>/mcp
```

### Transport

TeamWare uses **Streamable HTTP** transport mode for MCP. This is the standard HTTP-based MCP transport where:

- Each tool call is an HTTP POST request
- Request and response bodies are JSON
- The MCP client library handles framing, session management, and content block parsing

### Content Format

MCP tool responses are returned as `TextContentBlock` objects containing JSON strings. The agent parses these as JSON to extract structured data.

### Error Responses

Tool calls can fail in two ways:

1. **MCP-level error**: The response has `IsError = true` and the text content describes the error
2. **Application-level error**: The response is valid JSON with an `"error"` property:
   ```json
   { "error": "You are not a member of this project." }
   ```

---

## 5. MCP Tools Reference

TeamWare exposes the following MCP tools, grouped by category. All tools require PAT authentication.

### Profile Tools

| Tool | Description | Parameters | Returns |
|------|-------------|------------|---------|
| `get_my_profile` | Get the authenticated user's profile including agent status and server-side configuration | _(none)_ | `{ userId, displayName, email, isAgent, agentDescription, isAgentActive, lastActiveAt, configuration? }` |

The `configuration` object (present only for agent users) contains server-side settings that can be merged with local configuration. See [Configuration and Server-Side Merge](#12-configuration-and-server-side-merge).

### Task Tools

| Tool | Description | Key Parameters | Returns |
|------|-------------|----------------|---------|
| `my_assignments` | Get the authenticated user's task assignments across all projects | _(none)_ | Array of `{ id, title, projectName, projectId, status, priority, dueDate, isOverdue, isNextAction }` |
| `get_task` | Get detailed task information including comments | `taskId: int` | `{ id, title, description, status, priority, dueDate, isNextAction, isSomedayMaybe, projectId, createdByUserId, createdAt, updatedAt, assignees[], comments[] }` |
| `list_tasks` | List tasks in a project with optional filters | `projectId: int`, `status?`, `priority?`, `assigneeId?` | Array of tasks |
| `create_task` | Create a new task in a project | `projectId: int`, `title: string`, `description?`, `priority?`, `dueDate?` | Created task object |
| `update_task_status` | Change a task's status | `taskId: int`, `status: string` | Updated task object |
| `assign_task` | Assign users to a task | `taskId: int`, `userIds: string[]` | Success message |
| `add_comment` | Add a comment to a task | `taskId: int`, `content: string` | Created comment object |

**Important for agents:** The `my_assignments` tool automatically filters results for agent users, returning only tasks with `ToDo` or `InProgress` status.

**Valid status values:** `ToDo`, `InProgress`, `InReview`, `Done`, `Blocked`, `Error`

**Valid priority values:** `Low`, `Medium`, `High`, `Critical`

### Project Tools

| Tool | Description | Key Parameters | Returns |
|------|-------------|----------------|---------|
| `list_projects` | List all projects the user is a member of | _(none)_ | Array of `{ id, name, description, status, memberCount }` |
| `get_project` | Get project details with task statistics | `projectId: int` | `{ id, name, description, status, taskStatistics, overdueTasks, upcomingDeadlines }` |

### Lounge Tools (Chat)

| Tool | Description | Key Parameters | Returns |
|------|-------------|----------------|---------|
| `list_lounge_messages` | List recent messages from a project or global lounge | `projectId?: int`, `count?: int` | Array of messages |
| `post_lounge_message` | Post a message to a lounge | `content: string`, `projectId?: int` | Created message |
| `search_lounge_messages` | Search messages by content | `query: string`, `projectId?: int` | Array of matching messages |

### Inbox Tools (GTD)

| Tool | Description | Key Parameters | Returns |
|------|-------------|----------------|---------|
| `my_inbox` | Get unprocessed inbox items | _(none)_ | Array of inbox items |
| `capture_inbox` | Capture a new inbox item | `title: string`, `description?` | Created item |
| `process_inbox_item` | Convert inbox item to task | `inboxItemId: int`, `projectId: int`, `priority: string` | Created task |

### Activity Tools

| Tool | Description | Key Parameters | Returns |
|------|-------------|----------------|---------|
| `get_activity` | Get activity log entries | `projectId?: int`, `period?: string` | Array of activity entries |
| `get_project_summary` | Get task statistics and activity counts | `projectId: int`, `period?: string` | Summary with statistics |

**Period values:** `today`, `this_week` (default), `this_month`

---

## 6. Agent Lifecycle

```
┌──────────────────────────────────────────────────────────────────┐
│                        Process Startup                           │
│                                                                  │
│  1. Load configuration (appsettings.json + env vars)             │
│  2. Register services (MCP client factory, Copilot factory)      │
│  3. Start AgentHostedService                                     │
│  4. For each configured agent identity:                          │
│     a. Create TeamWareMcpClient (connect to MCP endpoint)        │
│     b. Create AgentPollingLoop                                   │
│     c. Launch polling loop on background thread                  │
└──────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌──────────────────────────────────────────────────────────────────┐
│                     Polling Loop (per identity)                  │
│                                                                  │
│  while (!cancelled):                                             │
│    1. Execute polling cycle                                      │
│    2. Sleep for PollingIntervalSeconds                            │
│    3. On error: log and continue                                 │
└──────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌──────────────────────────────────────────────────────────────────┐
│                        Process Shutdown                          │
│                                                                  │
│  1. Cancel all polling loops                                     │
│  2. Wait for all loops to complete                               │
│  3. Dispose MCP clients                                          │
└──────────────────────────────────────────────────────────────────┘
```

---

## 7. Polling Loop — Step by Step

Each polling cycle executes the following steps:

### Step 1: Profile Check

```
Call: get_my_profile
```

Check `isAgentActive`. If `false`, skip the entire cycle. This allows admins to pause an agent without stopping the process.

### Step 2: Server-Side Configuration Merge

If the profile response includes a `configuration` object, merge those values into the local configuration. Local values always take precedence. See [Section 12](#12-configuration-and-server-side-merge) for merge rules.

### Step 3: Task Discovery

```
Call: my_assignments
```

Filter the returned tasks to only those with `status == "ToDo"`. If none found, the cycle ends.

### Step 4: Process Tasks (One at a Time)

For each `ToDo` task, execute the task processing pipeline (see [Section 8](#8-task-processing-pipeline)). Tasks are processed sequentially — never in parallel. This prevents resource contention and makes error handling predictable.

### Step 5: Wait

Sleep for `PollingIntervalSeconds` (default: 60), then repeat.

---

## 8. Task Processing Pipeline

For each task picked up from the `ToDo` queue:

```
┌─────────────────────────────────────────────────────────────────┐
│ 1. READ-BEFORE-WRITE (Idempotency Guard)                       │
│                                                                  │
│    Call: get_task(taskId)                                         │
│    If status ≠ "ToDo" → skip (another agent/human may have      │
│    picked it up since the list was fetched)                      │
└─────────────────────────────┬───────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│ 2. PICK UP                                                      │
│                                                                  │
│    Call: add_comment(taskId, "Starting work on this task.")      │
│    Call: update_task_status(taskId, "InProgress")                │
└─────────────────────────────┬───────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│ 3. RESOLVE REPOSITORY                                           │
│                                                                  │
│    Match task.projectName against configured Repositories.       │
│    Determine working directory and git repo to use.              │
└─────────────────────────────┬───────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│ 4. SYNC REPOSITORY                                              │
│                                                                  │
│    If repo URL configured: clone (first time) or pull (update)  │
│    If no repo URL: skip (no-op)                                 │
└─────────────────────────────┬───────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│ 5. LLM PROCESSING                                              │
│                                                                  │
│    Create LLM session with:                                     │
│    - System prompt (behavioral instructions)                     │
│    - Task context (title, description, comments, project name)  │
│    - Available tools (all configured MCP servers + built-ins)   │
│    - Working directory set to the resolved repo path            │
│    - Timeout (TaskTimeoutSeconds, default 600s)                 │
│                                                                  │
│    The LLM reasons, reads files, runs commands, makes changes,  │
│    commits code, etc. — using the tools available to it.        │
└─────────────────────────────┬───────────────────────────────────┘
                              │
                    ┌─────────┴─────────┐
                    │                   │
              Success ✓            Failure ✗
                    │                   │
                    ▼                   ▼
┌─────────────────────────┐  ┌──────────────────────────────────┐
│ 6a. COMPLETE            │  │ 6b. ERROR                        │
│                         │  │                                  │
│ add_comment(summary)    │  │ add_comment(error details)       │
│ update_task_status(     │  │ update_task_status("Error")      │
│   "InReview")           │  │ post_lounge_message(             │
│                         │  │   projectId, error notification) │
└─────────────────────────┘  └──────────────────────────────────┘
```

---

## 9. Status Transition Protocol

The agent follows strict rules about status transitions and always comments before changing status.

### Transition: Pick Up (`ToDo` → `InProgress`)

1. `add_comment(taskId, "Starting work on this task.")`
2. `update_task_status(taskId, "InProgress")`

### Transition: Complete (`InProgress` → `InReview`)

1. `add_comment(taskId, <summary of work done>)`
2. `update_task_status(taskId, "InReview")`

No lounge message is posted for successful completions.

### Transition: Block (`InProgress` → `Blocked`)

1. `add_comment(taskId, <reason for blocking>)`
2. `update_task_status(taskId, "Blocked")`
3. `post_lounge_message(projectId, "I need help with Task #<id> — <title>. I've posted a comment explaining what information I need. Can someone take a look?")`

### Transition: Error (`InProgress` → `Error`)

1. `add_comment(taskId, <error details>)`
2. `update_task_status(taskId, "Error")`
3. `post_lounge_message(projectId, "I ran into a problem on Task #<id> — <title>. I've posted a comment with the error details. Someone will need to triage this.")`

### Rules

- **Always comment before changing status** — this creates an audit trail
- **Never set a task to `Done`** — only humans approve work
- **Never create or delete tasks** — work on what you're assigned
- **Never reassign tasks** — agents don't change assignments
- **Lounge messages use plain text only** — no icons, emoticons, or decorative formatting

---

## 10. Repository Management

The agent can optionally manage git repositories before processing tasks.

### Resolution Logic

1. If the task's `ProjectName` matches an entry in the `Repositories` configuration (case-insensitive), use that entry's URL, branch, and access token. The working directory becomes `<WorkingDirectory>/<ProjectName>/`.
2. If no match, fall back to the flat `RepositoryUrl`/`RepositoryBranch`/`RepositoryAccessToken` fields with the base `WorkingDirectory`.
3. If no repository URL is configured at all, skip repository sync entirely.

### Clone vs. Pull

- If the resolved working directory does **not** contain a `.git` folder → `git clone --branch <branch> --single-branch <url> <dir>`
- If it **does** contain a `.git` folder → `git pull origin <branch>`

### Authentication

For private repositories, the access token is injected into the HTTPS URL:

```
https://github.com/org/repo.git  →  https://<token>@github.com/org/repo.git
```

### Token Redaction

All git command output is sanitized before logging to prevent token leakage. The pattern `https://<anything>@` is replaced with `https://***@`.

---

## 11. LLM Integration

The existing agent uses the **GitHub Copilot SDK** as its LLM engine, but this is an implementation detail. Any LLM that supports tool use can be substituted.

### Session Configuration

When processing a task, the agent creates an LLM session with:

| Parameter | Description |
|-----------|-------------|
| **System Prompt** | Instructions governing agent behavior (see below) |
| **Model** | Configurable (e.g., `gpt-4o`); falls back to SDK default |
| **Working Directory** | The resolved repository path for the current task |
| **MCP Servers** | All configured MCP servers are passed as tool sources |
| **Timeout** | Maximum session duration (`TaskTimeoutSeconds`, default 600s) |

### Task Prompt

The task prompt sent to the LLM is structured as:

```
You have been assigned the following task. Please complete it according to your instructions.

Task ID: <id>
Title: <title>
Project: <projectName>
Priority: <priority>
Status: <status>

Description:
<description>

Existing Comments:
  [<timestamp>] <authorName>: <content>
  ...
```

### Default System Prompt

The built-in system prompt instructs the LLM to:

1. Read the task details and existing comments
2. Assess scope — block overly broad tasks
3. Explore the codebase
4. Make minimal, targeted changes
5. Run builds and tests
6. Commit to a feature branch (`agent/<task-id>`)
7. Post a summary comment
8. Set status to `InReview`

And enforces rules:
- Cannot set `InReview` without committed changes
- Never set `Done`
- Never create/delete tasks
- Never reassign tasks
- Never delete comments
- Always comment before changing status
- Block unclear or oversized tasks

### MCP Server Pass-Through

The LLM session is configured with all MCP servers from the agent's configuration. This means the LLM can call TeamWare tools (via the TeamWare MCP server) and any additional tools (via other MCP servers like Gitea, databases, etc.) during its reasoning process.

Two types of MCP servers are supported:

| Type | Transport | Configuration |
|------|-----------|---------------|
| `http` | Streamable HTTP | `url`, optional `authHeader` |
| `stdio` / `local` | stdin/stdout | `command`, `args`, `env` |

---

## 12. Configuration and Server-Side Merge

### Local Configuration

Each agent identity is configured locally in `appsettings.json` or via environment variables prefixed with `TEAMWARE_AGENT_`.

### Server-Side Configuration

TeamWare administrators can set agent configuration through the admin panel (`Admin > Agent Users > Edit Agent`). These settings are delivered in the `configuration` field of the `get_my_profile` response.

### Merge Rules

On each polling cycle, after calling `get_my_profile`, the agent merges server-side configuration into local options. **Local values always take precedence:**

| Field | Server value applied when local is... |
|-------|--------------------------------------|
| `PollingIntervalSeconds` | Default (`60`) |
| `Model` | `null` |
| `AutoApproveTools` | Default (`true`) |
| `DryRun` | Default (`false`) |
| `TaskTimeoutSeconds` | Default (`600`) |
| `SystemPrompt` | `null` |
| `RepositoryUrl` | `null` |
| `RepositoryBranch` | `null` |
| `RepositoryAccessToken` | `null` |
| `Repositories` | Merged by `ProjectName` (case-insensitive); local entries win on collision, server-only entries are appended |
| `McpServers` | Merged by `Name` (case-insensitive); local entries win on collision, server-only entries are appended |
| `WorkingDirectory` | **Never overwritten** |
| `PersonalAccessToken` | **Never overwritten** |

### Server-Side Configuration Shape

The `configuration` object returned by `get_my_profile`:

```json
{
  "pollingIntervalSeconds": 30,
  "model": "gpt-4o",
  "autoApproveTools": true,
  "dryRun": false,
  "taskTimeoutSeconds": 900,
  "systemPrompt": "Custom instructions...",
  "repositoryUrl": "https://github.com/org/repo.git",
  "repositoryBranch": "main",
  "repositoryAccessToken": "ghp_...",
  "repositories": [
    {
      "projectName": "Frontend",
      "url": "https://github.com/org/frontend.git",
      "branch": "dev",
      "accessToken": "ghp_..."
    }
  ],
  "mcpServers": [
    {
      "name": "extra-tools",
      "type": "http",
      "url": "https://tools.example.com/mcp",
      "authHeader": "Bearer xyz"
    }
  ]
}
```

All fields are nullable — only non-null fields are applied.

---

## 13. Safety Guardrails

The existing agent provides three levels of safety:

### Level 1: Auto-Approve All (`AutoApproveTools = true`)

All tool calls are approved automatically. This is the default for production agents where the system prompt and task scope are trusted.

### Level 2: Permission Handler (`AutoApproveTools = false`)

A custom permission handler inspects each tool call before approving. Currently blocks:

- `rm -rf` / `rm -r /`
- `git push --force` / `git push -f`
- `git checkout main` / `git checkout master`
- `git merge main` / `git merge master`

All other operations (MCP calls, file writes, other shell commands) are approved by default.

### Level 3: Dry Run Mode (`DryRun = true`)

The most restrictive mode:

- **Read operations** execute normally (MCP reads, file reads)
- **Write operations** are blocked and logged (MCP writes, shell commands, file writes)
- Tool call details (kind, parameters, LLM intention) are recorded

This is the recommended starting point for new deployments.

### System Prompt as Primary Control

The system prompt is the primary mechanism for controlling agent behavior. SDK-level permission handlers are a safety net, not the primary control. The system prompt explicitly tells the LLM what it can and cannot do.

---

## 14. Error Handling

### Polling-Level Errors

- Errors during a polling cycle (e.g., MCP connection failure) are logged and the loop continues
- The agent never crashes on transient failures
- After an error, it waits the normal polling interval before retrying

### Task-Level Errors

- If task processing fails, the agent:
  1. Posts an error comment on the task
  2. Sets the task status to `Error`
  3. Posts a lounge message notifying the team
- Processing continues with the next task

### MCP Tool Errors

- Tool calls can return errors as either MCP-level errors (`IsError = true`) or application-level errors (`{ "error": "..." }`)
- Both are surfaced as `McpToolException` in the existing implementation
- Alternative implementations should check for both error patterns

### Idempotency

- The read-before-write pattern prevents duplicate processing: before transitioning a task, the agent re-reads it to confirm the status hasn't changed
- If another agent or human picks up the task between discovery and processing, the task is skipped

---

## 15. Implementing Your Own Agent

### Minimum Viable Agent

To build the simplest possible agent that interacts with TeamWare:

1. **Connect to the MCP endpoint** using any MCP client library (or raw HTTP)
2. **Authenticate** with a Bearer PAT
3. **Poll** on an interval:
   - Call `get_my_profile` → check `isAgentActive`
   - Call `my_assignments` → filter to `ToDo`
4. **Process each task**:
   - Call `get_task(taskId)` → verify still `ToDo`
   - Call `add_comment(taskId, "Starting work...")` + `update_task_status(taskId, "InProgress")`
   - Do your work (whatever your agent does)
   - Call `add_comment(taskId, summary)` + `update_task_status(taskId, "InReview")`
5. **Handle errors**:
   - Call `add_comment(taskId, error)` + `update_task_status(taskId, "Error")`
   - Call `post_lounge_message(projectId, notification)`

### MCP Client Options

| Approach | Description |
|----------|-------------|
| **MCP client library** | Use a library for your language (e.g., `ModelContextProtocol` for .NET, `mcp` for Python/TypeScript) |
| **Raw HTTP** | MCP over Streamable HTTP is just JSON-over-HTTP. You can use raw HTTP calls if no MCP library is available. |

### Using Raw HTTP Instead of MCP Libraries

If you don't want to use an MCP client library, you can call tools directly via the Streamable HTTP endpoint. The MCP Streamable HTTP transport uses JSON-RPC over HTTP POST:

```http
POST /mcp HTTP/1.1
Host: your-teamware-instance
Authorization: Bearer <pat>
Content-Type: application/json

{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "my_assignments",
    "arguments": {}
  }
}
```

Refer to the [MCP specification](https://modelcontextprotocol.io/) for the complete Streamable HTTP transport protocol details.

### Non-LLM Agents

You don't need an LLM to build a TeamWare agent. Examples of non-LLM agents:

- **CI/CD Agent**: Picks up tasks, runs build/test pipelines, reports results
- **Deployment Agent**: Picks up deployment tasks, executes deployment scripts
- **Triage Agent**: Reads task descriptions, applies labels or priority based on rules
- **Notification Agent**: Monitors tasks and sends notifications to external systems (Slack, email)
- **Metric Agent**: Collects project statistics and posts summaries to the lounge

### Language-Agnostic Checklist

| Requirement | Implementation |
|-------------|---------------|
| HTTP client | Any HTTP library (curl, requests, fetch, HttpClient) |
| JSON parsing | Any JSON library |
| Timer/scheduler | Polling loop, cron, or scheduled task |
| PAT storage | Environment variable, secrets manager, or config file |
| Error handling | Log errors, continue polling, never crash on transient failures |
| Idempotency | Always re-read task status before transitioning |
| Comment-before-status | Always post a comment before calling `update_task_status` |

### Example: Minimal Python Agent (Pseudocode)

```python
import time
import requests

BASE_URL = "https://your-teamware-instance/mcp"
PAT = "your-pat-token"
HEADERS = {"Authorization": f"Bearer {PAT}"}
POLL_INTERVAL = 60

def call_tool(name, arguments=None):
    """Call an MCP tool via Streamable HTTP."""
    # Use your MCP client library here, or raw HTTP
    ...

def main():
    while True:
        try:
            # Step 1: Check if active
            profile = call_tool("get_my_profile")
            if not profile.get("isAgentActive"):
                time.sleep(POLL_INTERVAL)
                continue

            # Step 2: Get assignments
            assignments = call_tool("my_assignments")
            todo_tasks = [t for t in assignments if t["status"] == "ToDo"]

            # Step 3: Process each task
            for task in todo_tasks:
                task_detail = call_tool("get_task", {"taskId": task["id"]})
                if task_detail["status"] != "ToDo":
                    continue  # Idempotency check

                # Pick up
                call_tool("add_comment", {"taskId": task["id"], "content": "Starting work."})
                call_tool("update_task_status", {"taskId": task["id"], "status": "InProgress"})

                try:
                    # ... do your work here ...

                    # Complete
                    call_tool("add_comment", {"taskId": task["id"], "content": "Done."})
                    call_tool("update_task_status", {"taskId": task["id"], "status": "InReview"})
                except Exception as e:
                    call_tool("add_comment", {"taskId": task["id"], "content": f"Error: {e}"})
                    call_tool("update_task_status", {"taskId": task["id"], "status": "Error"})
                    call_tool("post_lounge_message", {
                        "projectId": task["projectId"],
                        "content": f"Error on Task #{task['id']} — {task['title']}."
                    })

        except Exception as e:
            print(f"Polling error: {e}")

        time.sleep(POLL_INTERVAL)
```

---

## 16. Sequence Diagrams

### Normal Task Processing

```
Agent                          TeamWare MCP
  │                                │
  │──── get_my_profile ──────────►│
  │◄─── { isAgentActive: true } ──│
  │                                │
  │──── my_assignments ──────────►│
  │◄─── [{ id:42, status:"ToDo" }]│
  │                                │
  │──── get_task(42) ────────────►│
  │◄─── { status:"ToDo", ... } ───│
  │                                │
  │──── add_comment(42, "Start") ►│
  │◄─── { id:100, ... } ──────────│
  │                                │
  │──── update_task_status ──────►│
  │     (42, "InProgress")         │
  │◄─── { status:"InProgress" } ──│
  │                                │
  │  ┌─────────────────────────┐   │
  │  │ LLM reasons & acts      │   │
  │  │ (reads files, runs CLI, │   │
  │  │  calls MCP tools, edits │   │
  │  │  code, commits, etc.)   │   │
  │  └─────────────────────────┘   │
  │                                │
  │──── add_comment(42, summary) ►│
  │◄─── { id:101, ... } ──────────│
  │                                │
  │──── update_task_status ──────►│
  │     (42, "InReview")           │
  │◄─── { status:"InReview" } ────│
  │                                │
  │  [sleep PollingIntervalSeconds]│
  │                                │
```

### Error During Task Processing

```
Agent                          TeamWare MCP
  │                                │
  │  ... (pick up as above) ...    │
  │                                │
  │  ┌─────────────────────────┐   │
  │  │ LLM processing fails    │   │
  │  │ (exception thrown)      │   │
  │  └─────────────────────────┘   │
  │                                │
  │──── add_comment(42, error) ──►│
  │◄─── { id:102, ... } ──────────│
  │                                │
  │──── update_task_status ──────►│
  │     (42, "Error")              │
  │◄─── { status:"Error" } ───────│
  │                                │
  │──── post_lounge_message ─────►│
  │     (projectId, notification)  │
  │◄─── { id:200, ... } ──────────│
  │                                │
```

### Agent Paused by Admin

```
Agent                          TeamWare MCP
  │                                │
  │──── get_my_profile ──────────►│
  │◄─── { isAgentActive: false } ─│
  │                                │
  │  [skip cycle, sleep, retry]    │
  │                                │
```

---

## Appendix A: Existing Agent Source Structure

```
TeamWare.Agent/
├── Program.cs                          # Host builder, DI registration, entry point
├── AgentHostedService.cs               # IHostedService managing polling loops
├── Configuration/
│   ├── AgentIdentityOptions.cs         # Per-identity config + server merge logic
│   ├── McpServerOptions.cs             # MCP server connection config
│   ├── RepositoryOptions.cs            # Per-project repository config
│   └── ResolvedRepository.cs           # Resolution result record
├── Mcp/
│   ├── ITeamWareMcpClient.cs           # MCP client interface (the contract)
│   ├── TeamWareMcpClient.cs            # Production MCP client implementation
│   ├── ITeamWareMcpClientFactory.cs    # Factory interface
│   ├── TeamWareMcpClientFactory.cs     # Factory implementation
│   ├── McpToolException.cs             # Exception for tool call failures
│   ├── AgentProfile.cs                 # Profile response model
│   ├── AgentProfileConfiguration.cs    # Server-side config model
│   ├── AgentProfileRepository.cs       # Server-side repo config model
│   ├── AgentProfileMcpServer.cs        # Server-side MCP server config model
│   ├── AgentTask.cs                    # Task summary model (from my_assignments)
│   ├── AgentTaskDetail.cs              # Full task model (from get_task)
│   ├── AgentTaskAssignee.cs            # Task assignee model
│   └── AgentTaskComment.cs             # Task comment model
├── Pipeline/
│   ├── AgentPollingLoop.cs             # Core polling loop logic
│   ├── TaskProcessor.cs                # Single-task LLM processing
│   ├── StatusTransitionHandler.cs      # Status change + comment + lounge notification
│   ├── DefaultSystemPrompt.cs          # Built-in system prompt text
│   ├── ICopilotClientWrapper.cs        # LLM client abstraction
│   ├── CopilotClientWrapper.cs         # GitHub Copilot SDK wrapper
│   ├── ICopilotClientWrapperFactory.cs # LLM client factory interface
│   ├── CopilotClientWrapperFactory.cs  # LLM client factory implementation
│   ├── ICopilotSessionWrapper.cs       # LLM session abstraction
│   └── CopilotSessionWrapper.cs        # GitHub Copilot SDK session wrapper
├── Permissions/
│   └── AgentPermissionHandler.cs       # Tool call safety guardrails
├── Logging/
│   ├── DryRunLogger.cs                 # Dry run mode — log writes, allow reads
│   └── DryRunToolCall.cs               # Model for logged dry-run tool calls
├── Repository/
│   └── RepositoryManager.cs            # Git clone/pull operations
├── appsettings.json                    # Local configuration
├── appsettings.example.json            # Documented example configuration
└── README.md                           # Operational documentation
```

## Appendix B: NuGet Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `GitHub.Copilot.SDK` | 0.2.1-preview.1 | LLM engine (Copilot integration) |
| `Microsoft.Extensions.Hosting` | 10.0.5 | .NET Generic Host (IHostedService, DI, logging) |
| `Microsoft.Extensions.Configuration.Json` | 10.0.5 | JSON configuration provider |
| `Microsoft.Extensions.Configuration.EnvironmentVariables` | 10.0.5 | Environment variable configuration |
| `Microsoft.Extensions.Logging.Console` | 10.0.5 | Console logging |
| `ModelContextProtocol` | 1.1.0 | MCP client for connecting to TeamWare's MCP endpoint |

**Note for alternative implementations:** Only the `ModelContextProtocol` package (or equivalent MCP client library for your language) and an HTTP client are truly required. The Copilot SDK is specific to the LLM engine choice. The hosting/configuration/logging packages are standard .NET infrastructure that would be replaced by equivalents in other languages.

## Appendix C: Environment Variables

All configuration can be set via environment variables prefixed with `TEAMWARE_AGENT_`. The naming follows .NET's standard configuration binding:

```bash
TEAMWARE_AGENT_Agents__0__Name=my-agent
TEAMWARE_AGENT_Agents__0__PersonalAccessToken=your-pat
TEAMWARE_AGENT_Agents__0__WorkingDirectory=/tmp/agent-work
TEAMWARE_AGENT_Agents__0__McpServers__0__Name=teamware
TEAMWARE_AGENT_Agents__0__McpServers__0__Type=http
TEAMWARE_AGENT_Agents__0__McpServers__0__Url=https://teamware.example.com/mcp
```

The double-underscore (`__`) is the standard .NET separator for nested configuration keys. `0` is the array index.
