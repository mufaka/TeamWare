# TeamWare - Agent Users Implementation Plan

This document defines the phased implementation plan for the TeamWare Agent Users feature based on the [Agent Users Specification](AgentUsersSpecification.md). Each phase builds on the previous one and is broken into work items suitable for GitHub Issues. Check off items as they are completed to track progress.

---

## Progress Summary

| Phase | Description | Status |
|-------|------------|--------|
| 32 | Agent Data Model and Authentication | Not Started |
| 33 | Agent Management UI | Not Started |
| 34 | Agent MCP Tools and Bot Badge | Not Started |
| 35 | Agent Polish and Hardening | Not Started |

---

## Current State

All original phases (0-9), social feature phases (10-14), Project Lounge phases (15-21), Ollama AI Integration phases (22-25), and MCP Server Integration phases (26-31) are complete. The workspace is an ASP.NET Core MVC project (.NET 10) with:

- Full project and task management (CRUD, assignment, filtering, GTD workflow)
- Inbox capture/clarify workflow with Someday/Maybe list
- Progress tracking with activity log and deadline visibility
- Task commenting with notifications
- In-app notification system
- GTD review workflow
- User profile management and personal dashboard
- Site-wide admin role and admin dashboard with `GlobalConfiguration` management
- User directory with profile pages
- Real-time online/offline presence via SignalR
- Project invitation accept/decline workflow
- Project Lounge with real-time messaging, reactions, pinning, and message retention
- Ollama AI Integration with content rewriting and summary generation
- MCP Server with PAT authentication, read/write tools, prompts, resources, and lounge integration
- Security hardening, performance optimization, and UI polish

The Agent Users feature builds on top of this foundation. It extends `ApplicationUser` with agent-specific fields, adds agent management to the admin panel, introduces a new MCP tool (`get_my_profile`), modifies one existing MCP tool (`my_assignments`), and adds bot badge rendering throughout the UI.

---

## Guiding Principles

All guiding principles from previous implementation plans continue to apply:

1. **Vertical slices** — Each phase delivers end-to-end working functionality (model, service, controller/tools, view, tests).
2. **Tests accompany every feature** — No phase is complete without its test cases.
3. **One type per file** — Enforced throughout (MAINT-01).
4. **MVC only** — Controllers and Views, no Razor Pages (project guideline).
5. **Reuse, do not duplicate** — Agent users are standard `ApplicationUser` records. All existing services work without modification.
6. **Optional and non-invasive** — Teams that do not create agent users see no difference in their experience.

Additionally:

7. **Human oversight** — Agents only work on tasks explicitly assigned by human users. The workflow includes review checkpoints.
8. **Audit trail** — All agent actions use existing logging infrastructure. The `IsAgent` flag enables visual distinction in the UI.

---

## Phase 32: Agent Data Model and Authentication

Extend `ApplicationUser` with agent-specific fields, update the PAT authentication handler to support agent pause/resume and the `IsAgent` claim, and add agent management methods to `IAdminService`. This phase delivers no UI changes but provides the data and service infrastructure for all subsequent phases.

### 32.1 ApplicationUser Entity Changes

- [ ] Add `IsAgent` boolean property to `ApplicationUser` with default value `false` (AGT-01)
- [ ] Add `AgentDescription` optional string property (max 2000 characters) to `ApplicationUser` (AGT-02)
- [ ] Add `IsAgentActive` boolean property to `ApplicationUser` with default value `true` (AGT-03)
- [ ] Add `[StringLength(2000)]` attribute to `AgentDescription`
- [ ] Create EF Core migration `AddAgentUserFields`:
  - [ ] `IsAgent` column: `bool`, not null, default `false`
  - [ ] `AgentDescription` column: `nvarchar(2000)`, nullable
  - [ ] `IsAgentActive` column: `bool`, not null, default `true`
- [ ] Verify migration is non-destructive: all existing rows receive `IsAgent = false`, `AgentDescription = null`, `IsAgentActive = true` (AGT-04, AGT-NF-04)
- [ ] Write tests verifying migration applies cleanly and existing users are unaffected (AGT-TEST-16)

### 32.2 PAT Authentication Handler Changes

- [ ] Modify `PatAuthenticationHandler.HandleAuthenticateAsync` to check `IsAgentActive` after successful token validation (AGT-60, AGT-62):
  - [ ] If `user.IsAgent` is `true` and `user.IsAgentActive` is `false`, return `AuthenticateResult.Fail("Agent is currently paused.")`
  - [ ] If `user.IsAgent` is `false`, skip the `IsAgentActive` check entirely
- [ ] Add `IsAgent` claim to the `ClaimsPrincipal` when `user.IsAgent` is `true` (AGT-61):
  - [ ] Claim type: `"IsAgent"`, value: `"true"`
  - [ ] Only added for agent users; human users do not receive this claim
- [ ] Write unit tests verifying:
  - [ ] Agent user with `IsAgentActive = true` authenticates successfully and has `IsAgent` claim (AGT-TEST-08)
  - [ ] Agent user with `IsAgentActive = false` is rejected with descriptive error (AGT-TEST-07)
  - [ ] Human user authenticates without `IsAgent` claim and without `IsAgentActive` check (AGT-TEST-09)

### 32.3 Admin Service Agent Methods

- [ ] Add agent management methods to `IAdminService` interface (Spec Section 5.1):
  - [ ] `CreateAgentUser(string displayName, string? agentDescription)` returning `ServiceResult<(ApplicationUser User, string RawToken)>`
  - [ ] `UpdateAgentUser(string userId, string displayName, string? agentDescription)` returning `ServiceResult`
  - [ ] `SetAgentActive(string userId, bool isActive)` returning `ServiceResult`
  - [ ] `GetAgentUsers()` returning `ServiceResult<List<AgentUserSummary>>`
  - [ ] `DeleteAgentUser(string userId, string adminUserId)` returning `ServiceResult`
- [ ] Create `AgentUserSummary` view model in `TeamWare.Web/ViewModels/AgentUserSummary.cs` (Spec Section 5.1)
- [ ] Implement `CreateAgentUser` in `AdminService`:
  - [ ] Generate username from display name: lowercase, replace spaces with hyphens, prefix with `agent-` (AGT-12)
  - [ ] Generate placeholder email: `{username}@agent.local` (AGT-16)
  - [ ] Generate random high-entropy password using `RandomNumberGenerator` (AGT-13)
  - [ ] Create `ApplicationUser` with `IsAgent = true`, `IsAgentActive = true`, the display name, and the generated username/email/password
  - [ ] Do not assign the `Admin` role (AGT-15)
  - [ ] Call `IPersonalAccessTokenService.CreateTokenAsync` to generate a PAT (AGT-14)
  - [ ] Return the user and raw token value
  - [ ] Log the creation in the admin activity log (AGT-NF-05)
- [ ] Implement `UpdateAgentUser` in `AdminService`:
  - [ ] Verify the target user has `IsAgent = true`; return failure otherwise (AGT-TEST-03)
  - [ ] Update `DisplayName` and `AgentDescription`
  - [ ] Log the update in the admin activity log
- [ ] Implement `SetAgentActive` in `AdminService`:
  - [ ] Verify the target user has `IsAgent = true`; return failure otherwise (AGT-TEST-04)
  - [ ] Set `IsAgentActive` to the specified value
  - [ ] Log the status change in the admin activity log
- [ ] Implement `GetAgentUsers` in `AdminService`:
  - [ ] Query all `ApplicationUser` records where `IsAgent = true` (AGT-TEST-05)
  - [ ] Include assigned task count (tasks not in `Done` status)
  - [ ] Return as `List<AgentUserSummary>`
- [ ] Implement `DeleteAgentUser` in `AdminService`:
  - [ ] Verify the target user has `IsAgent = true`; return failure otherwise
  - [ ] Call `IPersonalAccessTokenService.RevokeAllTokensForUserAsync` (AGT-28)
  - [ ] Delete the user via `UserManager<ApplicationUser>`
  - [ ] Log the deletion in the admin activity log (AGT-TEST-06)
- [ ] Register all new types in DI (if applicable)
- [ ] Write unit tests for all five methods:
  - [ ] `CreateAgentUser`: success, PAT generation, no Admin role, generated username and email (AGT-TEST-01, AGT-TEST-02)
  - [ ] `UpdateAgentUser`: success, failure for non-agent (AGT-TEST-03)
  - [ ] `SetAgentActive`: success, failure for non-agent (AGT-TEST-04)
  - [ ] `GetAgentUsers`: returns only agents (AGT-TEST-05)
  - [ ] `DeleteAgentUser`: success, PAT revocation, admin log entry (AGT-TEST-06)

---

## Phase 33: Agent Management UI

Deliver the admin panel pages for creating, editing, listing, and managing agent users. This phase provides the web UI for all agent management operations built in Phase 32.

### 33.1 Agent List Page

- [ ] Add `Agents` action to `AdminController` that calls `IAdminService.GetAgentUsers()` and returns the agent list view (AGT-20)
- [ ] Create `Views/Admin/Agents.cshtml`:
  - [ ] Display table with columns: Display Name, Description (truncated), Status (Active/Paused), Last Active, Assigned Tasks, Actions (AGT-UI-02)
  - [ ] "Active" status shown as a green badge; "Paused" as a yellow badge
  - [ ] Include "Create Agent" button linking to the creation form (AGT-UI-03)
  - [ ] Each row has "Edit" and "Pause/Resume" action links
  - [ ] Tailwind CSS 4 styling, light/dark theme support (AGT-UI-08)
- [ ] Add "Agents" link to the admin navigation sidebar/menu (AGT-UI-01)
- [ ] Write tests verifying the agent list page renders correctly with zero agents, one agent, and multiple agents

### 33.2 Agent Creation Flow

- [ ] Add `CreateAgent` (GET) action to `AdminController` returning the creation form view
- [ ] Add `CreateAgent` (POST) action to `AdminController`:
  - [ ] Accept display name (required) and description (optional) (AGT-11)
  - [ ] Call `IAdminService.CreateAgentUser(displayName, description)`
  - [ ] On success, redirect to the agent creation confirmation page with the raw token
  - [ ] On failure, return the form with validation errors
- [ ] Create `Views/Admin/CreateAgent.cshtml`:
  - [ ] Form with Display Name (required text input) and Description (optional textarea) fields (AGT-UI-04)
  - [ ] "Create" submit button
  - [ ] Tailwind CSS 4 styling (AGT-UI-08)
- [ ] Create `Views/Admin/AgentCreated.cshtml` (confirmation page):
  - [ ] Display the agent's display name and a success message
  - [ ] Show the raw PAT in a read-only input field with a "Copy to Clipboard" button (AGT-UI-05)
  - [ ] Display a prominent warning that the token will not be shown again (AGT-UI-05)
  - [ ] Link to the agent detail page
- [ ] Write tests verifying creation success, validation errors, and confirmation page rendering (AGT-TEST-13)

### 33.3 Agent Edit and Detail Pages

- [ ] Add `EditAgent` (GET) action to `AdminController` that loads the agent user and returns the edit form
- [ ] Add `EditAgent` (POST) action to `AdminController`:
  - [ ] Accept display name and description
  - [ ] Call `IAdminService.UpdateAgentUser(userId, displayName, description)` (AGT-21)
  - [ ] On success, redirect to the agent detail page
- [ ] Add `AgentDetail` action to `AdminController` that loads the agent user, their PATs, project memberships, and recent activity (AGT-26)
- [ ] Create `Views/Admin/EditAgent.cshtml`:
  - [ ] Form with Display Name, Description, and Active toggle (AGT-UI-06)
  - [ ] "Save" submit button
- [ ] Create `Views/Admin/AgentDetail.cshtml`:
  - [ ] Agent profile information (display name, description, active status, last active) (AGT-UI-07)
  - [ ] PAT list with name, prefix, creation date, last used, and "Revoke" button per token (AGT-24, AGT-25)
  - [ ] "Generate New Token" button that creates a PAT and shows the raw value once (AGT-24)
  - [ ] Project memberships list with "Add to Project" and "Remove" actions (AGT-27)
  - [ ] Recent activity log entries (AGT-26)
- [ ] Write tests verifying edit form submission, detail page rendering, PAT management, and project membership actions

### 33.4 Agent Pause/Resume and Deletion

- [ ] Add `ToggleAgentActive` (POST) action to `AdminController`:
  - [ ] Call `IAdminService.SetAgentActive(userId, !currentActiveStatus)` (AGT-22)
  - [ ] Redirect back to the agent list or detail page
- [ ] Add `DeleteAgent` (POST) action to `AdminController`:
  - [ ] Require confirmation (via a confirmation modal or page)
  - [ ] Call `IAdminService.DeleteAgentUser(userId, adminUserId)` (AGT-28)
  - [ ] Redirect to the agent list page
- [ ] Write tests verifying pause/resume toggle and deletion with PAT revocation (AGT-TEST-14)

---

## Phase 34: Agent MCP Tools and Bot Badge

Add the `get_my_profile` MCP tool, modify `my_assignments` for agent context, and add bot badge rendering across the UI. This phase makes agent users visible throughout the application and provides the MCP tool agents need to read their own identity.

### 34.1 ProfileTools MCP Tool

- [ ] Create `TeamWare.Web/Mcp/Tools/ProfileTools.cs` with `[McpServerToolType]` and `[Authorize]` (Spec Section 6.1)
- [ ] Implement `get_my_profile` tool:
  - [ ] Accept no parameters (AGT-41)
  - [ ] Resolve authenticated user ID from `ClaimsPrincipal`
  - [ ] Load `ApplicationUser` via `UserManager<ApplicationUser>.FindByIdAsync`
  - [ ] Return JSON object: `{ userId, displayName, email, isAgent, agentDescription, isAgentActive, lastActiveAt }` (AGT-42)
  - [ ] For human users: `agentDescription` and `isAgentActive` are `null` (AGT-44)
  - [ ] Dates use ISO 8601 format, consistent with all other MCP tools
- [ ] Write unit tests verifying correct response for agent user and human user (AGT-TEST-10)

### 34.2 my_assignments Agent Filtering

- [ ] Modify `my_assignments` in `TaskTools.cs` to detect agent context (AGT-33):
  - [ ] Check for the `IsAgent` claim in the `ClaimsPrincipal` (added by `PatAuthenticationHandler` in Phase 32)
  - [ ] If `IsAgent` claim is present with value `"true"`, filter results to only include tasks with status `ToDo` or `InProgress`
  - [ ] If `IsAgent` claim is not present (human user), return all statuses as before (no behavior change)
- [ ] Write unit tests verifying:
  - [ ] Agent user receives only `ToDo` and `InProgress` tasks (AGT-TEST-11)
  - [ ] Human user receives all task statuses (AGT-TEST-12)

### 34.3 Bot Badge Partial View

- [ ] Create `Views/Shared/_BotBadge.cshtml` partial view (AGT-55):
  - [ ] Accept a boolean model parameter (or `IsAgent` flag)
  - [ ] When `true`, render a small inline "BOT" label styled with Tailwind CSS (AGT-UI-10, AGT-UI-11)
  - [ ] Use a subtle background color with contrasting text (e.g., `bg-indigo-100 text-indigo-700 dark:bg-indigo-900 dark:text-indigo-300`) (AGT-UI-12)
  - [ ] When `false`, render nothing
  - [ ] No emoticons or emojis (AGT-56)

### 34.4 Bot Badge Integration Across Views

- [ ] Add bot badge to comment display in task detail view — next to the comment author's display name (AGT-50)
- [ ] Add bot badge to activity log entries — next to the actor's display name (AGT-51)
- [ ] Add bot badge to task assignee lists in task detail view (AGT-52)
- [ ] Add bot badge to project member lists in project views (AGT-53)
- [ ] Add bot badge to user directory entries (AGT-54)
- [ ] Add bot badge to task assignment dropdowns/selectors — next to agent user names (AGT-31)
- [ ] Add optional filter to user directory: "All Users", "Human Users Only", "Agent Users Only" (AGT-54)
- [ ] Write tests verifying bot badge renders for agent users and does not render for human users across all contexts (AGT-TEST-15)

---

## Phase 35: Agent Polish and Hardening

Final review, edge case handling, security hardening, and documentation. Ensure agent users work correctly across all TeamWare features and do not introduce regressions.

### 35.1 Security Hardening

- [ ] Verify that agent users cannot be assigned the site-wide `Admin` role through any path (AGT-SEC-01):
  - [ ] `CreateAgentUser` does not assign Admin
  - [ ] `PromoteToAdmin` in `AdminService` rejects agent users
  - [ ] Write tests for both paths
- [ ] Verify that agent users are rejected at the login form (AGT-SEC-02):
  - [ ] Modify the `AccountController.Login` POST action to check `IsAgent` after credential validation
  - [ ] If `IsAgent = true`, return generic "invalid credentials" error (do not reveal agent status)
  - [ ] Write tests verifying login rejection
- [ ] Verify that the `IsAgent` flag cannot be changed after creation (AGT-SEC-03):
  - [ ] Ensure no admin endpoint allows modifying `IsAgent`
  - [ ] The `UpdateAgentUser` method only modifies `DisplayName` and `AgentDescription`
  - [ ] Write tests verifying `IsAgent` is immutable
- [ ] Verify that pausing an agent immediately blocks all MCP requests (AGT-SEC-05):
  - [ ] Write integration test: create agent, authenticate successfully, pause agent, verify next request fails
- [ ] Verify that agent users respect project membership for all MCP tools (AGT-SEC-06):
  - [ ] Write integration tests for an agent user attempting to access a project they are not a member of

### 35.2 Edge Cases and Regression Testing

- [ ] Verify agent users work correctly with all existing MCP tools:
  - [ ] `list_projects` — returns only projects the agent is a member of
  - [ ] `get_task` — works for tasks in projects the agent is a member of
  - [ ] `update_task_status` — agent can move tasks through workflow
  - [ ] `add_comment` — agent can post comments
  - [ ] `create_task` — agent can create tasks in projects they are a member of
  - [ ] `capture_inbox` — agent can capture inbox items
  - [ ] Lounge tools — agent can read and post lounge messages in their projects
- [ ] Verify agent user deletion preserves historical data:
  - [ ] Comments created by a deleted agent user still display (with "Deleted User" or similar)
  - [ ] Activity log entries created by a deleted agent user still display
  - [ ] Task assignments referencing a deleted agent user are handled gracefully
- [ ] Verify the `get_my_profile` tool works when called via cookie authentication (human user in browser) and PAT authentication (agent user via MCP)
- [ ] Verify that the agent list page handles the case of zero agent users gracefully (empty state message)

### 35.3 UI Consistency Review

- [ ] Verify bot badge renders consistently across all views in both light and dark themes
- [ ] Verify the agent management pages follow existing admin panel styling
- [ ] Verify the agent creation confirmation page token display matches the existing PAT management page styling
- [ ] Verify all agent-related pages have no emoticons or emojis
- [ ] Verify the user directory filter works correctly and preserves other active filters

### 35.4 Documentation

- [ ] Update the [copilot-instructions.md](../../.github/copilot-instructions.md) with:
  - [ ] Phase 32-35 branch names in the Branch Strategy table
  - [ ] Phase 32-35 GitHub issue mappings in the GitHub Issue Map section
- [ ] Review and finalize the [Agent Users Idea document](AgentUsersIdea.md) — mark all next steps as complete
- [ ] Review and finalize the [Agent Users Specification](AgentUsersSpecification.md) — verify all requirements are implemented
- [ ] Add agent user documentation to any existing user-facing help or README content
