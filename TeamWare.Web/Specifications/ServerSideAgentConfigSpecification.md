# TeamWare - Server-Side Agent Configuration Specification

## 1. Introduction

### 1.1 Purpose

This document provides the formal specification for moving agent runtime configuration from the local `appsettings.json` file into the TeamWare web application. Instead of each agent host maintaining a full configuration file, administrators will manage repositories, MCP servers, and behavioral settings through the existing agent admin UI. The agent process retrieves its configuration from the TeamWare server at startup and on each polling cycle via the existing `get_my_profile` MCP tool.

This specification is a companion to the [Agent Users specification](AgentUsersSpecification.md), the [Copilot Agent specification](CopilotAgentSpecification.md), and the [MCP Server specification](McpServerSpecification.md).

### 1.2 Scope

The feature is divided into three areas:

- **Area A** — Data model changes on `TeamWare.Web`: new entities (`AgentConfiguration`, `AgentRepository`, `AgentMcpServer`) with EF Core migration.
- **Area B** — Admin UI changes: extend the existing Edit Agent page with configuration, repository, and MCP server management sections.
- **Area C** — MCP and agent-side changes: enrich the `get_my_profile` response with a `configuration` block; update the agent to merge server-side config with local overrides.

Out of scope:
- Changes to the Copilot SDK integration or task processing pipeline (these already read from `AgentIdentityOptions` and require no changes).
- Real-time config push (agents poll for config changes on each cycle).
- Migration tooling to import existing `appsettings.json` configurations into the database.

### 1.3 Definitions and Acronyms

| Term | Definition |
|------|-----------|
| Server-side config | Configuration stored in the TeamWare database and managed through the admin UI. |
| Local config | Configuration stored in the agent's `appsettings.json` file on the host machine. |
| Bootstrap config | The minimal local config required to connect to TeamWare: `Name`, `PersonalAccessToken`, `WorkingDirectory`, and the TeamWare MCP server entry. |
| Config merge | The process of combining server-side and local config, with local values taking precedence for host-specific settings. |

### 1.4 Design Principles

- **Backward compatible** — Agents with full local configuration continue to work unchanged. Server-side config is additive; the agent merges rather than replaces.
- **Local overrides server** — Host-specific settings (`WorkingDirectory`) and any explicitly set local values take precedence over server-side values.
- **Minimal bootstrap** — A new agent deployment needs only `Name`, `PersonalAccessToken`, `WorkingDirectory`, and one MCP server entry pointing to TeamWare.
- **Secure by default** — Repository access tokens and MCP server credentials are stored encrypted at rest (using the existing ASP.NET Core Data Protection stack). They are transmitted to agents only over the authenticated MCP connection.
- **Consistent with existing patterns** — Entities follow existing conventions. Services use `ServiceResult<T>`. UI uses Tailwind CSS 4, HTMX, and Alpine.js.

---

## 2. Technology Additions

No new technology dependencies are introduced. All changes use existing frameworks and libraries already in the TeamWare stack. The ASP.NET Core Data Protection API (already available) is used for encrypting sensitive fields.

---

## 3. Functional Requirements

### 3.1 Agent Configuration Entity

| ID | Requirement |
|----|------------|
| SACFG-01 | Each agent user (`ApplicationUser` with `IsAgent = true`) shall have at most one `AgentConfiguration` record (one-to-one relationship via `UserId` foreign key). |
| SACFG-02 | The `AgentConfiguration` entity shall include the following behavioral properties: `PollingIntervalSeconds` (int?, nullable), `Model` (string?, nullable), `AutoApproveTools` (bool?, nullable), `DryRun` (bool?, nullable), `TaskTimeoutSeconds` (int?, nullable), `SystemPrompt` (string?, nullable, max 10000 characters). |
| SACFG-03 | All behavioral properties shall be nullable to distinguish "not configured server-side" from "explicitly set." When null, the agent uses its local config or built-in default. |
| SACFG-04 | The `AgentConfiguration` entity shall not store `WorkingDirectory`, `PersonalAccessToken`, or the bootstrap TeamWare MCP server connection. These are inherently host-specific and remain in local config only. |

### 3.2 Agent Repository Entity

| ID | Requirement |
|----|------------|
| SACFG-10 | Each `AgentConfiguration` shall have zero or more `AgentRepository` records (one-to-many). |
| SACFG-11 | The `AgentRepository` entity shall include: `ProjectName` (string, required, max 200), `Url` (string, required, max 500), `Branch` (string, default "main", max 100), `EncryptedAccessToken` (string?, nullable), `DisplayOrder` (int, default 0). |
| SACFG-12 | Repository access tokens shall be encrypted using ASP.NET Core Data Protection before storage. The `EncryptedAccessToken` column stores the protected value. The raw token is never persisted in plaintext. |
| SACFG-13 | `ProjectName` shall be unique within an agent's repository list (enforced by a unique index on `AgentConfigurationId` + `ProjectName`). |

### 3.3 Agent MCP Server Entity

| ID | Requirement |
|----|------------|
| SACFG-20 | Each `AgentConfiguration` shall have zero or more `AgentMcpServer` records (one-to-many). |
| SACFG-21 | The `AgentMcpServer` entity shall include: `Name` (string, required, max 200), `Type` (string, required, "http" or "stdio"), `Url` (string?, nullable, max 500), `EncryptedAuthHeader` (string?, nullable), `Command` (string?, nullable, max 500), `Args` (string?, nullable — stored as JSON array), `EncryptedEnv` (string?, nullable — stored as encrypted JSON object), `DisplayOrder` (int, default 0). |
| SACFG-22 | Sensitive fields (`EncryptedAuthHeader`, `EncryptedEnv`) shall be encrypted using ASP.NET Core Data Protection before storage. |
| SACFG-23 | MCP servers configured server-side shall be **additive** to any MCP servers in local config. The agent merges both lists, with local entries taking precedence when names collide. |
| SACFG-24 | The bootstrap TeamWare MCP server (type "http" pointing to the TeamWare instance) shall NOT be stored server-side. It is inherently part of the local bootstrap config (chicken-and-egg: the agent needs it to reach the server in the first place). |

### 3.4 Flat Repository Fields (Legacy Support)

| ID | Requirement |
|----|------------|
| SACFG-30 | The `AgentConfiguration` entity shall also include flat repository fields for single-repo backward compatibility: `RepositoryUrl` (string?, nullable, max 500), `RepositoryBranch` (string?, nullable, max 100), `EncryptedRepositoryAccessToken` (string?, nullable). |
| SACFG-31 | If an agent has both flat repository fields and `AgentRepository` entries, the `AgentRepository` entries take precedence (same resolution logic as the agent's existing `ResolveRepository` method). |

### 3.5 Admin UI — Configuration Management

| ID | Requirement |
|----|------------|
| SACFG-40 | The existing Edit Agent page shall be extended with a "Configuration" section containing form fields for all behavioral properties (SACFG-02). |
| SACFG-41 | Each field shall have a "Not set (use agent default)" option to represent the null/unset state, visually distinct from an explicitly set value. |
| SACFG-42 | The Edit Agent page shall include a "Repositories" section with an inline table/list for managing `AgentRepository` entries (add, edit, remove). |
| SACFG-43 | The Edit Agent page shall include a "MCP Servers" section with an inline table/list for managing `AgentMcpServer` entries (add, edit, remove). |
| SACFG-44 | Repository access tokens and MCP server auth headers shall be displayed as masked values (e.g., `ghp_****xyz`) in the UI. Full values are never shown after initial entry. |
| SACFG-45 | The Agent Detail page shall display a read-only summary of the current configuration, repositories, and MCP servers. |
| SACFG-46 | The Create Agent flow shall not include configuration fields. New agents start with no server-side configuration (all null/empty), using agent defaults until configured. |

### 3.6 MCP Profile Response — Configuration Block

| ID | Requirement |
|----|------------|
| SACFG-50 | The `get_my_profile` MCP tool response shall include an optional `configuration` object when the authenticated user is an agent with an `AgentConfiguration` record. |
| SACFG-51 | The `configuration` object shall include all behavioral properties from SACFG-02, with null values omitted from the JSON output. |
| SACFG-52 | The `configuration` object shall include a `repositories` array containing all `AgentRepository` entries with `projectName`, `url`, `branch`, and `accessToken` (decrypted). |
| SACFG-53 | The `configuration` object shall include a `mcpServers` array containing all `AgentMcpServer` entries with all fields (sensitive fields decrypted). |
| SACFG-54 | The `configuration` object shall include the flat repository fields (`repositoryUrl`, `repositoryBranch`, `repositoryAccessToken`) when set. |
| SACFG-55 | The `configuration` block shall only be included for agent users (`IsAgent = true`). Human user profiles remain unchanged. |
| SACFG-56 | If no `AgentConfiguration` record exists for an agent, the `configuration` field shall be `null` (or omitted), signaling the agent to use its local config entirely. |

### 3.7 Agent-Side Configuration Merge

| ID | Requirement |
|----|------------|
| SACFG-60 | After a successful `get_my_profile` call, the agent shall merge the server-side `configuration` block into its `AgentIdentityOptions` instance. |
| SACFG-61 | Merge rules — server-side values are applied only when the local config value is at its default (i.e., was not explicitly set in `appsettings.json`). Specifically: |
| | — `PollingIntervalSeconds`: Server value applied if local value is the default (60). |
| | — `Model`: Server value applied if local value is `null`. |
| | — `AutoApproveTools`: Server value applied if local value is `true` (the default). |
| | — `DryRun`: Server value applied if local value is `false` (the default). |
| | — `TaskTimeoutSeconds`: Server value applied if local value is the default (600). |
| | — `SystemPrompt`: Server value applied if local value is `null`. |
| SACFG-62 | For `Repositories`: Server-side repositories are merged with local repositories. If a `ProjectName` exists in both, the local entry wins. Server-only entries are appended. |
| SACFG-63 | For `McpServers`: Server-side MCP servers are merged with local MCP servers. If a `Name` exists in both, the local entry wins. Server-only entries are appended. |
| SACFG-64 | `WorkingDirectory` is never sourced from the server. It is always local. |
| SACFG-65 | `PersonalAccessToken` is never sourced from the server. It is always local. |
| SACFG-66 | The merged config shall be logged at `Debug` level (with secrets redacted) so operators can verify what the agent is using. |
| SACFG-67 | If the `configuration` block is null or absent, the agent shall use its local config unchanged (full backward compatibility). |

---

## 4. Non-Functional Requirements

| ID | Requirement |
|----|------------|
| SACFG-NF-01 | Encrypted fields shall use the ASP.NET Core Data Protection API with the application's default key ring. |
| SACFG-NF-02 | The `get_my_profile` response size shall remain under 64 KB for typical configurations (up to 20 repositories and 10 MCP servers). |
| SACFG-NF-03 | The configuration merge shall complete in under 1 ms (in-memory operation only). |
| SACFG-NF-04 | The EF Core migration shall be non-destructive: existing agent users with no configuration continue to function identically. |
| SACFG-NF-05 | All admin UI operations shall validate inputs (max lengths, required fields, valid URLs) before persisting. |
| SACFG-NF-06 | Repository access tokens and MCP server credentials shall never appear in application logs, even at `Debug` or `Trace` level. |

---

## 5. Data Model

### 5.1 Entity Relationship

```
ApplicationUser (1) ──→ (0..1) AgentConfiguration
                                    │
                                    ├──→ (0..N) AgentRepository
                                    │
                                    └──→ (0..N) AgentMcpServer
```

### 5.2 AgentConfiguration Entity

| Property | Type | Constraints | Description |
|----------|------|-------------|-------------|
| `Id` | int | PK, auto-increment | Primary key |
| `UserId` | string | FK → ApplicationUser, unique, required | The agent user this config belongs to |
| `PollingIntervalSeconds` | int? | nullable | Polling interval override |
| `Model` | string? | max 200, nullable | Copilot model override |
| `AutoApproveTools` | bool? | nullable | Auto-approve override |
| `DryRun` | bool? | nullable | Dry run override |
| `TaskTimeoutSeconds` | int? | nullable | Task timeout override |
| `SystemPrompt` | string? | max 10000, nullable | System prompt override |
| `RepositoryUrl` | string? | max 500, nullable | Flat repo URL (legacy) |
| `RepositoryBranch` | string? | max 100, nullable | Flat repo branch (legacy) |
| `EncryptedRepositoryAccessToken` | string? | nullable | Encrypted flat repo token |
| `CreatedAt` | DateTime | required, UTC | When the config was created |
| `UpdatedAt` | DateTime | required, UTC | When the config was last modified |

Navigation properties:
- `ApplicationUser User` (required)
- `ICollection<AgentRepository> Repositories`
- `ICollection<AgentMcpServer> McpServers`

### 5.3 AgentRepository Entity

| Property | Type | Constraints | Description |
|----------|------|-------------|-------------|
| `Id` | int | PK, auto-increment | Primary key |
| `AgentConfigurationId` | int | FK → AgentConfiguration, required | Parent config |
| `ProjectName` | string | max 200, required | TeamWare project name to match |
| `Url` | string | max 500, required | Git repository URL |
| `Branch` | string | max 100, default "main" | Branch to clone/pull |
| `EncryptedAccessToken` | string? | nullable | Encrypted Git access token |
| `DisplayOrder` | int | default 0 | Ordering in UI |

Unique index: `(AgentConfigurationId, ProjectName)`

### 5.4 AgentMcpServer Entity

| Property | Type | Constraints | Description |
|----------|------|-------------|-------------|
| `Id` | int | PK, auto-increment | Primary key |
| `AgentConfigurationId` | int | FK → AgentConfiguration, required | Parent config |
| `Name` | string | max 200, required | Display name |
| `Type` | string | max 20, required | "http" or "stdio" |
| `Url` | string? | max 500, nullable | MCP endpoint URL (http type) |
| `EncryptedAuthHeader` | string? | nullable | Encrypted auth header (http type) |
| `Command` | string? | max 500, nullable | Executable path (stdio type) |
| `Args` | string? | nullable | JSON array of arguments (stdio type) |
| `EncryptedEnv` | string? | nullable | Encrypted JSON object of env vars (stdio type) |
| `DisplayOrder` | int | default 0 | Ordering in UI |

### 5.5 Migration Notes

- The migration creates three new tables: `AgentConfigurations`, `AgentRepositories`, `AgentMcpServers`.
- No existing tables are modified.
- No data migration is needed — existing agents simply have no `AgentConfiguration` record and use local config.

---

## 6. Service Layer

### 6.1 IAgentConfigurationService

A new service interface for managing agent configuration:

```
GetConfigurationAsync(string userId) → ServiceResult<AgentConfigurationDto?>
SaveConfigurationAsync(string userId, AgentConfigurationDto dto) → ServiceResult
AddRepositoryAsync(string userId, AgentRepositoryDto dto) → ServiceResult<int>
UpdateRepositoryAsync(int repositoryId, AgentRepositoryDto dto) → ServiceResult
RemoveRepositoryAsync(int repositoryId) → ServiceResult
AddMcpServerAsync(string userId, AgentMcpServerDto dto) → ServiceResult<int>
UpdateMcpServerAsync(int mcpServerId, AgentMcpServerDto dto) → ServiceResult
RemoveMcpServerAsync(int mcpServerId) → ServiceResult
GetDecryptedConfigurationAsync(string userId) → ServiceResult<AgentConfigurationDto?>
```

The `GetDecryptedConfigurationAsync` method is used by the MCP tool to build the profile response with decrypted secrets. All other methods work with encrypted storage.

### 6.2 Encryption Helper

A helper service (`IAgentSecretEncryptor`) wrapping ASP.NET Core Data Protection:

```
Encrypt(string? plaintext) → string?
Decrypt(string? ciphertext) → string?
MaskForDisplay(string? plaintext) → string?
```

`MaskForDisplay` returns a masked value like `ghp_****xyz` for UI display.

---

## 7. MCP Tool Changes

### 7.1 Updated `get_my_profile` Response

Current response (unchanged for human users):
```json
{
  "userId": "...",
  "displayName": "my-agent",
  "isAgent": true,
  "isAgentActive": true,
  "lastActiveAt": "..."
}
```

New response for agent users with configuration:
```json
{
  "userId": "...",
  "displayName": "my-agent",
  "isAgent": true,
  "isAgentActive": true,
  "lastActiveAt": "...",
  "configuration": {
    "pollingIntervalSeconds": 30,
    "model": "gpt-4o",
    "autoApproveTools": true,
    "dryRun": false,
    "taskTimeoutSeconds": 900,
    "systemPrompt": null,
    "repositoryUrl": null,
    "repositoryBranch": null,
    "repositoryAccessToken": null,
    "repositories": [
      {
        "projectName": "Frontend",
        "url": "https://github.com/org/frontend.git",
        "branch": "dev",
        "accessToken": "ghp_decrypted_token"
      }
    ],
    "mcpServers": [
      {
        "name": "gitea",
        "type": "stdio",
        "command": "/usr/local/bin/gitea-mcp",
        "args": ["-t", "stdio"],
        "env": { "GITEA_HOST": "http://gitea:3000" }
      }
    ]
  }
}
```

When no `AgentConfiguration` exists, `configuration` is `null`:
```json
{
  "userId": "...",
  "displayName": "my-agent",
  "isAgent": true,
  "isAgentActive": true,
  "configuration": null
}
```

---

## 8. Agent-Side Changes

### 8.1 AgentProfile Model Update

The `AgentProfile` class in `TeamWare.Agent` gains an optional `Configuration` property:

```
AgentProfile.Configuration → AgentProfileConfiguration?
```

`AgentProfileConfiguration` contains:
- Nullable behavioral fields: `PollingIntervalSeconds?`, `Model?`, `AutoApproveTools?`, `DryRun?`, `TaskTimeoutSeconds?`, `SystemPrompt?`
- Flat repo fields: `RepositoryUrl?`, `RepositoryBranch?`, `RepositoryAccessToken?`
- `List<AgentProfileRepository> Repositories`
- `List<AgentProfileMcpServer> McpServers`

### 8.2 Configuration Merge Method

A new method on `AgentIdentityOptions`:

```
ApplyServerConfiguration(AgentProfileConfiguration? serverConfig)
```

This method applies server-side values according to the merge rules in SACFG-61 through SACFG-65. It is called once per polling cycle after the profile check succeeds, allowing runtime changes to propagate without restarting the agent.

### 8.3 Minimal Local Config

After this feature, the minimal `appsettings.json` becomes:

```json
{
  "Agents": [
    {
      "Name": "my-agent",
      "PersonalAccessToken": "pat-token-here",
      "WorkingDirectory": "/path/to/working/directory",
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

All other settings come from the server. Local overrides still work for any field.

---

## 9. UI Changes

### 9.1 Edit Agent Page — Configuration Section

A new collapsible "Configuration" section below the existing Name/Description/Active fields:

- **Polling Interval** — Number input with "Use default (60s)" checkbox
- **Model** — Text input with "Use default" checkbox
- **Auto-Approve Tools** — Toggle with "Use default (on)" checkbox
- **Dry Run** — Toggle with "Use default (off)" checkbox
- **Task Timeout** — Number input with "Use default (600s)" checkbox
- **System Prompt** — Textarea with "Use default" checkbox

Each field uses a pattern where unchecking "Use default" reveals the input, and checking it sets the value to null.

### 9.2 Edit Agent Page — Repositories Section

A new "Repositories" section with:
- Table listing current repositories: Project Name, URL, Branch, Token (masked), actions (Edit, Remove)
- "Add Repository" button that opens an inline form or modal
- Form fields: Project Name (required), URL (required), Branch (default "main"), Access Token (optional, password field)

### 9.3 Edit Agent Page — MCP Servers Section

A new "MCP Servers" section with:
- Table listing current MCP servers: Name, Type, URL/Command, actions (Edit, Remove)
- "Add MCP Server" button
- Form fields vary by type:
  - HTTP: Name, URL, Auth Header (optional, password field)
  - Stdio: Name, Command, Args (textarea, one per line), Environment Variables (key-value pairs)

### 9.4 Agent Detail Page

The read-only detail page shows:
- Configuration summary (or "Using agent defaults" if no config)
- Repositories list
- MCP servers list

---

## 10. Testing Requirements

| ID | Requirement |
|----|------------|
| SACFG-TEST-01 | Unit tests for `AgentConfigurationService`: CRUD operations for configuration, repositories, and MCP servers |
| SACFG-TEST-02 | Unit tests for `AgentSecretEncryptor`: encrypt/decrypt round-trip, null handling, mask formatting |
| SACFG-TEST-03 | Unit tests for `ProfileTools`: verify configuration block is included for agents, absent for humans, null when no config exists |
| SACFG-TEST-04 | Unit tests for `AgentIdentityOptions.ApplyServerConfiguration`: all merge rules (SACFG-61 through SACFG-67) |
| SACFG-TEST-05 | Unit tests for repository merge: local wins on name collision, server-only entries appended |
| SACFG-TEST-06 | Unit tests for MCP server merge: local wins on name collision, server-only entries appended |
| SACFG-TEST-07 | Integration test: agent with no server config uses local config unchanged |
| SACFG-TEST-08 | Integration test: agent with server config merges correctly |
| SACFG-TEST-09 | Admin UI test: create config, add repositories, add MCP servers, verify saved correctly |
| SACFG-TEST-10 | Security test: encrypted fields are never logged, masked correctly in UI |
| SACFG-TEST-11 | Migration test: migration applies cleanly, existing data unaffected |
