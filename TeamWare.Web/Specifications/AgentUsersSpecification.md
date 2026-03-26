# TeamWare - Agent Users Specification

## 1. Introduction

### 1.1 Purpose

This document provides the formal specification for the Agent Users feature being added to TeamWare. It defines the functional requirements, data model changes, service layer additions, MCP tool additions, UI changes, and testing requirements needed to make TeamWare **agent-ready** — meaning external autonomous processes can participate as first-class team members through the existing MCP server. This specification is a companion to the [main TeamWare specification](Specification.md), the [MCP Server specification](McpServerSpecification.md), and follows the same conventions.

### 1.2 Scope

The Agent Users feature introduces the ability to distinguish automated agent users from human users within TeamWare and provides the supporting infrastructure for external agent processes to interact with the system. The feature is divided into three areas:

- **Area A** — Data model changes (`IsAgent`, `AgentDescription`, `IsActive` on `ApplicationUser`), migration, and seed data updates.
- **Area B** — Agent management UI in the admin panel (create, edit, pause/resume, dashboard, activity feed).
- **Area C** — New MCP tool (`get_my_profile`) and UI enhancements (bot badge on comments and activity).

This specification covers only what TeamWare itself needs to change. The external agent process (expected to use the GitHub Copilot SDK) is a separate project and is not specified here.

### 1.3 Definitions and Acronyms

| Term | Definition |
|------|-----------|
| Agent User | An `ApplicationUser` with `IsAgent = true`. Represents an automated process that interacts with TeamWare via the MCP server. |
| Human User | An `ApplicationUser` with `IsAgent = false` (the default). A regular team member who uses the web UI. |
| PAT | Personal Access Token. The authentication mechanism used by agent users (and optionally human users) to access the MCP endpoint. Defined in the [MCP Server specification](McpServerSpecification.md). |
| MCP | Model Context Protocol. The protocol used by external agent processes to interact with TeamWare. Defined in the [MCP Server specification](McpServerSpecification.md). |
| Bot Badge | A visual indicator in the UI that distinguishes actions and content created by agent users from those created by human users. |
| GitHub Copilot SDK | The expected SDK for the external agent process. Out of scope for this specification. |

### 1.4 Design Principles

- **Optional and non-invasive** — Agent users are entirely optional. Teams that do not create agent users see no difference in their experience. No existing behavior changes.
- **Reuse, do not duplicate** — Agent users are regular `ApplicationUser` records. All existing services (task assignment, comments, activity logs, project membership) work without modification.
- **Human oversight** — Agents only work on tasks explicitly assigned by human users. Agents move tasks to "In Review" rather than "Done," ensuring a human review checkpoint.
- **Audit trail** — All actions by agents are recorded in the same activity logs and comment history as human actions, with clear visual indicators that the actor was an agent.
- **Consistent with existing patterns** — Entity changes follow existing conventions. Services use `ServiceResult<T>`. UI uses Tailwind CSS 4, HTMX, and Alpine.js.

---

## 2. Technology Additions

No new technology dependencies are introduced. All changes use existing frameworks and libraries already in the TeamWare stack.

---

## 3. Functional Requirements

### 3.1 Agent User Identity

| ID | Requirement |
|----|------------|
| AGT-01 | The `ApplicationUser` entity shall include a boolean property `IsAgent` with a default value of `false` |
| AGT-02 | The `ApplicationUser` entity shall include an optional string property `AgentDescription` (max 2000 characters) to describe the agent's purpose and capabilities |
| AGT-03 | The `ApplicationUser` entity shall include a boolean property `IsAgentActive` with a default value of `true`, controlling whether the agent is currently enabled |
| AGT-04 | Existing users shall not be affected by the migration. All existing `ApplicationUser` records shall have `IsAgent = false`, `AgentDescription = null`, and `IsAgentActive = true` after the migration |
| AGT-05 | The `IsAgent` flag shall not be modifiable after user creation. Once a user is created as an agent, it remains an agent. Once created as a human, it remains human |

### 3.2 Agent User Creation

| ID | Requirement |
|----|------------|
| AGT-10 | Agent users shall be created exclusively through the admin panel. There shall be no self-registration path for agent users |
| AGT-11 | The admin agent creation form shall require a display name and allow an optional agent description |
| AGT-12 | When creating an agent user, the system shall generate a unique username derived from the display name (e.g., `agent-codebot`) |
| AGT-13 | Agent users shall have a random, high-entropy password set during creation. This password is never displayed or used; agents authenticate only via PAT |
| AGT-14 | Upon successful agent creation, the system shall automatically generate a PAT for the new agent user and display the raw token value once, with a copy-to-clipboard button and a warning that the token will not be shown again |
| AGT-15 | Agent users shall not be assigned the site-wide `Admin` role. Agents operate as regular users with project-level `Member` role |
| AGT-16 | The email field for agent users shall be optional. If not provided, the system shall generate a placeholder email (e.g., `agent-codebot@agent.local`) that is not used for communication |

### 3.3 Agent User Management

| ID | Requirement |
|----|------------|
| AGT-20 | The admin panel shall include an "Agents" section listing all agent users with display name, description, active status, last active time, and count of assigned tasks |
| AGT-21 | Admins shall be able to edit an agent's display name and description |
| AGT-22 | Admins shall be able to toggle an agent's `IsAgentActive` status (pause/resume) |
| AGT-23 | When `IsAgentActive` is set to `false`, the PAT authentication handler shall reject authentication attempts by the agent user. The existing PATs are not revoked; they are simply ineffective while the agent is paused |
| AGT-24 | Admins shall be able to generate additional PATs for an existing agent user |
| AGT-25 | Admins shall be able to revoke individual PATs for an agent user |
| AGT-26 | Admins shall be able to view the agent's recent activity log entries |
| AGT-27 | Admins shall be able to add an agent user to projects as a `Member` from the agent management page |
| AGT-28 | Admins shall be able to delete an agent user. Deletion shall revoke all associated PATs. Activity log entries, comments, and other historical data created by the agent shall be preserved (soft reference via user ID) |

### 3.4 Agent Workflow Support

| ID | Requirement |
|----|------------|
| AGT-30 | Agent users shall be assignable to tasks through all existing assignment mechanisms (web UI task assignment, MCP `assign_task` tool) |
| AGT-31 | When listing users available for task assignment in the web UI, agent users shall be visually distinguished (e.g., a bot icon next to the name) |
| AGT-32 | The existing MCP tools (`my_assignments`, `get_task`, `update_task_status`, `add_comment`, `create_task`, `list_tasks`) shall work identically for agent users and human users. No changes to existing MCP tools are required |
| AGT-33 | The `my_assignments` MCP tool shall return only tasks with status `ToDo` or `InProgress` when called by an agent user. Tasks in `InReview` or `Done` status have been handed off and should not appear in the agent's work queue |

### 3.5 New MCP Tool: get_my_profile

| ID | Requirement |
|----|------------|
| AGT-40 | The system shall expose a new MCP tool `get_my_profile` that returns the authenticated user's profile information |
| AGT-41 | The `get_my_profile` tool shall accept no parameters |
| AGT-42 | The `get_my_profile` tool shall return a JSON object containing: `userId`, `displayName`, `email`, `isAgent`, `agentDescription` (null for human users), `isAgentActive` (null for human users), and `lastActiveAt` |
| AGT-43 | The `get_my_profile` tool shall require PAT authentication, consistent with all other MCP tools |
| AGT-44 | The `get_my_profile` tool shall be available to both human and agent users |

### 3.6 UI Enhancements

| ID | Requirement |
|----|------------|
| AGT-50 | Comments posted by agent users shall display a bot badge next to the author's display name. The badge shall be a small robot icon or a "BOT" label, consistent with the TeamWare design system |
| AGT-51 | Activity log entries created by agent users shall display the same bot badge next to the actor's display name |
| AGT-52 | The task detail page shall show the bot badge next to agent user names in the assignee list |
| AGT-53 | The project member list shall show the bot badge next to agent user names |
| AGT-54 | The user directory shall include agent users, visually distinguished with the bot badge. An optional filter shall allow showing only human users or only agent users |
| AGT-55 | The bot badge shall be implemented as a shared partial view or tag helper to ensure consistent rendering across all pages |
| AGT-56 | All agent-related UI shall follow existing TeamWare styling (Tailwind CSS 4, light/dark theme support, no emoticons or emojis) |

### 3.7 PAT Authentication Changes

| ID | Requirement |
|----|------------|
| AGT-60 | The `PatAuthenticationHandler` shall check `IsAgentActive` when authenticating an agent user. If `IsAgentActive` is `false`, authentication shall fail with a descriptive error message |
| AGT-61 | The `PatAuthenticationHandler` shall include the `IsAgent` claim in the `ClaimsPrincipal` when authenticating an agent user. This claim can be used by MCP tools to detect agent context |
| AGT-62 | The `PatAuthenticationHandler` behavior for human users shall not change. The `IsAgentActive` check only applies when the authenticated user has `IsAgent = true` |

---

## 4. Data Model

### 4.1 Modified Entities

#### ApplicationUser

Three new properties are added to the existing `ApplicationUser` entity:

| Property | Type | Default | Constraints | Description |
|----------|------|---------|-------------|-------------|
| `IsAgent` | `bool` | `false` | Not null | Whether this user is an automated agent |
| `AgentDescription` | `string?` | `null` | Max 2000 chars | Human-readable description of the agent's purpose (null for human users) |
| `IsAgentActive` | `bool` | `true` | Not null | Whether the agent is currently enabled. Ignored for human users |

**Migration notes:**
- All existing rows receive `IsAgent = false`, `AgentDescription = null`, `IsAgentActive = true`.
- No indexes are needed on these columns. Queries filtering by `IsAgent` are admin-only and low-frequency.

### 4.2 No New Entities

No new database entities are introduced. Agent users are `ApplicationUser` records. Agent authentication uses the existing `PersonalAccessToken` entity. Agent project membership uses the existing `ProjectMember` entity. Agent task assignments use the existing `TaskAssignment` entity.

---

## 5. Service Layer Design

### 5.1 IAdminService (Extended)

The existing `IAdminService` is extended with methods for agent user management.

| Method | Parameters | Returns | Description |
|--------|-----------|---------|-------------|
| `CreateAgentUser` | `string displayName, string? agentDescription` | `ServiceResult<(ApplicationUser User, string RawToken)>` | Creates a new `ApplicationUser` with `IsAgent = true`, generates a random password, generates a PAT, and returns both the user and the raw token value |
| `UpdateAgentUser` | `string userId, string displayName, string? agentDescription` | `ServiceResult` | Updates the agent's display name and description. Returns failure if the user is not an agent |
| `SetAgentActive` | `string userId, bool isActive` | `ServiceResult` | Sets the `IsAgentActive` flag. Returns failure if the user is not an agent |
| `GetAgentUsers` | _(none)_ | `ServiceResult<List<AgentUserSummary>>` | Returns all users with `IsAgent = true`, including display name, description, active status, last active time, and assigned task count |
| `DeleteAgentUser` | `string userId, string adminUserId` | `ServiceResult` | Deletes the agent user and revokes all associated PATs. Records the action in the admin activity log. Returns failure if the user is not an agent |

#### AgentUserSummary (ViewModel)

| Property | Type | Description |
|----------|------|-------------|
| `UserId` | `string` | The agent user's ID |
| `DisplayName` | `string` | The agent's display name |
| `AgentDescription` | `string?` | The agent's description |
| `IsAgentActive` | `bool` | Whether the agent is currently enabled |
| `LastActiveAt` | `DateTime?` | Last activity timestamp |
| `AssignedTaskCount` | `int` | Number of tasks currently assigned to the agent (not Done) |

### 5.2 PatAuthenticationHandler (Modified)

The existing `PatAuthenticationHandler` is modified to:

1. After successful token validation, check if the owning user has `IsAgent = true`.
2. If `IsAgent = true` and `IsAgentActive = false`, return `AuthenticateResult.Fail("Agent is currently paused.")`.
3. If `IsAgent = true`, add a claim `IsAgent` with value `true` to the `ClaimsPrincipal`.
4. No changes for human users (`IsAgent = false`).

### 5.3 Existing Service Reuse

No existing services require modification. The following services are used by agent users through their existing interfaces, without any agent-specific logic:

| Service | Agent Usage |
|---------|------------|
| `ITaskService` | Agent discovers and updates tasks via MCP tools |
| `ICommentService` | Agent posts comments via MCP tools |
| `IActivityLogService` | Agent actions are logged automatically by existing service logic |
| `IProjectMemberService` | Agent is added to projects as a regular `Member` |
| `IPersonalAccessTokenService` | Agent authenticates via PAT using existing infrastructure |

---

## 6. MCP Tool Specification

### 6.1 New Tool: get_my_profile

**Class:** `ProfileTools` in `TeamWare.Web/Mcp/Tools/ProfileTools.cs`

| Tool Name | Parameters | Returns | Service Delegation |
|-----------|-----------|---------|-------------------|
| `get_my_profile` | _(none)_ | JSON object: `{ userId, displayName, email, isAgent, agentDescription, isAgentActive, lastActiveAt }` | Reads from `ClaimsPrincipal` and `UserManager<ApplicationUser>` |

**Implementation notes:**
- The tool reads the user ID from the `ClaimsPrincipal`.
- It loads the `ApplicationUser` via `UserManager<ApplicationUser>.FindByIdAsync`.
- For human users, `agentDescription` and `isAgentActive` are returned as `null`.
- Dates use ISO 8601 format. Enums use string names. Consistent with all other MCP tools.

### 6.2 Modified Tool: my_assignments

**Class:** `TaskTools` in `TeamWare.Web/Mcp/Tools/TaskTools.cs`

| Change | Description |
|--------|-------------|
| AGT-33 | When the authenticated user has `IsAgent = true` (detectable via the `IsAgent` claim added by `PatAuthenticationHandler`), the tool filters results to only include tasks with status `ToDo` or `InProgress`. Tasks in `InReview` or `Done` are excluded. For human users, behavior is unchanged. |

---

## 7. File Organization

New and modified files for the Agent Users feature:

```
TeamWare.Web/
  Models/
    ApplicationUser.cs                    (modified: add IsAgent, AgentDescription, IsAgentActive)
  Data/
    Migrations/
      <timestamp>_AddAgentUserFields.cs   (new: migration for ApplicationUser changes)
  Services/
    IAdminService.cs                      (modified: add agent management methods)
    AdminService.cs                       (modified: implement agent management methods)
  Authentication/
    PatAuthenticationHandler.cs           (modified: IsAgentActive check, IsAgent claim)
  Mcp/
    Tools/
      ProfileTools.cs                     (new: get_my_profile tool)
      TaskTools.cs                        (modified: filter for agent users in my_assignments)
  Controllers/
    AdminController.cs                    (modified: add agent management actions)
  ViewModels/
    AgentUserSummary.cs                   (new: agent list view model)
  Views/
    Admin/
      Agents.cshtml                       (new: agent list page)
      CreateAgent.cshtml                  (new: agent creation form)
      EditAgent.cshtml                    (new: agent edit form)
      AgentDetail.cshtml                  (new: agent detail with PATs and activity)
    Shared/
      _BotBadge.cshtml                    (new: shared partial for bot badge rendering)
```

---

## 8. Changes to Existing Requirements

No existing functional requirements are modified. The Agent Users feature adds new capabilities without changing existing behavior.

The following existing requirements apply with additional considerations:

| Requirement | Consideration |
|-------------|--------------|
| PAT-12 (Token validation) | `PatAuthenticationHandler` now additionally checks `IsAgentActive` for agent users |
| PAT-13 (ClaimsPrincipal) | `PatAuthenticationHandler` now additionally adds an `IsAgent` claim for agent users |
| MCP-14 (`my_assignments`) | Results are filtered for agent users per AGT-33 |
| SEC-05 (Authorization enforcement) | Agent users are subject to the same project membership checks as human users |

---

## 9. Non-Functional Requirements

| ID | Requirement |
|----|------------|
| AGT-NF-01 | The `IsAgentActive` check in `PatAuthenticationHandler` shall add negligible overhead. The user is already loaded during token validation; the check is a simple boolean comparison |
| AGT-NF-02 | The agent management pages shall be accessible only to site administrators (users with the `Admin` role), consistent with all other admin pages |
| AGT-NF-03 | The bot badge partial view shall be lightweight (no additional database queries). It shall render based solely on the `IsAgent` flag available on the user object already loaded by the page |
| AGT-NF-04 | The migration adding `IsAgent`, `AgentDescription`, and `IsAgentActive` to `ApplicationUser` shall be non-destructive and backwards-compatible |
| AGT-NF-05 | Agent user creation, PAT generation, and project membership assignment shall be audited in the admin activity log, consistent with existing admin operations |

---

## 10. UI Requirements

### 10.1 Agent Management Pages

| ID | Requirement |
|----|------------|
| AGT-UI-01 | The admin navigation shall include an "Agents" link that navigates to the agent list page |
| AGT-UI-02 | The agent list page shall display a table of all agent users with columns: Display Name, Description (truncated), Status (Active/Paused), Last Active, Assigned Tasks, and Actions (Edit, Pause/Resume) |
| AGT-UI-03 | The agent list page shall include a "Create Agent" button that navigates to the agent creation form |
| AGT-UI-04 | The agent creation form shall include fields for: Display Name (required), Description (optional, textarea), and a "Create" button |
| AGT-UI-05 | After successful agent creation, a confirmation page shall display the agent's PAT with a copy-to-clipboard button and a clearly visible warning that the token will not be shown again |
| AGT-UI-06 | The agent edit form shall include fields for: Display Name, Description, and an Active toggle |
| AGT-UI-07 | The agent detail page shall show the agent's profile information, a list of its PATs (with revoke buttons), its project memberships (with add/remove), and its recent activity log entries |
| AGT-UI-08 | All agent management UI shall follow existing TeamWare styling (Tailwind CSS 4, light/dark theme support, no emoticons or emojis) |

### 10.2 Bot Badge

| ID | Requirement |
|----|------------|
| AGT-UI-10 | The bot badge shall be rendered as a small inline element next to the user's display name |
| AGT-UI-11 | The bot badge shall use a robot icon from the existing Heroicons set or a styled "BOT" text label |
| AGT-UI-12 | The bot badge shall be visible in both light and dark themes with appropriate contrast |
| AGT-UI-13 | The bot badge shall appear in all contexts where a user's display name is shown and the user has `IsAgent = true`: comments, activity log entries, task assignee lists, project member lists, and user directory |

---

## 11. Testing Requirements

| ID | Requirement |
|----|------------|
| AGT-TEST-01 | Unit tests shall verify that `CreateAgentUser` creates an `ApplicationUser` with `IsAgent = true`, generates a PAT, and returns the raw token |
| AGT-TEST-02 | Unit tests shall verify that `CreateAgentUser` sets a random password and a placeholder email when none is provided |
| AGT-TEST-03 | Unit tests shall verify that `UpdateAgentUser` returns failure when the target user is not an agent |
| AGT-TEST-04 | Unit tests shall verify that `SetAgentActive` updates the `IsAgentActive` flag and returns failure for non-agent users |
| AGT-TEST-05 | Unit tests shall verify that `GetAgentUsers` returns only users with `IsAgent = true` |
| AGT-TEST-06 | Unit tests shall verify that `DeleteAgentUser` revokes all PATs for the agent and records the action in the admin activity log |
| AGT-TEST-07 | Unit tests shall verify that `PatAuthenticationHandler` rejects authentication for agent users with `IsAgentActive = false` |
| AGT-TEST-08 | Unit tests shall verify that `PatAuthenticationHandler` adds the `IsAgent` claim for agent users |
| AGT-TEST-09 | Unit tests shall verify that `PatAuthenticationHandler` does not check `IsAgentActive` or add `IsAgent` claim for human users |
| AGT-TEST-10 | Unit tests shall verify the `get_my_profile` MCP tool returns correct data for both human and agent users |
| AGT-TEST-11 | Unit tests shall verify that `my_assignments` filters to `ToDo` and `InProgress` tasks when the caller is an agent user |
| AGT-TEST-12 | Unit tests shall verify that `my_assignments` returns all statuses (unchanged behavior) when the caller is a human user |
| AGT-TEST-13 | Integration tests shall verify end-to-end agent creation through the admin panel, including PAT generation |
| AGT-TEST-14 | Integration tests shall verify that a paused agent cannot authenticate via PAT |
| AGT-TEST-15 | Integration tests shall verify that the bot badge renders correctly in comments and activity log entries |
| AGT-TEST-16 | The migration shall be tested to verify all existing users receive `IsAgent = false` |

---

## 12. Security Considerations

| ID | Consideration |
|----|--------------|
| AGT-SEC-01 | Agent users cannot be assigned the site-wide `Admin` role. This prevents an agent from managing other users or system configuration |
| AGT-SEC-02 | Agent users authenticate only via PAT. The login form shall reject authentication attempts by agent users (if attempted via username/password) with a generic "invalid credentials" message, not revealing that the account is an agent |
| AGT-SEC-03 | The `IsAgent` flag is immutable after creation. This prevents privilege escalation by converting a limited agent user into a full human user or vice versa |
| AGT-SEC-04 | Agent PAT tokens follow the same security requirements as human PAT tokens (SHA-256 hashing, 32+ bytes of entropy, `tw_` prefix). No weaker authentication is permitted |
| AGT-SEC-05 | The `IsAgentActive` pause mechanism provides immediate suspension capability. When an agent is paused, all subsequent MCP requests are rejected without needing to revoke and regenerate tokens |
| AGT-SEC-06 | Agent users are subject to the same project membership authorization as human users. An agent can only access projects it has been explicitly added to |

---

## 13. Relationship to Other Specifications

| Specification | Relationship |
|---------------|-------------|
| [Main Specification](Specification.md) | Agent Users extends the `ApplicationUser` entity defined in the main specification. All existing conventions (one type per file, `ServiceResult<T>`, Tailwind CSS 4, HTMX) are followed. |
| [MCP Server Specification](McpServerSpecification.md) | Agent Users adds one new MCP tool (`get_my_profile`) and modifies one existing tool (`my_assignments`). All PAT authentication infrastructure defined in the MCP specification is reused. |
| [Ollama Integration Specification](OllamaIntegrationSpecification.md) | No relationship. The Ollama integration provides AI features for human users in the web UI. Agent users do not use TeamWare's Ollama integration; all LLM inference happens in the external agent process. |
