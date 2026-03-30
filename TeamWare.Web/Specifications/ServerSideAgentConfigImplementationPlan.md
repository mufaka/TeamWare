# TeamWare - Server-Side Agent Configuration Implementation Plan

This document defines the phased implementation plan for moving agent configuration into the TeamWare web application, based on the [Server-Side Agent Configuration Specification](ServerSideAgentConfigSpecification.md). Each phase builds on the previous one and is broken into work items suitable for GitHub Issues.

---

## Progress Summary

| Phase | Description | Status |
|-------|------------|--------|
| 44 | Agent Configuration Data Model | Complete |
| 45 | Agent Configuration Admin UI | Complete |
| 46 | MCP Profile Configuration Response | Complete |
| 47 | Agent-Side Configuration Merge | Not Started |
| 48 | Server-Side Config Polish and Hardening | Not Started |

---

## Current State

All previous phases (0–43) are complete or in progress. The workspace includes:

- Agent user management in the admin panel (create, edit, pause/resume, delete)
- `get_my_profile` MCP tool returning agent identity and active status
- Agent process with full local configuration (`AgentIdentityOptions`) including multi-repo support
- ASP.NET Core Data Protection already available in the application

This feature extends the existing agent infrastructure to support centralized configuration management.

---

## Guiding Principles

All guiding principles from previous implementation plans continue to apply:

1. **Vertical slices** — Each phase delivers end-to-end working functionality.
2. **Tests accompany every feature** — No phase is complete without its test cases.
3. **One type per file** — Enforced throughout (MAINT-01).
4. **MVC only** — Controllers and Views, no Razor Pages.
5. **Backward compatible** — Agents with full local config continue to work unchanged.
6. **Local overrides server** — Host-specific settings always win.
7. **Secure by default** — Secrets encrypted at rest, never logged.

---

## Phase 44: Agent Configuration Data Model

Create the server-side data model for agent configuration, including the entities, EF Core configuration, migration, and the service layer for managing configuration CRUD operations.

### 44.1 Entity Creation and Migration

- [x] Create `AgentConfiguration` entity in `TeamWare.Web/Models/AgentConfiguration.cs` (SACFG-01, SACFG-02, SACFG-03, SACFG-30)
  - [x] `Id` (int, PK), `UserId` (string, FK to ApplicationUser, unique)
  - [x] Nullable behavioral fields: `PollingIntervalSeconds`, `Model`, `AutoApproveTools`, `DryRun`, `TaskTimeoutSeconds`, `SystemPrompt`
  - [x] Flat repo fields: `RepositoryUrl`, `RepositoryBranch`, `EncryptedRepositoryAccessToken`
  - [x] `CreatedAt`, `UpdatedAt` (DateTime, UTC)
  - [x] Navigation: `ApplicationUser User`, `ICollection<AgentRepository> Repositories`, `ICollection<AgentMcpServer> McpServers`
- [x] Create `AgentRepository` entity in `TeamWare.Web/Models/AgentRepository.cs` (SACFG-10, SACFG-11, SACFG-12, SACFG-13)
  - [x] `Id`, `AgentConfigurationId` (FK), `ProjectName`, `Url`, `Branch` (default "main"), `EncryptedAccessToken`, `DisplayOrder`
- [x] Create `AgentMcpServer` entity in `TeamWare.Web/Models/AgentMcpServer.cs` (SACFG-20, SACFG-21, SACFG-22)
  - [x] `Id`, `AgentConfigurationId` (FK), `Name`, `Type`, `Url`, `EncryptedAuthHeader`, `Command`, `Args` (JSON), `EncryptedEnv` (encrypted JSON), `DisplayOrder`
- [x] Add `AgentConfiguration` navigation property to `ApplicationUser`
- [x] Add `DbSet<AgentConfiguration>`, `DbSet<AgentRepository>`, `DbSet<AgentMcpServer>` to `ApplicationDbContext`
- [x] Configure EF Core relationships and constraints in `OnModelCreating`:
  - [x] `AgentConfiguration` → `ApplicationUser`: one-to-one, cascade delete
  - [x] `AgentRepository` → `AgentConfiguration`: one-to-many, cascade delete
  - [x] `AgentMcpServer` → `AgentConfiguration`: one-to-many, cascade delete
  - [x] Unique index on `AgentConfiguration.UserId`
  - [x] Unique index on `(AgentRepository.AgentConfigurationId, AgentRepository.ProjectName)`
  - [x] Max length constraints via fluent API or attributes
- [x] Create EF Core migration `AddAgentConfiguration`
- [x] Write tests verifying migration applies cleanly and existing data is unaffected (SACFG-NF-04, SACFG-TEST-11)

### 44.2 Encryption Helper Service

- [x] Create `IAgentSecretEncryptor` interface in `TeamWare.Web/Services/IAgentSecretEncryptor.cs`
  - [x] `string? Encrypt(string? plaintext)`
  - [x] `string? Decrypt(string? ciphertext)`
  - [x] `string? MaskForDisplay(string? plaintext)` — returns masked value like `ghp_****xyz`
- [x] Implement `AgentSecretEncryptor` in `TeamWare.Web/Services/AgentSecretEncryptor.cs`
  - [x] Use `IDataProtector` from ASP.NET Core Data Protection with purpose string `"AgentSecrets.v1"`
  - [x] Handle null/empty input gracefully (return null)
  - [x] `MaskForDisplay`: show first 4 and last 3 chars, mask middle with `****`; if token ≤ 8 chars, show `****`
- [x] Register `IAgentSecretEncryptor` in DI as singleton
- [x] Write unit tests for encrypt/decrypt round-trip, null handling, and mask formatting (SACFG-TEST-02)

### 44.3 Agent Configuration Service

- [x] Create `IAgentConfigurationService` interface in `TeamWare.Web/Services/IAgentConfigurationService.cs`
  - [x] `GetConfigurationAsync(string userId)` → `ServiceResult<AgentConfigurationDto?>`
  - [x] `SaveConfigurationAsync(string userId, SaveAgentConfigurationDto dto)` → `ServiceResult`
  - [x] `AddRepositoryAsync(string userId, SaveAgentRepositoryDto dto)` → `ServiceResult<int>`
  - [x] `UpdateRepositoryAsync(int repositoryId, SaveAgentRepositoryDto dto)` → `ServiceResult`
  - [x] `RemoveRepositoryAsync(int repositoryId)` → `ServiceResult`
  - [x] `AddMcpServerAsync(string userId, SaveAgentMcpServerDto dto)` → `ServiceResult<int>`
  - [x] `UpdateMcpServerAsync(int mcpServerId, SaveAgentMcpServerDto dto)` → `ServiceResult`
  - [x] `RemoveMcpServerAsync(int mcpServerId)` → `ServiceResult`
  - [x] `GetDecryptedConfigurationAsync(string userId)` → `ServiceResult<AgentConfigurationDto?>`
- [x] Create DTO classes:
  - [x] `AgentConfigurationDto` — mirrors entity with decrypted/masked secrets
  - [x] `SaveAgentConfigurationDto` — behavioral fields only (repos and MCP servers managed separately)
  - [x] `AgentRepositoryDto` — repository fields with plaintext token for display
  - [x] `SaveAgentRepositoryDto` — repository fields with plaintext token for save
  - [x] `AgentMcpServerDto` — MCP server fields with decrypted secrets
  - [x] `SaveAgentMcpServerDto` — MCP server fields with plaintext secrets for save
- [x] Implement `AgentConfigurationService`:
  - [x] `GetConfigurationAsync`: Load config with includes, decrypt secrets for display (masked)
  - [x] `SaveConfigurationAsync`: Create or update behavioral fields, set `UpdatedAt`
  - [x] Repository CRUD: validate uniqueness of `ProjectName`, encrypt tokens
  - [x] MCP server CRUD: encrypt auth headers and env vars
  - [x] `GetDecryptedConfigurationAsync`: Same as Get but with fully decrypted secrets (for MCP tool use)
  - [x] Validate that the target user `IsAgent = true` before any operation
- [x] Register `IAgentConfigurationService` in DI
- [x] Write unit tests for all CRUD operations (SACFG-TEST-01)

---

## Phase 45: Agent Configuration Admin UI

Extend the existing Edit Agent page with configuration management sections. Uses the same Tailwind CSS 4 / HTMX / Alpine.js patterns as the rest of the admin UI.

### 45.1 Edit Agent — Configuration Section

- [x] Create `AgentConfigurationViewModel` in `TeamWare.Web/ViewModels/AgentConfigurationViewModel.cs`
  - [x] Nullable fields for each behavioral property with "UseDefault" boolean companions
  - [x] Example: `int? PollingIntervalSeconds`, `bool PollingIntervalUseDefault`
- [x] Extend `EditAgentViewModel` with an `AgentConfigurationViewModel Configuration` property
- [x] Update `AdminController.EditAgent` (GET) to load and populate configuration
- [x] Update `AdminController.EditAgent` (POST) to save configuration via `IAgentConfigurationService`
- [x] Update `EditAgent.cshtml` with a collapsible "Configuration" section (SACFG-40, SACFG-41):
  - [x] Polling Interval: number input with "Use default (60s)" checkbox
  - [x] Model: text input with "Use default" checkbox
  - [x] Auto-Approve Tools: toggle with "Use default (on)" checkbox
  - [x] Dry Run: toggle with "Use default (off)" checkbox
  - [x] Task Timeout: number input with "Use default (600s)" checkbox
  - [x] System Prompt: textarea with "Use default" checkbox
  - [x] Alpine.js for show/hide behavior when toggling "Use default"
- [x] Write tests for configuration save/load round-trip

### 45.2 Edit Agent — Repositories Section

- [x] Add `List<AgentRepositoryDto> Repositories` to `EditAgentViewModel`
- [x] Create `_AgentRepositoryRow.cshtml` partial view for each repository row
- [x] Create `_AgentRepositoryForm.cshtml` partial view for add/edit form
- [x] Add controller actions for repository CRUD (can be HTMX endpoints):
  - [x] `POST AdminController.AddAgentRepository(string userId, SaveAgentRepositoryDto dto)`
  - [x] `POST AdminController.UpdateAgentRepository(int repositoryId, SaveAgentRepositoryDto dto)`
  - [x] `POST AdminController.RemoveAgentRepository(int repositoryId)`
- [x] Update `EditAgent.cshtml` with "Repositories" section (SACFG-42):
  - [x] Table showing Project Name, URL, Branch, Token (masked), Edit/Remove buttons
  - [x] "Add Repository" button
  - [x] HTMX for inline add/edit/remove without full page reload
- [x] Access tokens displayed as masked values (SACFG-44)
- [x] Write tests for repository CRUD endpoints

### 45.3 Edit Agent — MCP Servers Section

- [x] Add `List<AgentMcpServerDto> McpServers` to `EditAgentViewModel`
- [x] Create `_AgentMcpServerRow.cshtml` partial view for each MCP server row
- [x] Create `_AgentMcpServerForm.cshtml` partial view for add/edit form
  - [x] Dynamic form: show URL/AuthHeader fields for HTTP type, Command/Args/Env for stdio type
  - [x] Alpine.js for type-dependent field visibility
- [x] Add controller actions for MCP server CRUD:
  - [x] `POST AdminController.AddAgentMcpServer(string userId, SaveAgentMcpServerDto dto)`
  - [x] `POST AdminController.UpdateAgentMcpServer(int mcpServerId, SaveAgentMcpServerDto dto)`
  - [x] `POST AdminController.RemoveAgentMcpServer(int mcpServerId)`
- [x] Update `EditAgent.cshtml` with "MCP Servers" section (SACFG-43)
- [x] Auth headers and env vars displayed as masked values (SACFG-44)
- [x] Write tests for MCP server CRUD endpoints

### 45.4 Agent Detail Page Updates

- [x] Extend `AgentDetailViewModel` with configuration summary, repositories, and MCP servers
- [x] Update `AdminController.AgentDetail` to load configuration data
- [x] Update `AgentDetail.cshtml` with read-only configuration summary (SACFG-45):
  - [x] "Configuration" card: list of set values or "Using agent defaults" if no config
  - [x] "Repositories" card: table of repositories or "None configured"
  - [x] "MCP Servers" card: table of MCP servers or "None configured"
- [x] Write tests for detail page rendering

---

## Phase 46: MCP Profile Configuration Response

Enrich the `get_my_profile` MCP tool response with the server-side configuration block.

### 46.1 ProfileTools Update

- [x] Inject `IAgentConfigurationService` into `ProfileTools.get_my_profile` (via DI parameter)
- [x] After building the base profile response, check if user `IsAgent`
- [x] If agent, call `GetDecryptedConfigurationAsync(userId)` to load config with decrypted secrets
- [x] Build the `configuration` object per SACFG-50 through SACFG-56:
  - [x] Include non-null behavioral fields
  - [x] Include `repositories` array with decrypted access tokens
  - [x] Include `mcpServers` array with decrypted auth headers and env vars
  - [x] Include flat repo fields if set
  - [x] Set `configuration` to `null` if no `AgentConfiguration` record exists
- [x] Human user profiles remain unchanged (SACFG-55)
- [x] Write unit tests:
  - [x] Agent with configuration → configuration block included (SACFG-TEST-03)
  - [x] Agent without configuration → configuration is null (SACFG-TEST-03)
  - [x] Human user → no configuration field (SACFG-TEST-03)
  - [x] Verify decrypted secrets appear in response (for agent consumption)

### 46.2 Agent-Side Profile Model Update

- [x] Create `AgentProfileConfiguration` class in `TeamWare.Agent/Mcp/AgentProfileConfiguration.cs`:
  - [x] `int? PollingIntervalSeconds`
  - [x] `string? Model`
  - [x] `bool? AutoApproveTools`
  - [x] `bool? DryRun`
  - [x] `int? TaskTimeoutSeconds`
  - [x] `string? SystemPrompt`
  - [x] `string? RepositoryUrl`, `string? RepositoryBranch`, `string? RepositoryAccessToken`
  - [x] `List<AgentProfileRepository>? Repositories`
  - [x] `List<AgentProfileMcpServer>? McpServers`
- [x] Create `AgentProfileRepository` class in `TeamWare.Agent/Mcp/AgentProfileRepository.cs`:
  - [x] `string ProjectName`, `string Url`, `string Branch`, `string? AccessToken`
- [x] Create `AgentProfileMcpServer` class in `TeamWare.Agent/Mcp/AgentProfileMcpServer.cs`:
  - [x] `string Name`, `string Type`, `string? Url`, `string? AuthHeader`, `string? Command`, `List<string>? Args`, `Dictionary<string, string>? Env`
- [x] Add `AgentProfileConfiguration? Configuration` property to existing `AgentProfile` class
- [x] Verify `TeamWareMcpClient.GetMyProfileAsync` correctly deserializes the new `configuration` field (existing `PropertyNameCaseInsensitive = true` should handle it)
- [x] Write deserialization tests with sample JSON

---

## Phase 47: Agent-Side Configuration Merge

Implement the merge logic that applies server-side configuration to the agent's runtime options.

### 47.1 Configuration Merge Logic

- [ ] Add `ApplyServerConfiguration(AgentProfileConfiguration? serverConfig)` method to `AgentIdentityOptions` (SACFG-60)
- [ ] Implement merge rules (SACFG-61):
  - [ ] `PollingIntervalSeconds`: apply if local is default (60) and server value is not null
  - [ ] `Model`: apply if local is null and server value is not null
  - [ ] `AutoApproveTools`: apply if local is default (true) and server value is not null
  - [ ] `DryRun`: apply if local is default (false) and server value is not null
  - [ ] `TaskTimeoutSeconds`: apply if local is default (600) and server value is not null
  - [ ] `SystemPrompt`: apply if local is null and server value is not null
- [ ] Implement flat repo field merge (SACFG-31):
  - [ ] Apply `RepositoryUrl`, `RepositoryBranch`, `RepositoryAccessToken` if local values are null
- [ ] Implement `Repositories` merge (SACFG-62):
  - [ ] Match by `ProjectName` (case-insensitive)
  - [ ] Local entries win on collision
  - [ ] Server-only entries are appended
- [ ] Implement `McpServers` merge (SACFG-63):
  - [ ] Match by `Name` (case-insensitive)
  - [ ] Local entries win on collision
  - [ ] Server-only entries are appended
- [ ] Never merge `WorkingDirectory` or `PersonalAccessToken` (SACFG-64, SACFG-65)
- [ ] Log merged configuration at `Debug` level with secrets redacted (SACFG-66)
- [ ] When `serverConfig` is null, do nothing (SACFG-67)
- [ ] Write comprehensive unit tests for all merge rules (SACFG-TEST-04, SACFG-TEST-05, SACFG-TEST-06)

### 47.2 Polling Loop Integration

- [ ] Update `AgentPollingLoop.ExecuteCycleAsync` to call `_options.ApplyServerConfiguration(profile.Configuration)` after the profile check succeeds
- [ ] This enables runtime config changes to take effect on the next polling cycle without restarting the agent
- [ ] Write integration tests:
  - [ ] Agent with no server config uses local config unchanged (SACFG-TEST-07)
  - [ ] Agent with server config merges correctly (SACFG-TEST-08)
  - [ ] Config changes propagate on next cycle

### 47.3 Documentation Updates

- [ ] Update `TeamWare.Agent/README.md`:
  - [ ] Add "Server-Side Configuration" section explaining the feature
  - [ ] Document the minimal bootstrap config
  - [ ] Document the merge precedence rules
  - [ ] Update the configuration reference table with merge behavior notes
- [ ] Update `TeamWare.Agent/appsettings.example.json` comments to reference server-side config

---

## Phase 48: Server-Side Config Polish and Hardening

Final phase: security review, edge cases, UI polish, and documentation.

### 48.1 Security Hardening

- [ ] Verify encrypted fields are never logged at any level (SACFG-NF-06, SACFG-TEST-10)
- [ ] Verify decrypted secrets are only included in MCP responses, never in admin UI HTML
- [ ] Verify admin authorization is enforced on all configuration endpoints
- [ ] Verify an agent can only read its own configuration via MCP (not other agents')
- [ ] Pen-test the mask display logic for edge cases (short tokens, empty strings)

### 48.2 Edge Cases and Regression Testing

- [ ] Agent with server config and no local repos → server repos used
- [ ] Agent with local repos and no server config → local repos used
- [ ] Agent with both → merged correctly
- [ ] Agent with server MCP servers and local MCP servers with same name → local wins
- [ ] Agent configuration deleted while agent is running → next cycle uses local config
- [ ] Very large system prompt (10,000 characters) → handled correctly
- [ ] Unicode characters in project names, repo URLs → handled correctly
- [ ] Data Protection key rotation → existing encrypted values can still be decrypted (ASP.NET Core handles this automatically)

### 48.3 UI Consistency Review

- [ ] Verify all new form fields match existing Tailwind CSS patterns
- [ ] Verify HTMX interactions work smoothly (no flicker, proper loading states)
- [ ] Verify validation messages display correctly for all fields
- [ ] Verify responsive layout works on mobile for the expanded Edit Agent page
- [ ] Verify dark mode styling for all new UI elements

### 48.4 Documentation

- [ ] Update `TeamWare.Web/Specifications/ServerSideAgentConfigSpecification.md` with any changes from implementation
- [ ] Add configuration management to the admin help/guide if one exists
- [ ] Verify all new DTOs and services have XML doc comments
