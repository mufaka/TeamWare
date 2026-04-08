# TeamWare - Edit Agent Repositories and MCP Servers Implementation Plan

This document defines the phased implementation plan for inline editing of agent repository and MCP server entries on the Edit Agent page, based on the [Edit Agent Repositories and MCP Servers Idea Document](EditMcpAndRepositoryIdea.md). Each phase builds on the previous one and is broken into work items suitable for GitHub Issues.

---

## Progress Summary

| Phase | Description | Status |
|-------|------------|--------|
| 51 | Edit Agent Repositories and MCP Servers | Not Started |

---

## Current State

All previous phases (0–50) are complete. The workspace includes:

- `AgentRepository` and `AgentMcpServer` entities with encrypted secret fields
- `IAgentConfigurationService` with `UpdateRepositoryAsync` and `UpdateMcpServerAsync` already defined and implemented
- `AdminController` with `UpdateAgentRepository` and `UpdateAgentMcpServer` POST actions already wired up
- `EditAgentViewModel.Repositories` (`List<AgentRepositoryDto>`) and `EditAgentViewModel.McpServers` (`List<AgentMcpServerDto>`) populated with masked secret values from `GetConfigurationAsync`
- Add and Remove flows fully working for both entity types

The missing pieces are:
1. The service update methods unconditionally overwrite secrets — a blank submission clears the stored value rather than preserving it. The "keep current / clear" secret pattern must be added.
2. The Edit Agent page has no edit UI — no Edit buttons, no expand-to-edit rows, no Alpine.js state.

---

## Guiding Principles

All guiding principles from previous implementation plans continue to apply:

1. **Vertical slices** — Each phase delivers end-to-end working functionality.
2. **Tests accompany every feature** — No phase is complete without its test cases.
3. **One type per file** — Enforced throughout (MAINT-01).
4. **MVC only** — Controllers and Views, no Razor Pages.

Additionally:

5. **Secrets are write-only from the UI** — The server never sends decrypted values to the browser. Masked values are used as placeholder text only.
6. **Keep existing patterns** — The edit experience mirrors the existing Add and Remove flows: standard form POST, redirect-after-POST, `TempData` success/error messages.

---

## Phase 51: Edit Agent Repositories and MCP Servers

### 51.1 Service Layer: Keep-Current Secret Logic

The existing `UpdateRepositoryAsync` and `UpdateMcpServerAsync` implementations unconditionally call `_encryptor.Encrypt(dto.AccessToken)` (and equivalents), which sets the encrypted field to `null` when the caller submits a blank value. This must be fixed before the edit UI can be built safely.

- [ ] Add `ClearAccessToken` (bool) to `SaveAgentRepositoryDto`
  - Semantics: `true` → explicitly null the stored token; `false` + blank `AccessToken` → keep existing encrypted value; `false` + non-blank `AccessToken` → encrypt and store new value
- [ ] Add `ClearAuthHeader` (bool) and `ClearEnv` (bool) to `SaveAgentMcpServerDto`
  - Same three-way semantics for each field
- [ ] Update `UpdateRepositoryAsync` in `AgentConfigurationService` to apply keep-current logic for `EncryptedAccessToken`
- [ ] Update `UpdateMcpServerAsync` in `AgentConfigurationService` to apply keep-current logic for `EncryptedAuthHeader` and `EncryptedEnv`
- [ ] Verify `AddRepositoryAsync` and `AddMcpServerAsync` are unaffected (new entries with no secret are already handled correctly)
- [ ] Add unit tests in `AgentConfigurationServiceTests`:
  - `UpdateRepositoryAsync` with blank token preserves existing encrypted value
  - `UpdateRepositoryAsync` with `ClearAccessToken = true` nulls the token regardless of token field value
  - `UpdateRepositoryAsync` with a new token value encrypts and stores it
  - Same three scenarios for `UpdateMcpServerAsync` covering `AuthHeader` and `Env` independently

### 51.2 Repository Edit UI

Add an inline expand-to-edit row to the Repositories table in `EditAgent.cshtml`.

- [ ] Wrap the Repositories `<table>` in a parent `<div x-data="{ editingId: null }">` to hold single-open-at-a-time state
- [ ] Add an **Edit** button to each repository row's Actions cell (alongside the existing Remove button)
  - `@@click` toggles `editingId` between the row's `@repo.Id` and `null`
  - Button label changes to "Cancel" when that row is open
- [ ] Add an Alpine.js-controlled `<tr>` immediately after each data row:
  - `x-show="editingId === @repo.Id"` with `x-transition`
  - On show (`x-init` or `@@click` on the Edit button), trigger `$el.scrollIntoView({ behavior: 'smooth', block: 'nearest' })` so the form is visible
  - Contains a single `<td colspan="5">` wrapping the edit form
- [ ] Edit form posts to `UpdateAgentRepository` and includes:
  - `hidden`: `userId`, `repositoryId` (`@repo.Id`), `DisplayOrder` (`@repo.DisplayOrder`)
  - `<select name="ProjectName">`: populated from `Model.AvailableProjects`, pre-selected to `@repo.ProjectName`; if `@repo.ProjectName` is not present in `AvailableProjects`, render it as an additional `<option value="@repo.ProjectName" disabled selected>@repo.ProjectName (no longer available)</option>`
  - `Url` text input, pre-populated with `@repo.Url`
  - `Branch` text input, pre-populated with `@repo.Branch`
  - `AccessToken` password input with placeholder `@(repo.AccessToken ?? "No token set")` (the masked value); blank on submit = keep current
  - `ClearAccessToken` checkbox (rendered only when `@repo.AccessToken != null`), labelled "Clear access token"
  - Submit button ("Save Changes") and a Cancel link that sets `editingId = null`
- [ ] Add integration tests in `AdminControllerAgentConfigTests`:
  - `EditAgent_Get_ShowsEditButtonForRepository` — Edit button is present in the rendered page when a repository exists
  - `UpdateAgentRepository_Success_RedirectsToEditAgent` — POST updates fields and redirects with success message
  - `UpdateAgentRepository_BlankToken_PreservesExistingToken` — POST with blank token does not clear the stored token
  - `UpdateAgentRepository_ClearToken_NullsStoredToken` — POST with `ClearAccessToken=true` removes the token

### 51.3 MCP Server Edit UI

Add an inline expand-to-edit row to the MCP Servers table in `EditAgent.cshtml`.

- [ ] Wrap the MCP Servers `<table>` in a parent `<div x-data="{ editingId: null }">` to hold single-open-at-a-time state
- [ ] Add an **Edit** button to each MCP server row's Actions cell (alongside the existing Remove button)
  - `@@click` toggles `editingId`; button label changes to "Cancel" when open
- [ ] Add an Alpine.js-controlled `<tr>` immediately after each data row:
  - `x-show="editingId === @server.Id"` with `x-transition` and scroll-into-view on open
  - Contains a single `<td colspan="5">` wrapping the edit form
- [ ] Edit form posts to `UpdateAgentMcpServer` and includes:
  - `hidden`: `userId`, `mcpServerId` (`@server.Id`), `DisplayOrder` (`@server.DisplayOrder`)
  - Type-preserving Alpine.js local state: `x-data="{ serverType: '@server.Type' }"` so switching type during edit preserves field values until save
  - `Name` text input, pre-populated with `@server.Name`
  - `Type` select (http / stdio), pre-selected via `x-model="serverType"`
  - HTTP-only section (`x-show="serverType === 'http'"`):
    - `Url` text input, pre-populated with `@server.Url`
    - `AuthHeader` password input with placeholder showing `@server.AuthHeader` (masked) or "No auth header set"
    - `ClearAuthHeader` checkbox (rendered only when `@server.AuthHeader != null`), labelled "Clear auth header"
  - Stdio-only section (`x-show="serverType === 'stdio'"`):
    - `Command` text input, pre-populated with `@server.Command`
    - `Args` text input, pre-populated with `@server.Args`
    - `Env` password input with placeholder showing `@server.Env` (masked) or "No env vars set"
    - `ClearEnv` checkbox (rendered only when `@server.Env != null`), labelled "Clear environment variables"
  - Submit button ("Save Changes") and a Cancel link that sets `editingId = null`
- [ ] Add integration tests in `AdminControllerAgentConfigTests`:
  - `EditAgent_Get_ShowsEditButtonForMcpServer` — Edit button present when an MCP server exists
  - `UpdateAgentMcpServer_Http_Success_RedirectsToEditAgent` — POST with HTTP type updates fields and redirects
  - `UpdateAgentMcpServer_Stdio_Success_RedirectsToEditAgent` — POST with stdio type updates fields and redirects
  - `UpdateAgentMcpServer_BlankAuthHeader_PreservesExistingAuthHeader`
  - `UpdateAgentMcpServer_ClearAuthHeader_NullsStoredAuthHeader`
  - `UpdateAgentMcpServer_BlankEnv_PreservesExistingEnv`
  - `UpdateAgentMcpServer_ClearEnv_NullsStoredEnv`

---

## GitHub Issue Map

### Phase 51 (label: `Phase 51: Edit Agent Repositories and MCP Servers`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 51.1 Service Layer: Keep-Current Secret Logic | TBD | — |
| 51.2 Repository Edit UI | TBD | — |
| 51.3 MCP Server Edit UI | TBD | — |
