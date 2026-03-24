# TeamWare - MCP Server Integration Specification

## 1. Introduction

### 1.1 Purpose

This document provides the formal specification for the MCP Server Integration feature being added to TeamWare. It defines the functional requirements, authentication model, data model, tool/prompt/resource definitions, and configuration needed to expose a Model Context Protocol server from TeamWare over HTTP. This specification is a companion to the [main TeamWare specification](Specification.md) and follows the same conventions.

### 1.2 Scope

The MCP Server Integration exposes TeamWare's project management, task management, inbox, activity, and lounge capabilities to external AI agents and MCP-compatible clients through a standardized protocol. The feature is divided into five phases:

- **Phase A** — NuGet packages, HTTP endpoint setup, Personal Access Token (PAT) authentication
- **Phase B** — Read-only tools (list projects, list tasks, my assignments, activity, summaries)
- **Phase C** — Write tools (create task, update status, assign, comment, capture to inbox)
- **Phase D** — MCP Prompts (project context, task breakdown, standup) and MCP Resources (dashboard data)
- **Phase E** — Lounge integration (read messages, post messages, search)

The MCP server is entirely optional. It is disabled by default and has no effect on existing functionality when no MCP clients are connected.

### 1.3 Definitions and Acronyms

| Term | Definition |
|------|-----------|
| MCP | Model Context Protocol, an open standard for AI agent-to-server communication that defines tools, resources, and prompts. |
| MCP Tool | A callable function exposed by the server. Accepts parameters and returns results. Used for both read and write operations that require input. |
| MCP Resource | A read-only data endpoint exposed by the server. Identified by a URI. Used for structured data that clients can read without parameters. |
| MCP Prompt | A reusable prompt template exposed by the server. Returns prompt messages for the client to use in AI conversations. |
| PAT | Personal Access Token, a user-generated credential for authenticating API and MCP clients without interactive login. |
| Streamable HTTP | The modern MCP transport protocol over HTTP. Clients connect to the server endpoint directly. |
| SSE | Server-Sent Events, the legacy MCP transport protocol. Supported automatically by the SDK for backward compatibility. |
| GlobalConfiguration | The existing key-value configuration table in TeamWare, managed via the admin dashboard. |
| ServiceResult | The standard result wrapper used by TeamWare services, containing success/failure status, data, and error messages. |

### 1.4 Design Principles

- **Optional and non-invasive** — The MCP endpoint is disabled by default. TeamWare is fully functional without it. No existing behavior changes.
- **Reuse, do not duplicate** — All MCP tools delegate to existing service interfaces. No business logic is duplicated in the MCP layer.
- **Same user, different interface** — An MCP client operates as the authenticated user. The same authorization rules that govern the web UI apply to MCP tool invocations.
- **Fail-safe** — MCP errors never affect the web application. A misbehaving MCP client cannot degrade the experience for browser users.
- **Consistent with existing patterns** — Configuration uses `GlobalConfiguration`. New entities follow existing conventions. Services use `ServiceResult<T>`.

---

## 2. Technology Additions

| Layer | Technology | Purpose |
|-------|-----------|---------|
| MCP Server | `ModelContextProtocol` NuGet package | Core MCP server abstractions, `[McpServerTool]`, `[McpServerToolType]` attributes |
| HTTP Transport | `ModelContextProtocol.AspNetCore` NuGet package | `AddMcpServer()`, `WithHttpTransport()`, `MapMcp()`, `AddAuthorizationFilters()` |
| Token Hashing | `System.Security.Cryptography` (built-in) | SHA-256 hashing for PAT storage |
| Token Generation | `System.Security.Cryptography.RandomNumberGenerator` (built-in) | Cryptographically secure token generation |

All other technology choices remain unchanged from the [main specification](Specification.md).

---

## 3. Functional Requirements

### 3.1 Configuration

| ID | Requirement |
|----|------------|
| MCP-01 | The system shall store MCP server configuration in the existing `GlobalConfiguration` table using the key `MCP_ENABLED` |
| MCP-02 | The `MCP_ENABLED` key shall hold a boolean string value (`true` or `false`). When the value is not `true` (case-insensitive), the MCP endpoint shall not accept connections |
| MCP-03 | The `MCP_ENABLED` key shall be seeded with the value `false` and an appropriate description on first run so it appears in the admin configuration list |
| MCP-04 | Changes to `MCP_ENABLED` via the admin dashboard shall take effect within 60 seconds (the configuration cache duration) without requiring an application restart |
| MCP-05 | The MCP endpoint shall be mapped to the route `/mcp`. Streamable HTTP clients connect to `/mcp`. Legacy SSE clients connect to `/mcp/sse` |

### 3.2 Personal Access Token Authentication

| ID | Requirement |
|----|------------|
| PAT-01 | Authenticated users shall be able to generate Personal Access Tokens from their profile settings page |
| PAT-02 | Each PAT shall have a required user-provided name (for identification) and an optional expiration date |
| PAT-03 | The system shall generate a cryptographically secure random token value of at least 32 bytes, encoded as a URL-safe Base64 string, prefixed with `tw_` for identification |
| PAT-04 | The raw token value shall be displayed to the user exactly once at creation time. It shall not be stored or retrievable after creation |
| PAT-05 | The system shall store only a SHA-256 hash of the token value in the database, linked to the creating user |
| PAT-06 | Users shall be able to view a list of their tokens showing name, creation date, expiration date, and last-used date |
| PAT-07 | Users shall be able to revoke (delete) any of their own tokens |
| PAT-08 | Site administrators shall be able to view and revoke tokens for any user through the admin dashboard |
| PAT-09 | Expired tokens shall be rejected during authentication. The system shall not automatically delete expired tokens |
| PAT-10 | The system shall update the `LastUsedAt` timestamp on a token each time it is successfully used for authentication |
| PAT-11 | MCP clients shall authenticate by sending the PAT as a Bearer token in the HTTP `Authorization` header: `Authorization: Bearer tw_<token>` |
| PAT-12 | A custom ASP.NET Core authentication handler shall validate incoming Bearer tokens by hashing the presented token and comparing it against stored hashes |
| PAT-13 | Upon successful PAT validation, the authentication handler shall establish a `ClaimsPrincipal` with the same claims as the token's owning user, making the request indistinguishable from a regular authenticated session for authorization purposes |
| PAT-14 | PAT authentication shall coexist with the existing cookie-based Identity authentication. Cookie authentication continues to serve the web UI; PAT authentication serves MCP and API clients |
| PAT-15 | Each user may have a maximum of 10 active (non-expired, non-revoked) tokens at any time |

### 3.3 Read-Only Tools (Phase B)

| ID | Requirement |
|----|------------|
| MCP-10 | The system shall expose an MCP tool `list_projects` that returns all projects the authenticated user is a member of, including project ID, name, description, status, and member count |
| MCP-11 | The system shall expose an MCP tool `get_project` that accepts a project ID and returns the project dashboard data: name, description, status, member count, task statistics (counts by status), overdue tasks, and upcoming deadlines |
| MCP-12 | The system shall expose an MCP tool `list_tasks` that accepts a project ID and optional filters (status, priority, assignee user ID) and returns matching tasks with ID, title, status, priority, assignee names, and due date |
| MCP-13 | The system shall expose an MCP tool `get_task` that accepts a task ID and returns the full task detail: title, description, status, priority, due date, assignees, creation date, and comments |
| MCP-14 | The system shall expose an MCP tool `my_assignments` that returns all tasks assigned to the authenticated user across all projects, with project name, task ID, title, status, priority, and due date |
| MCP-15 | The system shall expose an MCP tool `my_inbox` that returns all unprocessed inbox items for the authenticated user with ID, title, description, and creation date |
| MCP-16 | The system shall expose an MCP tool `get_activity` that accepts an optional project ID and a time period (today, this week, this month) and returns activity log entries. When project ID is omitted, returns the authenticated user's activity across all projects |
| MCP-17 | The system shall expose an MCP tool `get_project_summary` that accepts a project ID and a time period and returns structured project health data: task counts by status, completion percentage, overdue count, tasks completed in period, and tasks created in period |
| MCP-18 | All read-only tools shall require authentication via PAT. Unauthenticated requests shall receive an appropriate MCP error |
| MCP-19 | Tools that accept a project ID shall verify the authenticated user is a member of that project. Non-members shall receive an authorization error |
| MCP-20 | Tools that accept a task ID shall verify the authenticated user is a member of the task's project |

### 3.4 Write Tools (Phase C)

| ID | Requirement |
|----|------------|
| MCP-30 | The system shall expose an MCP tool `create_task` that accepts a project ID, title, optional description, optional priority (Low, Medium, High, Critical; default Medium), and optional due date, and creates a new task in the specified project as the authenticated user |
| MCP-31 | The system shall expose an MCP tool `update_task_status` that accepts a task ID and a new status (ToDo, InProgress, InReview, Done) and updates the task's status |
| MCP-32 | The system shall expose an MCP tool `assign_task` that accepts a task ID and one or more user IDs and assigns those users to the task |
| MCP-33 | The system shall expose an MCP tool `add_comment` that accepts a task ID and comment content and posts a comment as the authenticated user |
| MCP-34 | The system shall expose an MCP tool `capture_inbox` that accepts a title and optional description and creates a new inbox item for the authenticated user |
| MCP-35 | The system shall expose an MCP tool `process_inbox_item` that accepts an inbox item ID, a project ID, a priority, and optional flags (isNextAction, isSomedayMaybe) and converts the inbox item to a task |
| MCP-36 | All write tools shall require authentication via PAT |
| MCP-37 | Write tools that operate on a project shall verify the authenticated user has a role that permits the operation: Owner, Admin, or Member. The specific role requirements shall match those enforced by the web UI |
| MCP-38 | Write tools shall return the created or updated entity data on success, or a descriptive error message on failure |

### 3.5 MCP Prompts (Phase D)

| ID | Requirement |
|----|------------|
| MCP-40 | The system shall expose an MCP prompt `project_context` that accepts a project ID and returns a prompt message containing the project description, member list, task statistics, and recent activity, formatted as context for an AI conversation |
| MCP-41 | The system shall expose an MCP prompt `task_breakdown` that accepts a project ID and a high-level task description and returns a prompt message asking the AI to suggest subtasks, including the existing task list to avoid duplication |
| MCP-42 | The system shall expose an MCP prompt `standup` that returns a prompt message containing the authenticated user's activity from the last 24 hours, formatted as a standup report template (Yesterday/Today/Blockers) |
| MCP-43 | All prompts shall require authentication via PAT |
| MCP-44 | Prompts that accept a project ID shall verify project membership |

### 3.6 MCP Resources (Phase D)

| ID | Requirement |
|----|------------|
| MCP-50 | The system shall expose an MCP resource `teamware://dashboard` that returns the authenticated user's personal dashboard data: assigned task count, unread notification count, unprocessed inbox count, and upcoming deadlines |
| MCP-51 | The system shall expose an MCP resource `teamware://projects/{projectId}/summary` that returns a structured project summary including task counts by status, completion percentage, and member count |
| MCP-52 | All resources shall require authentication via PAT |
| MCP-53 | Resources that reference a project shall verify project membership |

### 3.7 Lounge Tools (Phase E)

| ID | Requirement |
|----|------------|
| MCP-60 | The system shall expose an MCP tool `list_lounge_messages` that accepts an optional project ID (null for the global lounge) and an optional count (default 20, max 100) and returns recent messages with ID, author name, content, and timestamp |
| MCP-61 | The system shall expose an MCP tool `post_lounge_message` that accepts an optional project ID and message content and sends a message as the authenticated user |
| MCP-62 | The system shall expose an MCP tool `search_lounge_messages` that accepts a search query and optional project ID and returns matching messages |
| MCP-63 | Lounge tools that target a project lounge shall verify the authenticated user is a member of that project |
| MCP-64 | The `post_lounge_message` tool shall trigger the same notification and mention processing as messages posted through the web UI |

### 3.8 Error Handling

| ID | Requirement |
|----|------------|
| MCP-70 | If the MCP endpoint is disabled (`MCP_ENABLED` is not `true`), HTTP requests to `/mcp` and `/mcp/sse` shall return HTTP 404 |
| MCP-71 | If a PAT is missing, invalid, expired, or revoked, the MCP server shall return an authentication error per the MCP protocol |
| MCP-72 | If the authenticated user lacks permission for a requested operation, the MCP server shall return an authorization error with a descriptive message |
| MCP-73 | If a referenced entity (project, task, inbox item) does not exist, the tool shall return a descriptive error message |
| MCP-74 | If a service call returns a failure `ServiceResult`, the tool shall propagate the error messages to the MCP client |
| MCP-75 | MCP tool errors shall never affect the web application. An exception in an MCP tool shall be caught and returned as an MCP error response, not propagated to the ASP.NET Core middleware pipeline |

---

## 4. Data Model

### 4.1 New Entities

#### PersonalAccessToken

A new entity representing a user-generated authentication token for MCP and API access.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `Id` | `int` | PK, auto-increment | Primary key |
| `Name` | `string` | Required, max 100 chars | User-provided name for identification (e.g., "VS Code", "Claude Desktop") |
| `TokenHash` | `string` | Required, max 128 chars, unique index | SHA-256 hash of the raw token value |
| `TokenPrefix` | `string` | Required, max 10 chars | First 6 characters of the raw token (e.g., `tw_abc`) for display in token lists without revealing the full token |
| `UserId` | `string` | Required, FK to `ApplicationUser` | The user who created and owns this token |
| `CreatedAt` | `DateTime` | Required | UTC timestamp of token creation |
| `ExpiresAt` | `DateTime?` | Nullable | UTC expiration timestamp. Null means the token does not expire |
| `LastUsedAt` | `DateTime?` | Nullable | UTC timestamp of last successful authentication using this token |
| `RevokedAt` | `DateTime?` | Nullable | UTC timestamp when the token was revoked. Non-null means the token is revoked |

**Indexes:**
- Unique index on `TokenHash` for fast lookup during authentication
- Index on `UserId` for listing a user's tokens

**Relationships:**
- `PersonalAccessToken.UserId` → `ApplicationUser.Id` (many-to-one)
- Navigation property `User` on `PersonalAccessToken`
- Collection navigation property `PersonalAccessTokens` on `ApplicationUser`

### 4.2 Modified Entities

#### ApplicationUser

Add a navigation collection for tokens:

| Property | Type | Description |
|----------|------|-------------|
| `PersonalAccessTokens` | `ICollection<PersonalAccessToken>` | Collection of PATs owned by this user |

#### GlobalConfiguration (Seeded Keys)

One new key is seeded into the `GlobalConfiguration` table on first run:

| Key | Default Value | Description |
|-----|---------------|-------------|
| `MCP_ENABLED` | `false` | Whether the MCP server endpoint is active. Set to `true` to enable. |

---

## 5. Service Layer Design

### 5.1 IPersonalAccessTokenService

Manages the lifecycle of Personal Access Tokens.

| Method | Parameters | Returns | Description |
|--------|-----------|---------|-------------|
| `CreateToken` | `string userId, string name, DateTime? expiresAt` | `ServiceResult<string>` | Creates a new PAT. Returns the raw token value (shown once). Stores only the hash. Returns failure if the user has reached the 10-token limit. |
| `ValidateToken` | `string rawToken` | `ServiceResult<ApplicationUser>` | Hashes the provided token, looks up the hash, verifies not expired/revoked, updates `LastUsedAt`, and returns the owning user. Returns failure if the token is invalid, expired, or revoked. |
| `GetTokensForUser` | `string userId` | `ServiceResult<List<PersonalAccessToken>>` | Returns all non-revoked tokens for the specified user, ordered by creation date descending. |
| `RevokeToken` | `int tokenId, string userId` | `ServiceResult` | Revokes a token by setting `RevokedAt`. The `userId` must match the token owner unless the caller is a site admin. |
| `RevokeAllTokensForUser` | `string userId` | `ServiceResult` | Revokes all active tokens for a user. Used by admins or when a user's password is changed. |

**Implementation notes:**
- Token generation uses `RandomNumberGenerator.GetBytes(32)` to produce 32 cryptographically random bytes, encoded as URL-safe Base64, prefixed with `tw_`.
- Token hashing uses `SHA256.HashData` on the UTF-8 bytes of the raw token. The hex-encoded hash is stored in `TokenHash`.
- `ValidateToken` is called by the PAT authentication handler on every MCP request. It must be efficient: a single indexed database lookup by hash.

### 5.2 PatAuthenticationHandler

A custom ASP.NET Core `AuthenticationHandler<AuthenticationSchemeOptions>` that:

1. Extracts the Bearer token from the `Authorization` header.
2. Calls `IPersonalAccessTokenService.ValidateToken`.
3. On success, creates a `ClaimsPrincipal` with the user's identity and claims (user ID, name, email, roles), matching the claims produced by cookie authentication.
4. On failure, returns `AuthenticateResult.NoResult()` (allowing other handlers to try) or `AuthenticateResult.Fail(...)` if a Bearer token was present but invalid.

**Authentication scheme name:** `PersonalAccessToken`

**Registration:**
```csharp
builder.Services.AddAuthentication()
    .AddScheme<AuthenticationSchemeOptions, PatAuthenticationHandler>(
        "PersonalAccessToken", options => { });
```

The scheme is added alongside the existing Identity cookie scheme. ASP.NET Core's policy-based authorization evaluates both schemes. The `[Authorize]` attribute on MCP tools accepts either authentication method.

### 5.3 Existing Service Reuse

MCP tools delegate to existing services. No new business logic services are created for MCP. The following services are injected into MCP tool classes:

| Service | Used By |
|---------|---------|
| `IProjectService` | `ProjectTools` — `GetProjectsForUser`, `GetProjectDashboard` |
| `ITaskService` | `TaskTools` — `GetTasksForProject`, `GetTask`, `GetWhatsNext`, `CreateTask`, `ChangeStatus`, `AssignMembers` |
| `IInboxService` | `InboxTools` — `GetUnprocessedItems`, `AddItem`, `ConvertToTask` |
| `ICommentService` | `TaskTools` — `GetCommentsForTask`, `AddComment` |
| `IActivityLogService` | `ActivityTools` — `GetActivityForProject`, `GetActivityForUser` |
| `IProgressService` | `ProjectTools`, `ActivityTools` — `GetProjectStatistics`, `GetOverdueTasks`, `GetUpcomingDeadlines` |
| `IProjectMemberService` | Authorization checks — `GetMemberUserIds` |
| `INotificationService` | `ResourceTools` — `GetUnreadCount` |
| `ILoungeService` | `LoungeTools` — `GetMessages`, `SendMessage` |

---

## 6. MCP Endpoint Configuration

### 6.1 Service Registration

MCP services are registered unconditionally during application startup. The disabled state is handled at the HTTP level (the endpoint returns 404 when `MCP_ENABLED` is not `true`).

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .AddAuthorizationFilters()
    .WithToolsFromAssembly();
```

### 6.2 Endpoint Mapping

The MCP endpoint is mapped conditionally based on the `MCP_ENABLED` configuration value. A middleware component checks the cached configuration value and short-circuits with 404 when disabled.

```csharp
app.MapMcp("/mcp");
```

The `MapMcp` call registers both the Streamable HTTP endpoint at `/mcp` and the legacy SSE endpoints at `/mcp/sse` and `/mcp/message`.

### 6.3 Authentication Pipeline

The authentication pipeline is configured to support both cookie (web UI) and PAT (MCP/API) authentication:

```csharp
builder.Services.AddAuthentication(options =>
    {
        // Default scheme remains Identity cookies for the web UI
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
    })
    .AddScheme<AuthenticationSchemeOptions, PatAuthenticationHandler>(
        "PersonalAccessToken", options => { });
```

The MCP endpoint's `[Authorize]` attributes accept either scheme. When a Bearer token is present, the PAT handler authenticates. When a cookie is present, the Identity handler authenticates. Both produce equivalent `ClaimsPrincipal` instances.

---

## 7. Tool, Prompt, and Resource Specifications

### 7.1 Tool Specifications

#### ProjectTools

| Tool Name | Parameters | Returns | Service Delegation |
|-----------|-----------|---------|-------------------|
| `list_projects` | _(none)_ | JSON array of projects: `{ id, name, description, status, memberCount }` | `IProjectService.GetProjectsForUser` |
| `get_project` | `projectId: int` | JSON object: `{ id, name, description, status, memberCount, taskStats: { toDo, inProgress, inReview, done, total, completionPct }, overdueTasks: [...], upcomingDeadlines: [...] }` | `IProjectService.GetProjectDashboard`, `IProgressService` |

#### TaskTools

| Tool Name | Parameters | Returns | Service Delegation |
|-----------|-----------|---------|-------------------|
| `list_tasks` | `projectId: int, status?: string, priority?: string, assigneeId?: string` | JSON array of tasks: `{ id, title, status, priority, dueDate, assignees: [...] }` | `ITaskService.GetTasksForProject` |
| `get_task` | `taskId: int` | JSON object: `{ id, title, description, status, priority, dueDate, assignees, createdAt, comments: [...] }` | `ITaskService.GetTask`, `ICommentService.GetCommentsForTask` |
| `my_assignments` | _(none)_ | JSON array: `{ projectName, taskId, title, status, priority, dueDate }` | `ITaskService.GetWhatsNext` |
| `create_task` | `projectId: int, title: string, description?: string, priority?: string, dueDate?: string` | JSON object of created task | `ITaskService.CreateTask` |
| `update_task_status` | `taskId: int, status: string` | JSON object of updated task | `ITaskService.ChangeStatus` |
| `assign_task` | `taskId: int, userIds: string[]` | Success/failure message | `ITaskService.AssignMembers` |
| `add_comment` | `taskId: int, content: string` | JSON object of created comment | `ICommentService.AddComment` |

#### InboxTools

| Tool Name | Parameters | Returns | Service Delegation |
|-----------|-----------|---------|-------------------|
| `my_inbox` | _(none)_ | JSON array: `{ id, title, description, createdAt }` | `IInboxService.GetUnprocessedItems` |
| `capture_inbox` | `title: string, description?: string` | JSON object of created inbox item | `IInboxService.AddItem` |
| `process_inbox_item` | `inboxItemId: int, projectId: int, priority: string, isNextAction?: bool, isSomedayMaybe?: bool` | JSON object of created task | `IInboxService.ConvertToTask` |

#### ActivityTools

| Tool Name | Parameters | Returns | Service Delegation |
|-----------|-----------|---------|-------------------|
| `get_activity` | `projectId?: int, period: string` | JSON array of activity entries: `{ timestamp, user, changeType, taskTitle, oldValue, newValue }` | `IActivityLogService.GetActivityForProject` or `GetActivityForUser` |
| `get_project_summary` | `projectId: int, period: string` | JSON object: `{ taskStats, completionPct, overdueCount, completedInPeriod, createdInPeriod }` | `IProgressService.GetProjectStatistics`, `IActivityLogService` |

#### LoungeTools

| Tool Name | Parameters | Returns | Service Delegation |
|-----------|-----------|---------|-------------------|
| `list_lounge_messages` | `projectId?: int, count?: int` | JSON array: `{ id, authorName, content, createdAt }` | `ILoungeService.GetMessages` |
| `post_lounge_message` | `projectId?: int, content: string` | JSON object of created message | `ILoungeService.SendMessage` |
| `search_lounge_messages` | `query: string, projectId?: int` | JSON array of matching messages | `ILoungeService.GetMessages` (filtered) |

### 7.2 Prompt Specifications

| Prompt Name | Parameters | Output Description |
|-------------|-----------|-------------------|
| `project_context` | `projectId: int` | System message with project description, member list, task counts by status, and last 10 activity entries. Formatted as structured context suitable for an AI conversation. |
| `task_breakdown` | `projectId: int, taskDescription: string` | User message containing the task description, followed by a system message listing existing tasks in the project and asking the AI to suggest 3-7 actionable subtasks that do not duplicate existing work. |
| `standup` | _(none)_ | User message containing the authenticated user's activity from the last 24 hours (tasks completed, tasks updated, comments posted), formatted as a Yesterday/Today/Blockers template. |

### 7.3 Resource Specifications

| Resource URI | Returns | Service Delegation |
|-------------|---------|-------------------|
| `teamware://dashboard` | JSON object: `{ assignedTaskCount, unreadNotificationCount, unprocessedInboxCount, upcomingDeadlines: [...] }` | `ITaskService.GetWhatsNext`, `INotificationService.GetUnreadCount`, `IInboxService.GetUnprocessedCount` |
| `teamware://projects/{projectId}/summary` | JSON object: `{ name, status, memberCount, taskStats, completionPct }` | `IProjectService.GetProjectDashboard`, `IProgressService.GetProjectStatistics` |

---

## 8. Tool Organization

MCP tools, prompts, and resources are organized into classes by domain within the web project:

```
TeamWare.Web/
  Mcp/
    Tools/
      ProjectTools.cs
      TaskTools.cs
      InboxTools.cs
      ActivityTools.cs
      LoungeTools.cs
    Prompts/
      ProjectContextPrompt.cs
      TaskBreakdownPrompt.cs
      StandupPrompt.cs
    Resources/
      DashboardResource.cs
      ProjectSummaryResource.cs
  Authentication/
    PatAuthenticationHandler.cs
  Services/
    IPersonalAccessTokenService.cs
    PersonalAccessTokenService.cs
  Models/
    PersonalAccessToken.cs
```

Each tool class is decorated with `[McpServerToolType]` and `[Authorize]`. Individual tool methods are decorated with `[McpServerTool]` and `[Description]`. Services are resolved via DI method injection:

```csharp
[McpServerToolType]
[Authorize]
public class ProjectTools
{
    [McpServerTool, Description("Lists all projects the authenticated user is a member of.")]
    public static async Task<string> ListProjects(
        IProjectService projectService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var result = await projectService.GetProjectsForUser(userId!);
        // Serialize and return
    }
}
```

---

## 9. Changes to Existing Requirements

No existing functional requirements are modified. The MCP integration adds new capabilities without changing existing behavior.

The following existing non-functional requirements apply with additional considerations:

| Requirement | Consideration |
|-------------|--------------|
| SEC-05 (Authorization enforcement) | All MCP tools enforce authentication and resource-level authorization through existing service logic |
| AUTH-05 (Role-based access) | MCP clients are subject to the same Owner/Admin/Member role hierarchy as browser users |

The following existing entity receives a minor modification:

| Entity | Change |
|--------|--------|
| `ApplicationUser` | Add `PersonalAccessTokens` navigation collection |

---

## 10. Non-Functional Requirements

| ID | Requirement |
|----|------------|
| MCP-NF-01 | The MCP endpoint shall support both Streamable HTTP and legacy SSE transports via the SDK's `MapMcp` method |
| MCP-NF-02 | PAT validation shall require at most one indexed database query per request (hash lookup) |
| MCP-NF-03 | The `MCP_ENABLED` configuration value shall be cached in memory with a 60-second expiry, consistent with other `GlobalConfiguration` caching |
| MCP-NF-04 | MCP tools shall serialize return values as JSON. Dates shall use ISO 8601 format. Enums shall be serialized as their string names |
| MCP-NF-05 | The MCP endpoint shall not affect the performance or memory usage of the web application when no MCP clients are connected |
| MCP-NF-06 | Token hashing shall use SHA-256. Token generation shall use `RandomNumberGenerator` with a minimum of 32 bytes of entropy |
| MCP-NF-07 | The PAT authentication handler shall return `AuthenticateResult.NoResult()` when no Bearer token is present, allowing cookie authentication to proceed for web UI requests |

---

## 11. UI Requirements

### 11.1 Personal Access Token Management

| ID | Requirement |
|----|------------|
| MCP-UI-01 | The user profile page shall include a "Personal Access Tokens" section |
| MCP-UI-02 | The token list shall display each token's name, `TokenPrefix` (masked, e.g., `tw_abc...`), creation date, expiration date (or "Never"), and last-used date (or "Never used") |
| MCP-UI-03 | A "Generate New Token" form shall include fields for token name (required) and expiration date (optional) |
| MCP-UI-04 | After token creation, the raw token value shall be displayed in a read-only field with a "Copy to Clipboard" button and a warning that the token will not be shown again |
| MCP-UI-05 | Each token in the list shall have a "Revoke" button that requires confirmation before revoking |
| MCP-UI-06 | The token management UI shall follow existing TeamWare styling (Tailwind CSS 4, light/dark theme) |
| MCP-UI-07 | The token management UI shall not contain emoticons or emojis (consistent with UI-07) |

### 11.2 Admin Dashboard

| ID | Requirement |
|----|------------|
| MCP-UI-10 | The admin configuration page shall display the `MCP_ENABLED` key with its current value and description |
| MCP-UI-11 | The admin user management page shall show the count of active PATs per user |
| MCP-UI-12 | Admins shall be able to view and revoke any user's PATs from the user detail page |

---

## 12. Testing Requirements

| ID | Requirement |
|----|------------|
| MCP-TEST-01 | `PersonalAccessTokenService` shall have unit tests for token creation, validation, revocation, expiration, and the 10-token limit |
| MCP-TEST-02 | `PatAuthenticationHandler` shall have unit tests verifying successful authentication, missing token, invalid token, expired token, and revoked token scenarios |
| MCP-TEST-03 | Each MCP tool shall have unit tests verifying correct service delegation, parameter validation, and authorization enforcement |
| MCP-TEST-04 | Each MCP tool shall have tests verifying the correct JSON response shape |
| MCP-TEST-05 | Each MCP prompt shall have unit tests verifying prompt content assembly and proper inclusion of project/user data |
| MCP-TEST-06 | Each MCP resource shall have unit tests verifying correct data aggregation |
| MCP-TEST-07 | Integration tests shall verify end-to-end PAT authentication with the MCP endpoint |
| MCP-TEST-08 | Integration tests shall verify the MCP endpoint returns 404 when `MCP_ENABLED` is `false` |
| MCP-TEST-09 | Seed data tests shall verify that the `MCP_ENABLED` key is created on first run with a value of `false` |
| MCP-TEST-10 | UI tests shall verify the token management page renders correctly and the copy-to-clipboard flow works |

---

## 13. Security Considerations

| ID | Consideration |
|----|--------------|
| MCP-SEC-01 | Raw PAT values are never stored. Only SHA-256 hashes are persisted. A database breach does not directly expose usable tokens |
| MCP-SEC-02 | PATs should be transmitted only over HTTPS in production. The specification does not enforce this at the application level (it is an infrastructure concern), but documentation should recommend HTTPS |
| MCP-SEC-03 | Revoking a token takes effect immediately. There is no grace period or caching of valid tokens |
| MCP-SEC-04 | The `tw_` prefix enables secrets scanners and log scrubbers to identify TeamWare tokens in leaked credential dumps |
| MCP-SEC-05 | MCP tool implementations shall not return sensitive data (password hashes, other users' tokens, internal IDs of entities the user cannot access) |
| MCP-SEC-06 | Input validation shall be applied to all tool parameters. String inputs shall be bounded by the same `StringLength` constraints as the corresponding entity properties (e.g., task title max 300 chars, description max 4000 chars) |

---

## 14. Future Considerations

The following features are out of scope for this release but may be considered for future iterations:

- **Token scoping** — Allow PATs to be scoped to specific projects or read-only access, rather than inheriting the user's full permissions.
- **Webhook/event streaming** — Expose real-time TeamWare events (task created, comment posted) via MCP subscriptions or Server-Sent Events outside the MCP protocol.
- **Custom tool registration** — Allow administrators to define custom MCP tools through configuration, mapping to specific service operations.
- **MCP client directory** — Track which MCP clients have connected, their tool usage, and last connection time for observability.
- **Ollama passthrough** — Expose the local Ollama instance as an MCP tool for agents that lack their own LLM access (currently decided against, may revisit).

---

## 15. References

- [McpServerIdea.md](McpServerIdea.md) — Original idea document and design decisions
- [Specification.md](Specification.md) — Main TeamWare specification
- [OllamaIntegrationSpecification.md](OllamaIntegrationSpecification.md) — Ollama AI Integration specification (companion feature)
- [Model Context Protocol Specification](https://spec.modelcontextprotocol.io)
- [C# MCP SDK Documentation](https://csharp.sdk.modelcontextprotocol.io)
- [ModelContextProtocol NuGet Package](https://www.nuget.org/packages/ModelContextProtocol)
- [ModelContextProtocol.AspNetCore NuGet Package](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore)
