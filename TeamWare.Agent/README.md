# TeamWare Agent

The TeamWare Agent is a standalone .NET 10 console application that acts as an autonomous coding agent. It polls a TeamWare instance for assigned tasks, uses the GitHub Copilot SDK to reason about and complete them, and reports its progress back through comments and status transitions.

## Overview

The agent operates as a separate process from TeamWare.Web. All interaction is via TeamWare's MCP (Model Context Protocol) endpoint using Personal Access Token (PAT) authentication. Each agent identity:

1. Polls TeamWare for assigned `ToDo` tasks
2. Picks up a task (comments and moves to `InProgress`)
3. Creates a GitHub Copilot SDK session with the task context
4. Lets the LLM reason and act using available tools (CLI, MCP, file I/O)
5. Reports completion (`InReview`), blocks unclear tasks (`Blocked`), or flags errors (`Error`)

## Prerequisites

- **.NET 10 SDK** — Required to build and run the agent
- **GitHub Copilot subscription** or BYOK (Bring Your Own Key) configuration for the Copilot SDK
- **Git** — Required if using repository management features (clone/pull before task processing)
- **TeamWare instance** — A running TeamWare.Web instance with MCP endpoint enabled
- **Agent user account** — An agent-type user created in TeamWare's admin panel
- **Personal Access Token (PAT)** — Generated for the agent user in TeamWare

## Quick Start

### 1. Create an Agent User in TeamWare

1. Log in to TeamWare as an administrator
2. Navigate to **Admin > Agent Users**
3. Click **Create Agent**
4. Fill in the agent's display name and description
5. The agent will be created with `IsAgent = true` and `IsActive = true`

### 2. Generate a Personal Access Token

1. Navigate to the agent user's profile or **Admin > PAT Management**
2. Create a new PAT for the agent user
3. Copy the token — it will only be shown once

### 3. Configure `appsettings.json`

Copy `appsettings.example.json` to `appsettings.json` and fill in your values:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting": "Information",
      "TeamWare.Agent": "Debug"
    }
  },
  "Agents": [
    {
      "Name": "my-agent",
      "WorkingDirectory": "/path/to/working/directory",
      "PersonalAccessToken": "your-pat-token-here",
      "PollingIntervalSeconds": 60,
      "DryRun": true,
      "McpServers": [
        {
          "Name": "teamware",
          "Type": "http",
          "Url": "https://your-teamware-instance/mcp"
        }
      ]
    }
  ]
}
```

### 4. Run the Agent

```bash
cd TeamWare.Agent
dotnet run
```

The agent will start polling for tasks. In dry run mode, it will log what it would do without actually making changes.

### 5. Disable Dry Run

Once you've verified the agent is discovering tasks correctly, set `"DryRun": false` in your configuration to enable full processing.

## Configuration Reference

Configuration is loaded from `appsettings.json` and environment variables (prefixed with `TEAMWARE_AGENT_`).

### Agent Identity Options

Each entry in the `Agents` array configures one agent identity:

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `Name` | string | Yes | — | Display name for this agent identity (used in logs) |
| `WorkingDirectory` | string | Yes | — | Local directory where the agent works (clones repos, runs commands). Never overwritten by server config. |
| `RepositoryUrl` | string | No | `null` | Git repository URL to clone/pull before processing tasks. Server value applied if local is null. |
| `RepositoryBranch` | string | No | `"main"` | Branch to clone/pull from. Server value applied if local is null. |
| `RepositoryAccessToken` | string | No | `null` | Token for git authentication (inserted into HTTPS URLs). Server value applied if local is null. |
| `PersonalAccessToken` | string | Yes | — | TeamWare PAT for MCP authentication. Never overwritten by server config. |
| `PollingIntervalSeconds` | int | No | `60` | Seconds between polling cycles. Server value applied if local is default (60). |
| `Model` | string | No | `null` | Copilot model to use (e.g., `"gpt-4o"`) — uses SDK default if null. Server value applied if local is null. |
| `AutoApproveTools` | bool | No | `true` | Auto-approve all tool calls; set `false` for custom permission handling. Server value applied if local is default (true). |
| `DryRun` | bool | No | `false` | Log write operations instead of executing them. Server value applied if local is default (false). |
| `TaskTimeoutSeconds` | int | No | `600` | Seconds before a task processing session times out. Server value applied if local is default (600). |
| `SystemPrompt` | string | No | `null` | Custom system prompt; uses the built-in default if null. Server value applied if local is null. |
| `Repositories` | array | No | `[]` | Per-project repository mappings (see below). Merged with server entries by ProjectName. |
| `McpServers` | array | Yes | — | MCP server connections (at least one HTTP server required). Merged with server entries by Name. |

### MCP Server Options

Each entry in the `McpServers` array configures an MCP server connection:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `Name` | string | Yes | Display name for this MCP server |
| `Type` | string | Yes | Server type: `"http"` or `"stdio"` (also accepts `"local"`) |
| `Url` | string | Yes (http) | MCP endpoint URL (required for HTTP servers) |
| `AuthHeader` | string | No | Custom `Authorization` header value; falls back to `Bearer {PAT}` if not set |
| `Command` | string | Yes (stdio) | Path to the executable for stdio/local MCP servers |
| `Args` | string[] | No | Command-line arguments for the stdio server executable |
| `Env` | object | No | Environment variables to set for the stdio server process |

### Repository Options (Multi-Repo Support)

Each entry in the `Repositories` array maps a TeamWare project to a separate Git repository. When the agent picks up a task, it matches the task's `ProjectName` (case-insensitive) against this list to determine which repository to clone/pull and which working directory to use for the Copilot session.

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `ProjectName` | string | Yes | — | TeamWare project name to match against (case-insensitive) |
| `Url` | string | Yes | — | Git repository URL (HTTPS or SSH) |
| `Branch` | string | No | `"main"` | Branch to clone/pull from |
| `AccessToken` | string | No | `null` | Token for private repository authentication |

**Resolution logic:**

1. If a matching project entry exists, a subdirectory named after the project is created under `WorkingDirectory` (e.g., `/tmp/work/Frontend/`)
2. If no match is found or `Repositories` is empty, falls back to the flat `RepositoryUrl`/`WorkingDirectory` fields
3. This is fully backward compatible — existing single-repo configurations work unchanged

**Example:**

```json
{
  "Name": "multi-repo-agent",
  "WorkingDirectory": "/tmp/work",
  "PersonalAccessToken": "your-pat",
  "Repositories": [
    {
      "ProjectName": "Frontend",
      "Url": "https://github.com/org/frontend.git",
      "Branch": "dev",
      "AccessToken": "ghp_frontend_token"
    },
    {
      "ProjectName": "Backend",
      "Url": "https://github.com/org/backend.git"
    }
  ]
}
```

In this example:
- Tasks from the "Frontend" project clone into `/tmp/work/Frontend/` on the `dev` branch
- Tasks from the "Backend" project clone into `/tmp/work/Backend/` on the `main` branch
- Tasks from any other project fall back to `RepositoryUrl`/`WorkingDirectory`

### Server-Side Configuration

TeamWare supports centralized agent configuration through the admin panel. Administrators can set behavioral options, repository mappings, and MCP server connections directly in the TeamWare web UI. These settings are delivered to the agent via the `get_my_profile` MCP tool response on each polling cycle.

**How it works:**

1. An administrator configures the agent in **Admin > Agent Users > Edit Agent**
2. On each polling cycle, the agent calls `get_my_profile` which returns a `configuration` block
3. The agent merges server-side values into its local options using the rules below

**Merge precedence — local always wins:**

| Field | Applied when local value is… |
|-------|------------------------------|
| `PollingIntervalSeconds` | Default (`60`) |
| `Model` | `null` |
| `AutoApproveTools` | Default (`true`) |
| `DryRun` | Default (`false`) |
| `TaskTimeoutSeconds` | Default (`600`) |
| `SystemPrompt` | `null` |
| `RepositoryUrl` | `null` |
| `RepositoryBranch` | `null` |
| `RepositoryAccessToken` | `null` |
| `Repositories` | Merged by `ProjectName` (case-insensitive) — local entries win on collision, server-only entries are appended |
| `McpServers` | Merged by `Name` (case-insensitive) — local entries win on collision, server-only entries are appended |
| `WorkingDirectory` | **Never overwritten** — always local |
| `PersonalAccessToken` | **Never overwritten** — always local |

**Minimal bootstrap configuration:**

With server-side configuration, an agent only needs the bare minimum in `appsettings.json`:

```json
{
  "Agents": [
    {
      "Name": "my-agent",
      "WorkingDirectory": "/path/to/working/directory",
      "PersonalAccessToken": "your-pat-token-here",
      "McpServers": [
        {
          "Name": "teamware",
          "Type": "http",
          "Url": "https://your-teamware-instance/mcp"
        }
      ]
    }
  ]
}
```

All other settings (model, polling interval, repositories, additional MCP servers) can be managed centrally via the admin panel. Changes take effect on the next polling cycle without restarting the agent.

**Backward compatibility:** Agents with full local configuration continue to work unchanged. Server-side configuration is entirely optional — if no server-side config exists, the agent uses its local settings as before.

## Dry Run Mode

Dry run mode is the recommended starting point for new agent deployments. When `DryRun` is `true`:

- The full pipeline executes: polling, task discovery, profile checks, session creation, LLM reasoning
- **Read operations** (MCP reads, file reads) execute normally
- **Write operations** (MCP writes, shell commands, file writes) are **blocked and logged**
- Tool call details (name, parameters, LLM reasoning) are recorded

This lets you verify the agent is:
- Connecting to TeamWare correctly
- Discovering the right tasks
- Generating reasonable plans

without making any changes to your project or TeamWare data.

## Architecture

```
┌────────────────────────────────────────────────────┐
│                 AgentHostedService                  │
│  (IHostedService — one per process)                │
├──────────────────┬─────────────────────────────────┤
│  AgentPollingLoop │  AgentPollingLoop              │
│  (Identity #1)   │  (Identity #2)                 │
│                  │                                 │
│  ┌─────────────┐ │  ┌─────────────┐               │
│  │StatusHandler │ │  │StatusHandler │               │
│  │RepoManager  │ │  │RepoManager  │               │
│  │TaskProcessor│ │  │TaskProcessor│               │
│  └─────────────┘ │  └─────────────┘               │
└──────────────────┴─────────────────────────────────┘
         │                       │
         ▼                       ▼
   ┌───────────┐          ┌───────────┐
   │ TeamWare  │          │ TeamWare  │
   │ MCP       │          │ MCP       │
   │ Endpoint  │          │ Endpoint  │
   └───────────┘          └───────────┘
```

### Pipeline Flow

For each polling cycle:

1. **Profile Check** — `get_my_profile` verifies the agent is active
2. **Configuration Merge** — Server-side configuration (if any) is merged into local options
3. **Task Discovery** — `my_assignments` returns assigned tasks, filtered to `ToDo`
3. **Read-Before-Write** — `get_task` verifies the task is still in `ToDo` (idempotency)
4. **Pickup** — Comment posted + status changed to `InProgress`
5. **Repository Resolution** — Task's `ProjectName` matched against `Repositories` to determine repo and working directory
6. **Repository Sync** — Git clone/pull for the resolved repository (or `RepositoryUrl` fallback)
7. **Processing** — Copilot SDK session with task context, CWD set to resolved working directory
8. **Completion** — Comment posted + status changed to `InReview`
8. **Error Handling** — On failure: error comment + `Error` status + lounge notification

## Deployment Options

### Terminal (Development)

```bash
dotnet run --project TeamWare.Agent
```

### Published Executable

```bash
dotnet publish TeamWare.Agent -c Release -o ./publish
./publish/TeamWare.Agent
```

### systemd (Linux)

Create a service file at `/etc/systemd/system/teamware-agent.service`:

```ini
[Unit]
Description=TeamWare Copilot Agent
After=network.target

[Service]
Type=notify
WorkingDirectory=/opt/teamware-agent
ExecStart=/opt/teamware-agent/TeamWare.Agent
Restart=on-failure
RestartSec=10
Environment=DOTNET_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
```

Then:

```bash
sudo systemctl daemon-reload
sudo systemctl enable teamware-agent
sudo systemctl start teamware-agent
```

### Docker

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish TeamWare.Agent -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "TeamWare.Agent.dll"]
```

## Troubleshooting

### Authentication Failures

- **"401 Unauthorized"** — Verify the PAT is correct and hasn't expired. Check that the agent user is active in TeamWare's admin panel.
- **"get_my_profile returned isAgentActive=false"** — The agent user has been paused by an admin. Re-enable it in **Admin > Agent Users**.

### Network Errors

- **"Connection refused"** — Verify the TeamWare MCP endpoint URL is correct and the server is running.
- **Repeated network errors** — The agent will log errors and continue polling. It does not crash on transient failures.

### Copilot CLI Not Found

- Ensure the GitHub Copilot SDK is properly installed and the `copilot` CLI is available in the system PATH.
- Verify your GitHub Copilot subscription is active.

### Repository Sync Failures

- **"git command failed"** — Check that `git` is installed and available in PATH.
- **Clone failures** — Verify the `RepositoryUrl` and `RepositoryAccessToken` are correct.
- **Pull failures** — Ensure the `RepositoryBranch` exists in the remote repository.

### No Tasks Found

- Verify tasks are assigned to the agent user in TeamWare.
- Check that assigned tasks have `ToDo` status — the agent only picks up `ToDo` tasks.
- Verify the agent user is a member of the project containing the tasks.

### Agent Processes Same Task Repeatedly

- This shouldn't happen due to read-before-write idempotency checks. If a task is no longer in `ToDo` status, the agent skips it.
- Check if another process is resetting task statuses.
