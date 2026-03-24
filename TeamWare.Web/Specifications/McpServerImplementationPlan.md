# TeamWare - MCP Server Integration Implementation Plan

This document defines the phased implementation plan for the TeamWare MCP Server Integration based on the [MCP Server Specification](McpServerSpecification.md). Each phase builds on the previous one and is broken into work items suitable for GitHub Issues. Check off items as they are completed to track progress.

---

## Progress Summary

| Phase | Description | Status |
|-------|------------|--------|
| 26 | MCP Foundation and PAT Authentication | Complete |
| 27 | Read-Only MCP Tools | Complete |
| 28 | Write MCP Tools | Complete |
| 29 | MCP Prompts and Resources | Not Started |
| 30 | Lounge MCP Tools | Not Started |
| 31 | MCP Polish and Hardening | Not Started |

---

## Current State

All original phases (0-9), social feature phases (10-14), Project Lounge phases (15-21), and Ollama AI Integration phases (22-25) are complete. The workspace is an ASP.NET Core MVC project (.NET 10) with:

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
- Security hardening, performance optimization, and UI polish

The MCP Server Integration builds on top of this foundation. It uses the existing `GlobalConfiguration` infrastructure for settings, the existing `ServiceResult<T>` pattern for service layer results, and the existing service interfaces (`IProjectService`, `ITaskService`, `IInboxService`, etc.) for all business logic.

---

## Guiding Principles

All guiding principles from previous implementation plans continue to apply:

1. **Vertical slices** — Each phase delivers end-to-end working functionality (service, controller/tools, tests).
2. **Tests accompany every feature** — No phase is complete without its test cases.
3. **One type per file** — Enforced throughout (MAINT-01).
4. **MVC only** — Controllers and Views, no Razor Pages (project guideline).

Additionally:

5. **Reuse, do not duplicate** — All MCP tools delegate to existing service interfaces. No business logic is duplicated in the MCP layer.
6. **Same user, different interface** — An MCP client operates as the authenticated user. The same authorization rules that govern the web UI apply to MCP tool invocations.
7. **Optional and non-invasive** — The MCP endpoint is disabled by default. TeamWare is fully functional without it. No existing behavior changes.
8. **Fail-safe** — MCP errors never affect the web application. A misbehaving MCP client cannot degrade the experience for browser users.

---

## Phase 26: MCP Foundation and PAT Authentication

Establish the Personal Access Token authentication system, MCP NuGet packages, endpoint configuration, and `GlobalConfiguration` seeding. This phase delivers no MCP tools but provides the infrastructure for all subsequent phases.

### 26.1 PersonalAccessToken Entity and Migration

- [x] Create `PersonalAccessToken` model class with properties: `Id`, `Name`, `TokenHash`, `TokenPrefix`, `UserId`, `CreatedAt`, `ExpiresAt`, `LastUsedAt`, `RevokedAt` (Spec Section 4.1)
- [x] Add navigation property `User` (`ApplicationUser`) to `PersonalAccessToken`
- [x] Add `PersonalAccessTokens` collection navigation property to `ApplicationUser` (Spec Section 4.2)
- [x] Add `PersonalAccessToken` `DbSet` to `ApplicationDbContext`
- [x] Configure entity in `ApplicationDbContext.OnModelCreating`:
  - [x] Unique index on `TokenHash` for fast authentication lookup (PAT-12, MCP-NF-02)
  - [x] Index on `UserId` for listing a user's tokens
  - [x] Required `Name` with max length 100
  - [x] Required `TokenHash` with max length 128
  - [x] Required `TokenPrefix` with max length 10
  - [x] Foreign key relationship to `ApplicationUser`
- [x] Create EF Core migration
- [x] Write tests verifying entity configuration, indexes, and relationships

### 26.2 PersonalAccessTokenService

- [x] Create `IPersonalAccessTokenService` interface with methods: `CreateToken`, `ValidateToken`, `GetTokensForUser`, `RevokeToken`, `RevokeAllTokensForUser` (Spec Section 5.1)
- [x] Create `PersonalAccessTokenService` implementation:
  - [x] `CreateToken`:
    - [x] Validate user has fewer than 10 active (non-expired, non-revoked) tokens (PAT-15)
    - [x] Generate 32 cryptographically random bytes using `RandomNumberGenerator.GetBytes` (PAT-03, MCP-NF-06)
    - [x] Encode as URL-safe Base64 and prefix with `tw_` (PAT-03)
    - [x] Compute SHA-256 hash of the raw token string (PAT-05, MCP-NF-06)
    - [x] Store the hash (`TokenHash`), first 6 characters (`TokenPrefix`), name, user ID, expiration date (PAT-04, PAT-05)
    - [x] Return the raw token value in `ServiceResult<string>.Success(rawToken)` (PAT-04)
  - [x] `ValidateToken`:
    - [x] Compute SHA-256 hash of the presented raw token
    - [x] Look up the hash in the database via the unique index (PAT-12, MCP-NF-02)
    - [x] Verify `RevokedAt` is null (PAT-09)
    - [x] Verify `ExpiresAt` is null or in the future (PAT-09)
    - [x] Update `LastUsedAt` to `DateTime.UtcNow` (PAT-10)
    - [x] Return the owning `ApplicationUser` in `ServiceResult<ApplicationUser>.Success(user)`
    - [x] Return failure for: no matching hash, expired, revoked
  - [x] `GetTokensForUser`: Return all non-revoked tokens for the user, ordered by `CreatedAt` descending (PAT-06)
  - [x] `RevokeToken`: Set `RevokedAt` to `DateTime.UtcNow`. Verify user owns the token or is a site admin (PAT-07, PAT-08)
  - [x] `RevokeAllTokensForUser`: Set `RevokedAt` on all active tokens for the user (PAT-08)
- [x] Register `IPersonalAccessTokenService` / `PersonalAccessTokenService` in DI
- [x] Write unit tests for: creation success, 10-token limit, validation success, invalid token, expired token, revoked token, revoke own token, revoke as admin, revoke all (MCP-TEST-01)

### 26.3 PAT Authentication Handler

- [x] Create `PatAuthenticationHandler` extending `AuthenticationHandler<AuthenticationSchemeOptions>` (Spec Section 5.2)
  - [x] In `HandleAuthenticateAsync`:
    - [x] Extract `Authorization: Bearer <token>` header
    - [x] If no Bearer token present, return `AuthenticateResult.NoResult()` to allow cookie auth to proceed (MCP-NF-07, PAT-14)
    - [x] Call `IPersonalAccessTokenService.ValidateToken(rawToken)`
    - [x] On success, build `ClaimsPrincipal` with the user's claims (user ID, name, email, roles) matching the claims produced by cookie authentication (PAT-13)
    - [x] Return `AuthenticateResult.Success(ticket)`
    - [x] On failure (invalid/expired/revoked), return `AuthenticateResult.Fail(errorMessage)` (PAT-11, MCP-71)
- [x] Register the authentication scheme as `"PersonalAccessToken"` alongside existing Identity cookie authentication (PAT-14, Spec Section 6.3)
- [x] Write unit tests for: successful auth, no bearer header, invalid token, expired token, revoked token (MCP-TEST-02)

### 26.4 GlobalConfiguration Seeding and MCP Endpoint

- [x] Seed `MCP_ENABLED` key with value `false` and description: "Enable the MCP (Model Context Protocol) server endpoint at /mcp. Set to true to allow AI agents and MCP clients to connect." (MCP-01, MCP-02, MCP-03)
- [x] Add `ModelContextProtocol` and `ModelContextProtocol.AspNetCore` NuGet packages to `TeamWare.Web.csproj` (Spec Section 2)
- [x] Register MCP server services in `Program.cs` (Spec Section 6.1):
  ```csharp
  builder.Services.AddMcpServer()
      .WithHttpTransport()
      .WithToolsFromAssembly();
  ```
- [x] Map the MCP endpoint at `/mcp` (Spec Section 6.2):
  ```csharp
  app.MapMcp("/mcp");
  ```
- [x] Create MCP endpoint middleware that checks `MCP_ENABLED` from cached `IGlobalConfigurationService` and returns HTTP 404 when disabled (MCP-02, MCP-70, MCP-NF-03)
- [x] Write tests verifying `MCP_ENABLED` seed key is created on first run with value `false` (MCP-TEST-09)
- [x] Write integration tests verifying the MCP endpoint returns 404 when `MCP_ENABLED` is `false` (MCP-TEST-08)
- [x] Write integration tests verifying end-to-end PAT authentication with the MCP endpoint (MCP-TEST-07)

### 26.5 PAT Management UI

- [x] Add "Personal Access Tokens" section to the user profile page (MCP-UI-01)
- [x] Display token list with: name, `TokenPrefix` (masked, e.g., `tw_abc...`), creation date, expiration date (or "Never"), last-used date (or "Never used") (MCP-UI-02, PAT-06)
- [x] Add "Generate New Token" form with fields: token name (required), expiration date (optional) (MCP-UI-03, PAT-01, PAT-02)
- [x] After creation, display raw token in a read-only field with "Copy to Clipboard" button and warning that it will not be shown again (MCP-UI-04, PAT-04)
- [x] Add "Revoke" button per token with confirmation dialog (MCP-UI-05, PAT-07)
- [x] Style with Tailwind CSS, supporting light/dark theme (MCP-UI-06)
- [x] No emoticons or emojis (MCP-UI-07)
- [x] Write UI rendering tests for token list and creation flow (MCP-TEST-10)

### 26.6 Admin PAT Management

- [x] Display active PAT count per user on the admin user management page (MCP-UI-11)
- [x] Add ability for admins to view and revoke any user's PATs from the admin user detail page (MCP-UI-12, PAT-08)
- [x] Display the `MCP_ENABLED` key with value and description on the admin configuration page (MCP-UI-10)

---

## Phase 27: Read-Only MCP Tools

Deliver the first set of MCP tools that expose TeamWare data to external AI agents. All tools are read-only and delegate to existing services.

### 27.1 ProjectTools

- [x] Create `TeamWare.Web/Mcp/Tools/ProjectTools.cs` with `[McpServerToolType]` and `[Authorize]` (Spec Section 8)
- [x] Implement `list_projects` tool:
  - [x] Accept no parameters (MCP-10)
  - [x] Resolve authenticated user ID from `ClaimsPrincipal`
  - [x] Call `IProjectService.GetProjectsForUser(userId)`
  - [x] Return JSON array: `{ id, name, description, status, memberCount }` (MCP-10, MCP-NF-04)
- [x] Implement `get_project` tool:
  - [x] Accept `projectId: int` parameter (MCP-11)
  - [x] Verify project membership (MCP-19)
  - [x] Call `IProjectService.GetProjectDashboard(projectId, userId)`
  - [x] Call `IProgressService.GetProjectStatistics(projectId)`, `GetOverdueTasks(projectId)`, `GetUpcomingDeadlines(projectId)`
  - [x] Return JSON object with project details and task statistics (MCP-11, MCP-NF-04)
- [x] Write unit tests for both tools verifying service delegation, authorization, JSON shape (MCP-TEST-03, MCP-TEST-04)

### 27.2 TaskTools (Read)

- [x] Create `TeamWare.Web/Mcp/Tools/TaskTools.cs` with `[McpServerToolType]` and `[Authorize]` (Spec Section 8)
- [x] Implement `list_tasks` tool:
  - [x] Accept `projectId: int` and optional `status`, `priority`, `assigneeId` (MCP-12)
  - [x] Verify project membership (MCP-19)
  - [x] Parse optional string filters to `TaskItemStatus?` and `TaskItemPriority?` enums
  - [x] Call `ITaskService.GetTasksForProject(projectId, userId, statusFilter, priorityFilter, assigneeId)`
  - [x] Return JSON array: `{ id, title, status, priority, dueDate, assignees }` (MCP-12, MCP-NF-04)
- [x] Implement `get_task` tool:
  - [x] Accept `taskId: int` (MCP-13)
  - [x] Call `ITaskService.GetTask(taskId, userId)` — service handles membership verification (MCP-20)
  - [x] Call `ICommentService.GetCommentsForTask(taskId, userId)` for comments
  - [x] Return JSON object with full task detail including comments (MCP-13, MCP-NF-04)
- [x] Implement `my_assignments` tool:
  - [x] Accept no parameters (MCP-14)
  - [x] Call `ITaskService.GetWhatsNext(userId)`
  - [x] Return JSON array with project name, task ID, title, status, priority, due date (MCP-14, MCP-NF-04)
- [x] Write unit tests for all three tools (MCP-TEST-03, MCP-TEST-04)

### 27.3 InboxTools (Read)

- [x] Create `TeamWare.Web/Mcp/Tools/InboxTools.cs` with `[McpServerToolType]` and `[Authorize]` (Spec Section 8)
- [x] Implement `my_inbox` tool:
  - [x] Accept no parameters (MCP-15)
  - [x] Call `IInboxService.GetUnprocessedItems(userId)`
  - [x] Return JSON array: `{ id, title, description, createdAt }` (MCP-15, MCP-NF-04)
- [x] Write unit tests (MCP-TEST-03, MCP-TEST-04)

### 27.4 ActivityTools

- [x] Create `TeamWare.Web/Mcp/Tools/ActivityTools.cs` with `[McpServerToolType]` and `[Authorize]` (Spec Section 8)
- [x] Implement `get_activity` tool:
  - [x] Accept optional `projectId: int?` and `period: string` (today, this_week, this_month) (MCP-16)
  - [x] Parse `period` to a `DateTime since` value
  - [x] If `projectId` provided: verify membership (MCP-19), call `IActivityLogService.GetActivityForProject(projectId, since)` (MCP-16)
  - [x] If `projectId` omitted: call `IActivityLogService.GetActivityForUser(userId, since)` (MCP-16)
  - [x] Return JSON array: `{ timestamp, user, changeType, taskTitle, oldValue, newValue }` (MCP-16, MCP-NF-04)
- [x] Implement `get_project_summary` tool:
  - [x] Accept `projectId: int` and `period: string` (MCP-17)
  - [x] Verify project membership (MCP-19)
  - [x] Call `IProgressService.GetProjectStatistics(projectId)` for current task counts (MCP-17)
  - [x] Call `IActivityLogService.GetActivityForProject(projectId, since)` to count tasks completed and created in period (MCP-17)
  - [x] Return JSON object: `{ taskStats, completionPct, overdueCount, completedInPeriod, createdInPeriod }` (MCP-17, MCP-NF-04)
- [x] Write unit tests for both tools (MCP-TEST-03, MCP-TEST-04)

### 27.5 Cross-Cutting Read Tool Tests

- [x] Write integration tests verifying all read-only tools require PAT authentication (MCP-18)
- [x] Write integration tests verifying tools return authorization errors for non-member project access (MCP-19, MCP-20)
- [x] Write integration tests verifying tools return descriptive errors for non-existent entities (MCP-73)
- [x] Write integration tests verifying tools propagate `ServiceResult` failures (MCP-74)

---

## Phase 28: Write MCP Tools

Add tools that create and modify data in TeamWare.

### 28.1 TaskTools (Write)

- [x] Add `create_task` tool to `TaskTools`:
  - [x] Accept `projectId: int`, `title: string`, optional `description: string`, optional `priority: string` (default "Medium"), optional `dueDate: string` (MCP-30)
  - [x] Validate `title` max 300 characters, `description` max 4000 characters (MCP-SEC-06)
  - [x] Parse `priority` to `TaskItemPriority` enum, `dueDate` to `DateTime?` (MCP-NF-04)
  - [x] Verify project membership with a role that permits task creation (MCP-37)
  - [x] Call `ITaskService.CreateTask(projectId, title, description, priority, dueDate, userId)` (MCP-30)
  - [x] Return JSON object of created task (MCP-38)
- [x] Add `update_task_status` tool to `TaskTools`:
  - [x] Accept `taskId: int`, `status: string` (MCP-31)
  - [x] Parse `status` to `TaskItemStatus` enum
  - [x] Call `ITaskService.ChangeStatus(taskId, newStatus, userId)` — service handles membership verification (MCP-31)
  - [x] Return JSON object of updated task (MCP-38)
- [x] Add `assign_task` tool to `TaskTools`:
  - [x] Accept `taskId: int`, `userIds: string[]` (MCP-32)
  - [x] Call `ITaskService.AssignMembers(taskId, userIds, userId)` (MCP-32)
  - [x] Return success/failure message (MCP-38)
- [x] Add `add_comment` tool to `TaskTools`:
  - [x] Accept `taskId: int`, `content: string` (MCP-33)
  - [x] Validate `content` max 4000 characters (MCP-SEC-06)
  - [x] Call `ICommentService.AddComment(taskId, content, userId)` (MCP-33)
  - [x] Return JSON object of created comment (MCP-38)
- [x] Write unit tests for all write tools: successful creation/update, validation errors, authorization errors, service failures (MCP-TEST-03, MCP-TEST-04)

### 28.2 InboxTools (Write)

- [x] Add `capture_inbox` tool to `InboxTools`:
  - [x] Accept `title: string`, optional `description: string` (MCP-34)
  - [x] Validate `title` max 300 characters, `description` max 4000 characters (MCP-SEC-06)
  - [x] Call `IInboxService.AddItem(title, description, userId)` (MCP-34)
  - [x] Return JSON object of created inbox item (MCP-38)
- [x] Add `process_inbox_item` tool to `InboxTools`:
  - [x] Accept `inboxItemId: int`, `projectId: int`, `priority: string`, optional `isNextAction: bool`, optional `isSomedayMaybe: bool` (MCP-35)
  - [x] Parse `priority` to `TaskItemPriority` enum
  - [x] Call `IInboxService.ConvertToTask(inboxItemId, projectId, priority, null, isNextAction, isSomedayMaybe, userId)` (MCP-35)
  - [x] Return JSON object of created task (MCP-38)
- [x] Write unit tests for both write tools (MCP-TEST-03, MCP-TEST-04)

### 28.3 Cross-Cutting Write Tool Tests

- [x] Write integration tests verifying all write tools require PAT authentication (MCP-36)
- [x] Write integration tests verifying authorization enforcement matches web UI behavior (MCP-37)
- [x] Write integration tests verifying input validation rejects oversized strings (MCP-SEC-06)
- [x] Write integration tests verifying tools return descriptive error messages on failure (MCP-38, MCP-74)

---

## Phase 29: MCP Prompts and Resources

Deliver MCP Prompts and MCP Resources that provide structured context and dashboard data to AI agents.

### 29.1 Project Context Prompt

- [ ] Create `TeamWare.Web/Mcp/Prompts/ProjectContextPrompt.cs` (Spec Section 8)
- [ ] Implement `project_context` prompt:
  - [ ] Accept `projectId: int` (MCP-40)
  - [ ] Verify project membership (MCP-44)
  - [ ] Gather: project description, member list via `IProjectMemberService.GetMembers`, task counts via `IProgressService.GetProjectStatistics`, last 10 activity entries via `IActivityLogService.GetActivityForProject` (MCP-40)
  - [ ] Format as a system message with structured context suitable for AI conversation (MCP-40)
- [ ] Write unit tests verifying prompt content assembly and data inclusion (MCP-TEST-05)

### 29.2 Task Breakdown Prompt

- [ ] Create `TeamWare.Web/Mcp/Prompts/TaskBreakdownPrompt.cs` (Spec Section 8)
- [ ] Implement `task_breakdown` prompt:
  - [ ] Accept `projectId: int` and `taskDescription: string` (MCP-41)
  - [ ] Verify project membership (MCP-44)
  - [ ] Gather existing task list via `ITaskService.GetTasksForProject` (MCP-41)
  - [ ] Format as: user message with the task description, system message listing existing tasks and asking for 3-7 actionable subtask suggestions that avoid duplication (MCP-41)
- [ ] Write unit tests (MCP-TEST-05)

### 29.3 Standup Prompt

- [ ] Create `TeamWare.Web/Mcp/Prompts/StandupPrompt.cs` (Spec Section 8)
- [ ] Implement `standup` prompt:
  - [ ] Accept no parameters (MCP-42)
  - [ ] Gather user's activity from last 24 hours via `IActivityLogService.GetActivityForUser` (MCP-42)
  - [ ] Format as a user message in Yesterday/Today/Blockers standup template (MCP-42)
- [ ] Write unit tests (MCP-TEST-05)

### 29.4 Dashboard Resource

- [ ] Create `TeamWare.Web/Mcp/Resources/DashboardResource.cs` (Spec Section 8)
- [ ] Implement `teamware://dashboard` resource:
  - [ ] Gather assigned task count via `ITaskService.GetWhatsNext` (MCP-50)
  - [ ] Gather unread notification count via `INotificationService.GetUnreadCount` (MCP-50)
  - [ ] Gather unprocessed inbox count via `IInboxService.GetUnprocessedCount` (MCP-50)
  - [ ] Return JSON: `{ assignedTaskCount, unreadNotificationCount, unprocessedInboxCount, upcomingDeadlines }` (MCP-50, MCP-NF-04)
- [ ] Write unit tests verifying data aggregation (MCP-TEST-06)

### 29.5 Project Summary Resource

- [ ] Create `TeamWare.Web/Mcp/Resources/ProjectSummaryResource.cs` (Spec Section 8)
- [ ] Implement `teamware://projects/{projectId}/summary` resource:
  - [ ] Verify project membership (MCP-53)
  - [ ] Gather project data via `IProjectService.GetProjectDashboard` and `IProgressService.GetProjectStatistics` (MCP-51)
  - [ ] Return JSON: `{ name, status, memberCount, taskStats, completionPct }` (MCP-51, MCP-NF-04)
- [ ] Write unit tests (MCP-TEST-06)

### 29.6 Cross-Cutting Prompt and Resource Tests

- [ ] Write integration tests verifying all prompts require PAT authentication (MCP-43, MCP-52)
- [ ] Write integration tests verifying prompts and resources enforce project membership (MCP-44, MCP-53)

---

## Phase 30: Lounge MCP Tools

Expose the Project Lounge messaging system to MCP clients.

### 30.1 LoungeTools

- [ ] Create `TeamWare.Web/Mcp/Tools/LoungeTools.cs` with `[McpServerToolType]` and `[Authorize]` (Spec Section 8)
- [ ] Implement `list_lounge_messages` tool:
  - [ ] Accept optional `projectId: int?` (null for global lounge) and optional `count: int` (default 20, max 100) (MCP-60)
  - [ ] If `projectId` provided, verify project membership (MCP-63)
  - [ ] Call `ILoungeService.GetMessages(projectId, null, count)` (MCP-60)
  - [ ] Return JSON array: `{ id, authorName, content, createdAt }` (MCP-60, MCP-NF-04)
- [ ] Implement `post_lounge_message` tool:
  - [ ] Accept optional `projectId: int?` and `content: string` (MCP-61)
  - [ ] If `projectId` provided, verify project membership (MCP-63)
  - [ ] Call `ILoungeService.SendMessage(projectId, userId, content)` (MCP-61)
  - [ ] Ensure notification and mention processing triggers (MCP-64)
  - [ ] Return JSON object of created message (MCP-61)
- [ ] Implement `search_lounge_messages` tool:
  - [ ] Accept `query: string` and optional `projectId: int?` (MCP-62)
  - [ ] If `projectId` provided, verify project membership (MCP-63)
  - [ ] Retrieve messages and filter by query content match (MCP-62)
  - [ ] Return JSON array of matching messages (MCP-62, MCP-NF-04)
- [ ] Write unit tests for all three tools (MCP-TEST-03, MCP-TEST-04)

### 30.2 Cross-Cutting Lounge Tests

- [ ] Write integration tests verifying lounge tools require PAT authentication
- [ ] Write integration tests verifying project membership enforcement for project lounges (MCP-63)
- [ ] Write integration tests verifying `post_lounge_message` triggers notifications and mention processing (MCP-64)

---

## Phase 31: MCP Polish and Hardening

Final pass on cross-cutting concerns: error handling, security audit, documentation, and consistency.

### 31.1 Error Handling and Resilience

- [ ] Verify the MCP endpoint returns HTTP 404 when `MCP_ENABLED` is `false` (MCP-70)
- [ ] Verify all authentication error paths return proper MCP protocol errors (MCP-71)
- [ ] Verify all authorization error paths return descriptive messages (MCP-72)
- [ ] Verify all entity-not-found cases return descriptive error messages (MCP-73)
- [ ] Verify all `ServiceResult` failures are propagated correctly to MCP clients (MCP-74)
- [ ] Verify MCP tool exceptions are caught and returned as MCP error responses, not propagated to the ASP.NET Core middleware pipeline (MCP-75)
- [ ] Test behavior when `MCP_ENABLED` is changed from `false` to `true` (endpoint becomes available within 60 seconds) (MCP-04)
- [ ] Test behavior when `MCP_ENABLED` is changed from `true` to `false` (endpoint stops accepting within 60 seconds) (MCP-04)

### 31.2 Security Review

- [ ] Audit all MCP tools for authentication enforcement — every tool requires PAT (MCP-18, MCP-36, MCP-43, MCP-52)
- [ ] Audit all project-scoped tools for membership verification (MCP-19, MCP-20, MCP-37, MCP-44, MCP-53, MCP-63)
- [ ] Verify input validation on all string parameters: title max 300 chars, description/content max 4000 chars (MCP-SEC-06)
- [ ] Verify no sensitive data is exposed in tool responses (password hashes, token values, internal IDs of inaccessible entities) (MCP-SEC-05)
- [ ] Verify the `tw_` prefix is applied to all generated tokens (MCP-SEC-04)
- [ ] Verify token revocation takes effect immediately with no caching delay (MCP-SEC-03)
- [ ] Verify PAT hashing uses SHA-256 (MCP-SEC-01, MCP-NF-06)

### 31.3 JSON Response Consistency

- [ ] Verify all MCP tool responses use consistent JSON property naming (camelCase) (MCP-NF-04)
- [ ] Verify all dates are serialized in ISO 8601 format (MCP-NF-04)
- [ ] Verify all enums are serialized as their string names, not integer values (MCP-NF-04)
- [ ] Verify all tools handle null/empty optional parameters gracefully

### 31.4 UI/UX Consistency

- [ ] Verify PAT management UI styling is consistent with existing profile page sections (MCP-UI-06)
- [ ] Verify light/dark theme support on all PAT management UI elements (MCP-UI-06)
- [ ] Verify no emoticons or emojis in PAT management chrome or labels (MCP-UI-07)
- [ ] Verify token creation flow: form → success display with copy button → token list refresh (MCP-UI-03, MCP-UI-04)
- [ ] Verify revocation flow: confirm dialog → token removed from list (MCP-UI-05)
- [ ] Verify admin PAT management is accessible and functional (MCP-UI-11, MCP-UI-12)

### 31.5 Documentation

- [ ] Update `README.md` with MCP server setup instructions:
  - [ ] How to enable the MCP endpoint via `MCP_ENABLED` in the admin dashboard
  - [ ] How to generate a Personal Access Token
  - [ ] How to configure MCP clients (VS Code, Claude Desktop) with the TeamWare MCP endpoint URL and PAT
  - [ ] List of available tools with brief descriptions
- [ ] Update `copilot-instructions.md` with Phase 26-31 branch names and issue map
- [ ] Update the [MCP Server Idea document](McpServerIdea.md) to mark the implementation as complete

---

## Branch Strategy

| Phase | Branch Name |
|-------|------------|
| Phase 26 | `phase-26/mcp-foundation` |
| Phase 27 | `phase-27/mcp-read-tools` |
| Phase 28 | `phase-28/mcp-write-tools` |
| Phase 29 | `phase-29/mcp-prompts-resources` |
| Phase 30 | `phase-30/mcp-lounge-tools` |
| Phase 31 | `phase-31/mcp-polish-hardening` |

---

## References

- [McpServerSpecification.md](McpServerSpecification.md) — Formal specification for this feature
- [McpServerIdea.md](McpServerIdea.md) — Original idea document and design decisions
- [ImplementationPlan.md](ImplementationPlan.md) — Main TeamWare implementation plan (Phases 0-9)
- [SocialFeaturesImplementationPlan.md](SocialFeaturesImplementationPlan.md) — Social Features implementation plan (Phases 10-14)
- [ProjectLoungeImplementationPlan.md](ProjectLoungeImplementationPlan.md) — Project Lounge implementation plan (Phases 15-21)
- [OllamaIntegrationImplementationPlan.md](OllamaIntegrationImplementationPlan.md) — Ollama AI Integration implementation plan (Phases 22-25)
