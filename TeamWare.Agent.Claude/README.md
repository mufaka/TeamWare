# TeamWare Claude Agent

A standalone Node.js/TypeScript coding agent that connects to TeamWare via MCP and uses the Anthropic Claude Agent SDK to process tasks autonomously.

## Overview

This agent follows the same protocol as the .NET Copilot agent (`TeamWare.Agent`) and the Codex agent (`TeamWare.Agent.Codex`). All interaction is via TeamWare's MCP endpoint using Personal Access Token (PAT) authentication. The agent:

1. Polls TeamWare for assigned `ToDo` tasks
2. Picks up a task (comments and moves to `InProgress`)
3. Runs the Claude Agent SDK with the task context
4. Lets the LLM reason and act using Claude Code's built-in tools (file read/write, bash, web search, etc.)
5. Reports completion (`InReview`), blocks unclear tasks (`Blocked`), or flags errors (`Error`)

## Prerequisites

- **Node.js 18+**
- **Claude Code CLI** — `npm install -g @anthropic-ai/claude-code`
- **Git** — required for repository management
- **TeamWare instance** with MCP endpoint enabled
- **Agent user account** created in TeamWare's admin panel
- **Personal Access Token (PAT)** generated for the agent user

## Authentication

### Local Auth (Recommended)

Run `claude login` on the machine where the agent will run. Auth is handled automatically by the Claude Code CLI. This is included with Claude Max and Claude Pro subscriptions at **no additional API cost**.

### API Key (Alternative)

> **Cost warning:** Using an API key routes all Claude requests through the Anthropic API, which incurs per-token usage costs **separate from and in addition to any Claude subscription**. A Claude Max/Pro subscription does NOT offset API charges.

Provide an API key in the TeamWare admin UI or in local config.

## Quick Start

### 1. Create an Agent User in TeamWare

1. Log in as admin, navigate to **Admin > Agent Users > Create Agent**
2. Set the agent backend to **Claude** on the edit page
3. Generate a PAT for the agent

### 2. Authenticate Claude

```bash
claude login   # Follow the prompts to sign in
```

### 3. Configure

Copy `config/config.example.json` to `config/config.json`:

```json
{
  "teamware": {
    "mcpUrl": "https://your-teamware-instance/mcp",
    "personalAccessToken": "your-pat-token-here"
  },
  "workingDirectory": "/path/to/working/directory"
}
```

That's the minimum. All other settings (model, polling interval, repositories, etc.) are managed from the TeamWare admin UI and delivered via `get_my_profile`.

### 4. Run

```bash
npm install
npm run build
npm start
```

Or for development:

```bash
npm run dev
```

### 5. Verify with Dry Run

Set `dryRun: true` in the TeamWare admin UI (or local config) to run the full pipeline without making changes. Claude will plan what it would do without executing any tools.

## Configuration Reference

Local config takes precedence over server-side config (same merge semantics as the Copilot and Codex agents).

### Required (local only)

| Field | Description |
|-------|-------------|
| `teamware.mcpUrl` | TeamWare MCP endpoint URL |
| `teamware.personalAccessToken` | PAT for this agent user |
| `workingDirectory` | Local directory for cloning repos and running tasks |

### Optional (local overrides, or managed from TeamWare UI)

| Field | Default | Description |
|-------|---------|-------------|
| `pollingIntervalSeconds` | 60 | Seconds between polling cycles |
| `model` | SDK default | Claude model (e.g. `claude-sonnet-4-5-20250929`) |
| `autoApproveTools` | true | Auto-approve tool calls (uses `bypassPermissions` mode) |
| `dryRun` | false | Plan mode — Claude describes what it would do without executing |
| `taskTimeoutSeconds` | 600 | Seconds before task timeout |
| `systemPrompt` | Built-in | Custom system prompt |
| `anthropicApiKey` | — | Anthropic API key (incurs per-token costs) |
| `repositoryUrl` | — | Default git repository URL |
| `repositoryBranch` | `main` | Default branch |
| `repositoryAccessToken` | — | Git access token |
| `repositories` | `[]` | Per-project repository mappings |
| `mcpServers` | `[]` | Additional MCP server connections |

## Architecture

```
+------------------------------------------+
|          TeamWare.Agent.Claude            |
|                                          |
|  index.ts                                |
|    +- PollingLoop                        |
|         +- TeamWareMcpClient             |
|         |   (get_my_profile,             |
|         |    my_assignments,             |
|         |    get_task, etc.)             |
|         +- StatusTransitionHandler       |
|         |   (comments + status changes)  |
|         +- RepositoryManager             |
|         |   (git clone/pull)             |
|         +- TaskProcessor                 |
|             (Claude Agent SDK query())   |
+---------------+--+-------+---------------+
                |           |
                | MCP       | Claude Agent SDK
                | (HTTP+PAT)|  (subprocess)
                v           v
        +--------------+  +-----------+
        | TeamWare.Web |  | Claude    |
        | /mcp         |  | Code CLI  |
        +--------------+  +-----------+
```

## Notes

- **Git token in remote URL**: `buildAuthenticatedUrl` embeds the access token as the URL username (`https://<token>@host/repo`). This means the token is visible in `git remote -v` output. Same behavior as the Codex agent.
- **MCP servers for the LLM**: The processor passes TeamWare's MCP server to the Claude Agent SDK so Claude can call TeamWare tools directly during task execution.
- **Session persistence**: Sessions are not persisted (`persistSession: false`) since each task is a fresh execution.

## Deployment

### Terminal

```bash
npm start
# or: node dist/index.js
# or with custom config: node dist/index.js /path/to/config.json
```

### systemd

```ini
[Unit]
Description=TeamWare Claude Agent
After=network.target

[Service]
Type=simple
WorkingDirectory=/opt/teamware-claude-agent
ExecStart=/usr/bin/node dist/index.js
Restart=on-failure
RestartSec=10
Environment=NODE_ENV=production

[Install]
WantedBy=multi-user.target
```

### Docker

```dockerfile
FROM node:22-slim
RUN npm install -g @anthropic-ai/claude-code
WORKDIR /app
COPY package*.json ./
RUN npm ci --omit=dev
COPY dist/ dist/
COPY config/ config/
CMD ["node", "dist/index.js"]
```
