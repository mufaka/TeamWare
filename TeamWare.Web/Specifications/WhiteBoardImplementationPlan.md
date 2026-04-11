# TeamWare - Whiteboard Implementation Plan

This document defines the phased implementation plan for the Whiteboard feature, based on the [Whiteboard Specification](WhiteBoardSpecification.md). Each phase builds on the previous one and is broken into work items suitable for GitHub Issues.

---

## Progress Summary

| Phase | Description | Status |
|-------|------------|--------|
| 52 | Whiteboard Data Model | Complete |
| 53 | Whiteboard Service Layer | Complete |
| 54 | Whiteboard Landing Page | Complete |
| 55 | WhiteboardHub and Presence | Complete |
| 56 | Whiteboard Session Page and Canvas | Complete |
| 57 | Chat Sidebar | Complete |
| 58 | Invitations and Notifications | Complete |
| 59 | Presenter Control | Complete |
| 60 | Project Association and Saved Boards | Complete |
| 61 | Whiteboard Polish and Hardening | Complete |

---

## Current State

All previous phases (0–51) are complete. The workspace includes:

- Full project and task management with status changes, comments, and activity logging
- SignalR infrastructure with `LoungeHub` (real-time chat), `PresenceHub` (online/offline presence), and `TaskHub` (real-time task updates)
- Notification system with multiple notification types and in-app delivery
- Project membership and role-based authorization throughout
- ASP.NET Core MVC with HTMX, Alpine.js, and Tailwind CSS 4 (light/dark theme)
- Site admin capabilities for user and agent management

The Whiteboard feature adds a new top-level area for real-time, diagram-focused visual collaboration with a single-presenter model and invite-gated access.

---

## Guiding Principles

All guiding principles from previous implementation plans continue to apply:

1. **Vertical slices** — Each phase delivers end-to-end working functionality.
2. **Tests accompany every feature** — No phase is complete without its test cases.
3. **One type per file** — Enforced throughout (MAINT-01).
4. **MVC only** — Controllers and Views, no Razor Pages.

Additionally:

5. **Reuse proven patterns** — `WhiteboardHub` mirrors `LoungeHub` and `TaskHub` for group management and authorization. Invitation notifications reuse the existing notification system. Landing page patterns follow existing list views.
6. **Server-rendered structure, client-rendered canvas** — Page structure (landing page, session page chrome, side panel, chat) uses MVC + HTMX. The canvas itself is rendered client-side using HTML5 Canvas or SVG with JavaScript, synchronized via SignalR.
7. **Single-presenter simplicity** — No conflict resolution, operational transforms, or multi-cursor logic. Only the presenter's client sends canvas updates; all other clients receive and render.
8. **Progressive enhancement** — The whiteboard session page degrades gracefully if SignalR is unavailable. Static canvas state loads from the server; real-time features require an active connection.
9. **Single feature branch** — All whiteboard work (Phases 52–61) is developed on the `feature/whiteboard` branch. The branch merges to `master` only when the full implementation is complete and validated. Work items are tracked in this document, not in GitHub issues.

---

## Phase 52: Whiteboard Data Model

Create the database entities, EF Core configuration, and migration for the whiteboard feature.

### 52.1 Entity Creation

- [x] Create `Whiteboard` entity in `TeamWare.Web/Models/Whiteboard.cs` (WB-01 through WB-04, Section 4.1)
  - [x] `Id` (int, PK, auto-increment)
  - [x] `Title` (string, required, max 200 characters)
  - [x] `OwnerId` (string, FK to ApplicationUser, required)
  - [x] `ProjectId` (int?, FK to Project, nullable — null means temporary board)
  - [x] `CurrentPresenterId` (string?, FK to ApplicationUser, nullable)
  - [x] `CanvasData` (string?, nullable — JSON-serialized canvas state)
  - [x] `CreatedAt` (DateTime, required, default UTC now)
  - [x] `UpdatedAt` (DateTime, required, updated on every canvas save)
  - [x] Navigation properties: `Owner`, `Project`, `CurrentPresenter`, `Invitations`, `ChatMessages`
- [x] Create `WhiteboardInvitation` entity in `TeamWare.Web/Models/WhiteboardInvitation.cs` (WB-26 through WB-32, Section 4.1)
  - [x] `Id` (int, PK, auto-increment)
  - [x] `WhiteboardId` (int, FK to Whiteboard, required)
  - [x] `UserId` (string, FK to ApplicationUser, required)
  - [x] `InvitedByUserId` (string, FK to ApplicationUser, required)
  - [x] `CreatedAt` (DateTime, required, default UTC now)
  - [x] Navigation properties: `Whiteboard`, `User`, `InvitedByUser`
- [x] Create `WhiteboardChatMessage` entity in `TeamWare.Web/Models/WhiteboardChatMessage.cs` (WB-51 through WB-55, Section 4.1)
  - [x] `Id` (int, PK, auto-increment)
  - [x] `WhiteboardId` (int, FK to Whiteboard, required)
  - [x] `UserId` (string, FK to ApplicationUser, required)
  - [x] `Content` (string, required, max 4000 characters)
  - [x] `CreatedAt` (DateTime, required, default UTC now)
  - [x] Navigation properties: `Whiteboard`, `User`

### 52.2 DbContext and EF Core Configuration

- [x] Add `DbSet<Whiteboard>`, `DbSet<WhiteboardInvitation>`, `DbSet<WhiteboardChatMessage>` to `ApplicationDbContext`
- [x] Configure EF Core relationships and constraints in `OnModelCreating`:
  - [x] `Whiteboard` → `ApplicationUser` (Owner): many-to-one via `OwnerId`, restrict delete
  - [x] `Whiteboard` → `ApplicationUser` (CurrentPresenter): many-to-one via `CurrentPresenterId`, set null on delete
  - [x] `Whiteboard` → `Project`: many-to-one via `ProjectId`, cascade delete (WB-65)
  - [x] `WhiteboardInvitation` → `Whiteboard`: many-to-one, cascade delete (WB-69)
  - [x] `WhiteboardInvitation` → `ApplicationUser` (User): many-to-one via `UserId`, restrict delete
  - [x] `WhiteboardInvitation` → `ApplicationUser` (InvitedBy): many-to-one via `InvitedByUserId`, restrict delete
  - [x] `WhiteboardChatMessage` → `Whiteboard`: many-to-one, cascade delete (WB-69)
  - [x] `WhiteboardChatMessage` → `ApplicationUser`: many-to-one via `UserId`, restrict delete
- [x] Configure indexes:
  - [x] `IX_Whiteboard_OwnerId` (OwnerId)
  - [x] `IX_Whiteboard_ProjectId` (ProjectId)
  - [x] `IX_WhiteboardInvitation_WhiteboardId` (WhiteboardId)
  - [x] `IX_WhiteboardInvitation_UserId` (UserId)
  - [x] `IX_WhiteboardInvitation_WhiteboardId_UserId` — Unique constraint
  - [x] `IX_WhiteboardChatMessage_WhiteboardId_CreatedAt` (WhiteboardId, CreatedAt)
- [x] Configure max length constraints:
  - [x] `Whiteboard.Title`: 200
  - [x] `WhiteboardChatMessage.Content`: 4000

### 52.3 Modified Entities

- [x] Add `WhiteboardInvitation` notification type to the existing `NotificationType` enum (Section 4.2)
- [x] Add `Whiteboards` navigation property (`ICollection<Whiteboard>`) to `Project` entity
- [x] Add `OwnedWhiteboards` navigation property (`ICollection<Whiteboard>`) to `ApplicationUser` entity
- [x] Add `WhiteboardInvitations` navigation property (`ICollection<WhiteboardInvitation>`) to `ApplicationUser` entity

### 52.4 Migration

- [x] Create EF Core migration `AddWhiteboard`
- [x] Write tests:
  - [x] Migration applies cleanly and existing data is unaffected
  - [x] CRUD operations on all three entities work correctly
  - [x] Cascade delete from Whiteboard removes invitations and chat messages
  - [x] Cascade delete from Project removes associated whiteboards
  - [x] Unique constraint on WhiteboardInvitation (WhiteboardId, UserId) is enforced

---

## Phase 53: Whiteboard Service Layer

Create the service interfaces, implementations, DTOs, and view models for whiteboard CRUD operations.

### 53.1 DTOs and View Models

- [x] Create `WhiteboardDto` in `TeamWare.Web/ViewModels/WhiteboardDto.cs`
  - [x] `Id`, `Title`, `OwnerId`, `OwnerDisplayName`, `ProjectId`, `ProjectName`, `CurrentPresenterId`, `CurrentPresenterDisplayName`, `CreatedAt`, `UpdatedAt`, `IsTemporary` (computed)
- [x] Create `WhiteboardDetailDto` in `TeamWare.Web/ViewModels/WhiteboardDetailDto.cs`
  - [x] Extends `WhiteboardDto` with `CanvasData`, `Invitations` (list), `ActiveUsers` (list)
- [x] Create `WhiteboardInvitationDto` in `TeamWare.Web/ViewModels/WhiteboardInvitationDto.cs`
  - [x] `Id`, `WhiteboardId`, `UserId`, `UserDisplayName`, `InvitedByUserId`, `CreatedAt`
- [x] Create `WhiteboardChatMessageDto` in `TeamWare.Web/ViewModels/WhiteboardChatMessageDto.cs`
  - [x] `Id`, `WhiteboardId`, `UserId`, `UserDisplayName`, `Content`, `CreatedAt`
- [x] Create `CreateWhiteboardViewModel` in `TeamWare.Web/ViewModels/CreateWhiteboardViewModel.cs`
  - [x] `Title` (required, max 200)
- [x] Create `WhiteboardLandingViewModel` in `TeamWare.Web/ViewModels/WhiteboardLandingViewModel.cs`
  - [x] `Whiteboards` (list of `WhiteboardDto`), sorted by active-first then recent
- [x] Create `WhiteboardSessionViewModel` in `TeamWare.Web/ViewModels/WhiteboardSessionViewModel.cs`
  - [x] `Whiteboard` (WhiteboardDetailDto), `IsOwner`, `IsPresenter`, `CanDraw`, `IsTemporary`, `IsSiteAdmin`, `AvailableProjects` (for save-to-project dropdown)

### 53.2 Whiteboard Service Interface and Implementation

- [x] Create `IWhiteboardService` interface in `TeamWare.Web/Services/IWhiteboardService.cs`
  - [x] `CreateAsync(string userId, string title)` → `ServiceResult<int>` (WB-01 through WB-04)
  - [x] `GetByIdAsync(int whiteboardId)` → `ServiceResult<WhiteboardDetailDto?>`
  - [x] `GetLandingPageAsync(string userId, bool isSiteAdmin)` → `ServiceResult<List<WhiteboardDto>>` (WB-36 through WB-42)
  - [x] `DeleteAsync(int whiteboardId, string userId, bool isSiteAdmin)` → `ServiceResult` (WB-66 through WB-69)
  - [x] `SaveCanvasAsync(int whiteboardId, string canvasData, string presenterId)` → `ServiceResult` (WB-56, WB-57)
  - [x] `CanAccessAsync(int whiteboardId, string userId, bool isSiteAdmin)` → `ServiceResult<bool>` (WB-81, WB-82)
- [x] Implement `WhiteboardService` in `TeamWare.Web/Services/WhiteboardService.cs`
  - [x] `CreateAsync`: create Whiteboard with OwnerId and CurrentPresenterId both set to the creator (WB-03)
  - [x] `GetByIdAsync`: load whiteboard with owner, presenter, invitations, include navigation properties
  - [x] `GetLandingPageAsync`: query boards the user is invited to (temporary) or can access via project membership (saved), union with all boards for site admins. Order by active-first, then recent (WB-40)
  - [x] `DeleteAsync`: verify ownership or site admin, cascade delete (WB-66, WB-67, WB-69)
  - [x] `SaveCanvasAsync`: verify presenter status, update CanvasData and UpdatedAt (WB-14, WB-56)
  - [x] `CanAccessAsync`: check invitation for temporary boards, project membership for saved boards, site admin override (WB-81, WB-82)
- [x] Register `IWhiteboardService` in DI
- [x] Write unit tests for all service methods

### 53.3 Invitation Service

- [x] Create `IWhiteboardInvitationService` interface in `TeamWare.Web/Services/IWhiteboardInvitationService.cs`
  - [x] `InviteAsync(int whiteboardId, string invitedUserId, string ownerUserId)` → `ServiceResult` (WB-26 through WB-28)
  - [x] `RevokeAsync(int whiteboardId, string userId)` → `ServiceResult` (WB-30, WB-34)
  - [x] `HasInvitationAsync(int whiteboardId, string userId)` → `bool` (WB-29)
  - [x] `CleanupInvalidInvitationsAsync(int whiteboardId)` → `ServiceResult` (WB-32)
- [x] Implement `WhiteboardInvitationService` in `TeamWare.Web/Services/WhiteboardInvitationService.cs`
  - [x] `InviteAsync`: verify caller is owner (WB-27), create invitation, send notification via existing notification system (WB-28)
  - [x] `RevokeAsync`: delete invitation record
  - [x] `HasInvitationAsync`: check for active invitation
  - [x] `CleanupInvalidInvitationsAsync`: for saved boards, delete invitations where user is no longer a project member (WB-32)
- [x] Register `IWhiteboardInvitationService` in DI
- [x] Write unit tests for all invitation service methods

### 53.4 Project Association Service

- [x] Create `IWhiteboardProjectService` interface in `TeamWare.Web/Services/IWhiteboardProjectService.cs`
  - [x] `SaveToProjectAsync(int whiteboardId, int projectId, string userId)` → `ServiceResult` (WB-60, WB-61)
  - [x] `ClearProjectAsync(int whiteboardId, string userId)` → `ServiceResult` (WB-64)
  - [x] `TransferOwnershipIfNeededAsync(int whiteboardId)` → `ServiceResult` (WB-11, EDGE-01)
- [x] Implement `WhiteboardProjectService` in `TeamWare.Web/Services/WhiteboardProjectService.cs`
  - [x] `SaveToProjectAsync`: verify caller is owner, set ProjectId, cleanup invalid invitations (WB-62)
  - [x] `ClearProjectAsync`: verify caller is owner, set ProjectId to null (WB-64)
  - [x] `TransferOwnershipIfNeededAsync`: if owner is not a member of the associated project, transfer ownership to the project owner (WB-11, EDGE-01)
- [x] Register `IWhiteboardProjectService` in DI
- [x] Write unit tests for all project association methods, including ownership transfer edge case

---

## Phase 54: Whiteboard Landing Page

Create the WhiteboardController, landing page view, and create flow.

### 54.1 Whiteboard Controller — Landing Page

- [x] Create `WhiteboardController` in `TeamWare.Web/Controllers/WhiteboardController.cs`
  - [x] `[Authorize]` attribute
  - [x] Inject `IWhiteboardService`, `IWhiteboardInvitationService`, `IWhiteboardProjectService`
  - [x] `Index` action (GET): call `GetLandingPageAsync`, return view with `WhiteboardLandingViewModel` (WB-36 through WB-42)
- [x] Create `Views/Whiteboard/Index.cshtml`
  - [x] List of boards showing title, creator name (temporary), project name (saved), last updated time (WB-20, WB-41)
  - [x] Active boards emphasized first, then recent (WB-40)
  - [x] "New Whiteboard" button (WB-01)
  - [x] Tailwind CSS 4 styling with light/dark theme support (UI-28)
- [x] Write tests:
  - [x] Landing page shows boards the user is invited to
  - [x] Landing page shows boards accessible via project membership
  - [x] Site admin sees all boards (WB-39)
  - [x] Unauthenticated user is redirected to login

### 54.2 Whiteboard Controller — Create Flow

- [x] Add `Create` action (GET) to `WhiteboardController`: return view with `CreateWhiteboardViewModel`
- [x] Add `Create` action (POST) to `WhiteboardController`:
  - [x] Validate model (title required, max 200)
  - [x] Call `WhiteboardService.CreateAsync`
  - [x] Redirect to the new whiteboard session page
- [x] Create `Views/Whiteboard/Create.cshtml`
  - [x] Simple form with title field and submit button (WB-02)
  - [x] Tailwind CSS styling consistent with other create forms
- [x] Write tests:
  - [x] Create with valid title succeeds and redirects
  - [x] Create with empty title fails validation
  - [x] Create with title exceeding 200 characters fails validation
  - [x] Creator is set as owner and initial presenter (WB-03)

### 54.3 Navigation Integration

- [x] Add "Whiteboards" link to the main navigation layout (UI-18)
  - [x] Visible to all authenticated users
  - [x] Follows existing navigation conventions
- [x] Write tests:
  - [x] "Whiteboards" link is visible to authenticated users
  - [x] "Whiteboards" link is not visible to unauthenticated users

---

## Phase 55: WhiteboardHub and Presence

Create the SignalR hub for real-time whiteboard collaboration and presence tracking.

### 55.1 WhiteboardHub Creation

- [x] Create `WhiteboardHub` class in `TeamWare.Web/Hubs/WhiteboardHub.cs` (WB-75 through WB-80)
  - [x] Inherit from `Hub`
  - [x] Add `[Authorize]` attribute
  - [x] Inject `IWhiteboardService`, `IWhiteboardInvitationService`
  - [x] Implement `static string GetGroupName(int whiteboardId)` returning `$"whiteboard-{whiteboardId}"`
  - [x] Implement `JoinBoard(int whiteboardId)`:
    - [x] Resolve authenticated user from `Context.User`
    - [x] Call `CanAccessAsync` to verify authorization (WB-80, WB-81, WB-82)
    - [x] If authorized, add connection to group
    - [x] Broadcast `UserJoined` to the group with `userId` and `displayName`
    - [x] If not authorized, throw `HubException`
  - [x] Implement `LeaveBoard(int whiteboardId)`:
    - [x] Remove connection from group
    - [x] Broadcast `UserLeft` to the group
  - [x] Override `OnDisconnectedAsync`:
    - [x] Track which board the user was connected to
    - [x] Broadcast `UserLeft` to the appropriate group
- [x] Register hub endpoint in `Program.cs`: `app.MapHub<WhiteboardHub>("/hubs/whiteboard")`
- [x] Write unit tests:
  - [x] `JoinBoard` succeeds for invited user (temporary board)
  - [x] `JoinBoard` succeeds for project member (saved board)
  - [x] `JoinBoard` succeeds for site admin
  - [x] `JoinBoard` fails for unauthorized user
  - [x] `LeaveBoard` succeeds and broadcasts `UserLeft`
  - [x] `GetGroupName` returns expected format

### 55.2 Presence Tracking

- [x] Create `IWhiteboardPresenceTracker` interface in `TeamWare.Web/Services/IWhiteboardPresenceTracker.cs`
  - [x] `AddConnectionAsync(int whiteboardId, string userId, string connectionId)` → track active connections
  - [x] `RemoveConnectionAsync(string connectionId)` → remove connection, return (whiteboardId, userId) if last connection for that user
  - [x] `GetActiveUsersAsync(int whiteboardId)` → list of active user IDs
  - [x] `IsUserActiveAsync(int whiteboardId, string userId)` → bool
- [x] Implement `WhiteboardPresenceTracker` in `TeamWare.Web/Services/WhiteboardPresenceTracker.cs`
  - [x] In-memory `ConcurrentDictionary` tracking connectionId → (whiteboardId, userId) and whiteboardId → set of userIds
  - [x] Handle multiple connections per user (multiple tabs)
- [x] Register `IWhiteboardPresenceTracker` as singleton in DI
- [x] Integrate presence tracker with `WhiteboardHub.JoinBoard`, `LeaveBoard`, and `OnDisconnectedAsync`
- [x] Write unit tests:
  - [x] Adding and removing connections updates the active user list
  - [x] Multiple connections from same user count as one active user
  - [x] Removing last connection for a user removes them from active list
  - [x] `GetActiveUsersAsync` returns correct list

---

## Phase 56: Whiteboard Session Page and Canvas

Create the session page, canvas rendering, and drawing tools.

### 56.1 Session Page Controller Actions

- [x] Add `Session` action (GET) to `WhiteboardController`:
  - [x] Accept `int id` parameter
  - [x] Verify access via `CanAccessAsync` (WB-81, WB-82)
  - [x] Handle edge cases: deleted board (EDGE-03), lost project membership (EDGE-04, WB-32)
  - [x] Build `WhiteboardSessionViewModel` with board details, user role flags, available projects
  - [x] Return session view
- [x] Add `Delete` action (POST) to `WhiteboardController`:
  - [x] Verify ownership or site admin (WB-66, WB-67)
  - [x] Call `WhiteboardService.DeleteAsync`
  - [x] Broadcast `BoardDeleted` via `IHubContext<WhiteboardHub>` to connected clients (WB-69)
  - [x] Redirect to landing page
- [x] Write tests:
  - [x] Session page loads for authorized user
  - [x] Session page returns 403 for unauthorized user
  - [x] Deleted board invitation cleanup works (EDGE-03)
  - [x] Delete succeeds for owner
  - [x] Delete succeeds for site admin
  - [x] Delete fails for non-owner non-admin

### 56.2 Session Page View

- [x] Create `Views/Whiteboard/Session.cshtml`
  - [x] Display owner and current presenter names prominently above the canvas (WB-71, UI-21)
  - [x] Main area: canvas element with `data-whiteboard-id` attribute
  - [x] Side panel: active user list at top, chat section below (WB-72, UI-22)
  - [x] Tool palette below or beside the canvas with mode switcher (WB-43, UI-25)
  - [x] "Save to Project" dropdown (visible to owner only) with project list and warning note (WB-63, UI-27)
  - [x] "Invite Users" button (visible to owner only)
  - [x] "Delete" button (visible to owner and site admin) with confirmation dialog (WB-68)
  - [x] `data-whiteboard-id`, `data-is-owner`, `data-is-presenter` attributes for client-side JavaScript
  - [x] Script references: `signalr.min.js`, `whiteboard.js`, `whiteboard-canvas.js`
  - [x] Tailwind CSS 4 styling with light/dark theme support (UI-28)
  - [x] Mobile responsive: canvas prioritized, side panel togglable (UI-29)
- [x] Write rendering tests:
  - [x] Owner sees invite, save-to-project, and delete controls
  - [x] Non-owner viewer does not see owner-only controls
  - [x] Presenter indicator is displayed correctly
  - [x] Canvas element and data attributes are present

### 56.3 Canvas Client-Side Implementation

- [x] Create `wwwroot/js/whiteboard-canvas.js`
  - [x] Canvas rendering engine using HTML5 Canvas or SVG
  - [x] Shape model: each element has `id`, `type`, `x`, `y`, `width`, `height`, `rotation`, `properties` (type-specific)
  - [x] Supported standard shapes (WB-45): rectangles, circles/ellipses, text labels, lines, arrows
  - [x] Supported specialized shapes (WB-46): servers, desktops, mobile devices, data (database/storage), switches, routers, firewalls, clouds
  - [x] Connectors between shapes (WB-47): line/arrow connecting two shape endpoints
  - [x] Freehand drawing (WB-48): pen tool recording point arrays
  - [x] Mode switching: diagram mode and freehand mode as equal toggleable modes (WB-43, WB-44)
  - [x] Shape selection, move, resize, delete (presenter only) (WB-49)
  - [x] Canvas pan and zoom
  - [x] Serialize canvas state to JSON for persistence and SignalR transmission
  - [x] Deserialize canvas state from JSON for rendering received updates
  - [x] Presenter-gated input: if `data-is-presenter` is false, disable all drawing/manipulation input (WB-25)
- [x] Write tests:
  - [x] Shape creation, selection, move, resize, delete
  - [x] Canvas serialization/deserialization round-trip
  - [x] Mode switching between diagram and freehand
  - [x] Input disabled when not presenter

### 56.4 Canvas SignalR Integration

- [x] Create `wwwroot/js/whiteboard.js`
  - [x] On page load, read `data-whiteboard-id` from the page container
  - [x] Build SignalR connection to `/hubs/whiteboard` with `withAutomaticReconnect()`
  - [x] On connection start, invoke `hub.JoinBoard(whiteboardId)`
  - [x] Register handler for `CanvasUpdated`: deserialize and render the updated canvas state
  - [x] When presenter makes a canvas change, call `hub.SendCanvasUpdate(whiteboardId, canvasData)` (WB-50)
  - [x] Implement debounce (200ms) for canvas updates to avoid flooding
  - [x] Register handler for `PresenterChanged`: update presenter display, toggle drawing input (WB-16)
  - [x] Register handler for `UserJoined` / `UserLeft`: update active user list in side panel (WB-70)
  - [x] Register handler for `UserRemoved`: if current user was removed, navigate to landing page (WB-34)
  - [x] Register handler for `BoardDeleted`: navigate to landing page
  - [x] Handle reconnection: re-invoke `JoinBoard` on reconnect
  - [x] Load initial canvas state from the server-rendered `CanvasData` on page load
- [x] Add `SendCanvasUpdate` method to `WhiteboardHub`:
  - [x] Verify caller is current presenter (WB-83)
  - [x] Save canvas state via `WhiteboardService.SaveCanvasAsync` (WB-56)
  - [x] Broadcast `CanvasUpdated` to group (excluding caller)
- [x] Write tests:
  - [x] SignalR connection established and `JoinBoard` invoked
  - [x] Canvas updates are sent and received
  - [x] Non-presenter cannot send canvas updates
  - [x] Presenter change updates UI and drawing permissions

---

## Phase 57: Chat Sidebar

Add real-time chat to the whiteboard session page.

### 57.1 Chat Service

- [x] Create `IWhiteboardChatService` interface in `TeamWare.Web/Services/IWhiteboardChatService.cs`
  - [x] `SendMessageAsync(int whiteboardId, string userId, string content)` → `ServiceResult<WhiteboardChatMessageDto>` (WB-51 through WB-55)
  - [x] `GetMessagesAsync(int whiteboardId, int page, int pageSize)` → `ServiceResult<List<WhiteboardChatMessageDto>>`
- [x] Implement `WhiteboardChatService` in `TeamWare.Web/Services/WhiteboardChatService.cs`
  - [x] `SendMessageAsync`: validate content length (max 4000, WB-54), create message, return DTO
  - [x] `GetMessagesAsync`: load messages ordered by CreatedAt descending, paginated
- [x] Register `IWhiteboardChatService` in DI
- [x] Write unit tests for send and get methods

### 57.2 Chat Hub Methods

- [x] Add `SendChatMessage` method to `WhiteboardHub` (WB-78):
  - [x] Verify caller is connected to the whiteboard (WB-84)
  - [x] Call `WhiteboardChatService.SendMessageAsync`
  - [x] Broadcast `ChatMessageReceived` to group with message details (WB-53)
- [x] Write tests:
  - [x] Chat message is saved and broadcast
  - [x] Message exceeding 4000 characters is rejected
  - [x] Unauthorized user cannot send chat messages

### 57.3 Chat UI

- [x] Add chat section to `Session.cshtml` side panel (UI-22):
  - [x] Scrollable message list showing author display name and timestamp (WB-55)
  - [x] Message input field at the bottom of the chat area
  - [x] Auto-scroll to newest message
  - [x] Load initial chat history from server on page load (paginated)
- [x] Add chat handling to `whiteboard.js`:
  - [x] Register `ChatMessageReceived` handler: append message to chat list, auto-scroll
  - [x] On message submit: call `hub.SendChatMessage(whiteboardId, content)`, clear input
- [x] Write tests:
  - [x] Chat messages display with author and timestamp
  - [x] New messages auto-scroll the chat area
  - [x] Chat input clears after sending

---

## Phase 58: Invitations and Notifications

Implement the invitation flow including sending invitations, receiving notifications, and access enforcement.

### 58.1 Invitation Controller Actions

- [x] Add `Invite` action (POST) to `WhiteboardController`:
  - [x] Accept whiteboard ID and user ID(s) to invite
  - [x] Verify caller is owner (WB-27, WB-85)
  - [x] Call `WhiteboardInvitationService.InviteAsync` for each user
  - [x] Return success/failure response (HTMX partial or JSON)
- [x] Add `InviteForm` action (GET) to `WhiteboardController`:
  - [x] Return partial view with user search/selection for inviting users
  - [x] Exclude users already invited and the owner
- [x] Write tests:
  - [x] Owner can invite users
  - [x] Non-owner cannot invite users
  - [x] Duplicate invitation is handled gracefully
  - [x] Invited user receives notification

### 58.2 Notification Integration

- [x] Create `WhiteboardInvitation` notification handler using existing notification patterns
  - [x] Notification links to the whiteboard session page
  - [x] Notification text includes whiteboard title and inviter name
- [x] Handle notification click:
  - [x] If board still exists and user has access, navigate to session page
  - [x] If board is deleted, clean up invitation and show "board no longer available" message (EDGE-03, WB-31)
  - [x] If saved board and user lost project membership, clean up invitation and show "no longer have permission" (EDGE-04, WB-32)
- [x] Write tests:
  - [x] Notification is created when invitation is sent
  - [x] Notification links to correct session page
  - [x] Deleted board invitation cleanup on notification click
  - [x] Lost project membership invitation cleanup on notification click

### 58.3 Invitation UI on Session Page

- [x] Add invite user modal/dropdown to session page (owner only):
  - [x] User search field to find users by name
  - [x] "Invite" button per user result
  - [x] Show already-invited users as disabled
  - [x] HTMX for inline invite without page reload
- [x] Add invited users list to session page sidebar (visible to owner):
  - [x] Show invited users who are not currently connected
- [x] Write tests:
  - [x] Invite UI is visible only to owner
  - [x] User search returns correct results
  - [x] Invitation is sent and reflected in the UI

---

## Phase 59: Presenter Control

Implement presenter assignment, reclamation, and user removal.

### 59.1 Presenter Hub Methods

- [x] Add `AssignPresenter` method to `WhiteboardHub` (WB-78):
  - [x] Verify caller is owner (WB-86)
  - [x] Verify target user is currently viewing the whiteboard via presence tracker (WB-15, WB-18)
  - [x] Update `CurrentPresenterId` in database
  - [x] Broadcast `PresenterChanged` to group (WB-16)
- [x] Add `ReclaimPresenter` method to `WhiteboardHub` (WB-78):
  - [x] Verify caller is owner (WB-86)
  - [x] Update `CurrentPresenterId` to owner's user ID
  - [x] Broadcast `PresenterChanged` to group (WB-17)
- [x] Write tests:
  - [x] Owner can assign presenter to active viewer
  - [x] Owner cannot assign presenter to non-active user
  - [x] Non-owner cannot assign presenter
  - [x] Owner can reclaim presenter
  - [x] Non-owner cannot reclaim presenter
  - [x] `PresenterChanged` is broadcast after assignment/reclamation

### 59.2 User Removal Hub Method

- [x] Add `RemoveUser` method to `WhiteboardHub` (WB-78):
  - [x] Verify caller is owner (WB-87)
  - [x] Verify board is temporary (WB-35)
  - [x] Revoke invitation via `WhiteboardInvitationService.RevokeAsync` (WB-34)
  - [x] Broadcast `UserRemoved` to the removed user's connections
  - [x] Remove the user's connections from the SignalR group
  - [x] If removed user was presenter, clear `CurrentPresenterId` and broadcast `PresenterChanged`
- [x] Write tests:
  - [x] Owner can remove user from temporary board
  - [x] Owner cannot remove user from saved board (WB-35)
  - [x] Non-owner cannot remove user
  - [x] Removed user's invitation is revoked
  - [x] Removed user receives `UserRemoved` event
  - [x] If removed user was presenter, presenter is cleared

### 59.3 Presenter Context Menu UI

- [x] Add context menu to active user list items in session page (UI-24):
  - [x] "Make Presenter" option: visible to owner for any non-presenter active user (WB-19)
  - [x] "Remove User" option: visible to owner on temporary boards only (WB-33, WB-35)
  - [x] Alpine.js for context menu show/hide behavior
- [x] Wire context menu actions to SignalR hub calls:
  - [x] "Make Presenter" → `hub.AssignPresenter(whiteboardId, userId)`
  - [x] "Remove User" → `hub.RemoveUser(whiteboardId, userId)`
- [x] Update presenter display above board when `PresenterChanged` is received (UI-21)
- [x] Write tests:
  - [x] Context menu appears for owner
  - [x] Context menu does not appear for non-owner
  - [x] "Remove User" not shown on saved boards
  - [x] Presenter change updates the prominent display

---

## Phase 60: Project Association and Saved Boards

Implement save-to-project, change project, clear project, and ownership transfer.

### 60.1 Project Association Controller Actions

- [x] Add `SaveToProject` action (POST) to `WhiteboardController`:
  - [x] Accept whiteboard ID and project ID
  - [x] Verify caller is owner (WB-88)
  - [x] Verify caller is a member of the target project
  - [x] Call `WhiteboardProjectService.SaveToProjectAsync` (WB-60)
  - [x] Return updated session page or HTMX partial
- [x] Add `ClearProject` action (POST) to `WhiteboardController`:
  - [x] Verify caller is owner (WB-88)
  - [x] Call `WhiteboardProjectService.ClearProjectAsync` (WB-64)
  - [x] Return updated session page or HTMX partial
- [x] Write tests:
  - [x] Owner can save board to project they belong to
  - [x] Owner cannot save board to project they do not belong to
  - [x] Non-owner cannot save board to project
  - [x] Clearing project restores temporary status (EDGE-07)

### 60.2 Save-to-Project UI

- [x] Add project selection dropdown to session page (owner only):
  - [x] List projects the owner is a member of
  - [x] Show current project association if saved
  - [x] "Clear Project" button if currently saved (WB-64)
  - [x] Warning note: "Only project members will be able to see this board" (WB-63, UI-27)
- [x] Wire save/clear actions to controller endpoints
- [x] Write tests:
  - [x] Project dropdown is visible only to owner
  - [x] Warning note is displayed
  - [x] Save and clear actions work correctly

### 60.3 Ownership Transfer

- [x] Integrate ownership transfer into project membership change hooks:
  - [x] When a user is removed from a project, check if they own any whiteboards saved to that project
  - [x] If so, call `WhiteboardProjectService.TransferOwnershipIfNeededAsync` (WB-11, EDGE-01)
  - [x] Transfer ownership to the project owner
- [x] Integrate project deletion cascade:
  - [x] Verify that EF Core cascade delete configuration handles project deletion → whiteboard deletion (WB-65, EDGE-02)
- [x] Write tests:
  - [x] Ownership transfers when owner loses project membership (EDGE-01)
  - [x] New owner is the project owner
  - [x] Project deletion cascades to all associated whiteboards (EDGE-02)

---

## Phase 61: Whiteboard Polish and Hardening

Final phase: security review, edge cases, UI polish, accessibility, and documentation.

### 61.1 Security Hardening

- [x] Verify all WhiteboardHub methods enforce authorization server-side (WB-80, WB-90, SEC-08)
- [x] Verify all controller actions enforce authorization (WB-90)
- [x] Verify canvas data is validated server-side to prevent injection (SEC-09)
- [x] Verify chat message content is sanitized to prevent XSS (SEC-10)
- [x] Verify invitation-gated access for temporary boards (WB-29, WB-81)
- [x] Verify project membership access for saved boards (WB-62, WB-82)
- [x] Verify site admin can access all boards (WB-39, WB-67, WB-89)

### 61.2 Edge Cases and Regression Testing

- [x] Presenter disconnects: board remains active, no one can draw, owner must reclaim (EDGE-05, WB-73)
- [x] Owner disconnects: board remains active, presenter retains status (EDGE-06)
- [x] User follows invitation to deleted board: invitation cleaned up, user informed (EDGE-03, WB-31)
- [x] User with invitation to saved board loses project membership: invitation cleaned up, access denied (EDGE-04, WB-32)
- [x] Project association cleared: board returns to temporary, invitations remain (EDGE-07)
- [x] Multiple browser tabs from same user: presence tracking handles correctly
- [x] Rapid canvas updates: debounce prevents flooding
- [x] Large canvas state: JSON serialization handles large payloads
- [x] Concurrent presenter assignment: no race conditions

### 61.3 UI Consistency Review

- [x] Verify Tailwind CSS 4 styling matches existing TeamWare conventions (UI-28)
- [x] Verify light/dark theme support throughout all whiteboard views
- [x] Verify mobile responsiveness: canvas prioritized, side panel togglable (UI-29)
- [x] Verify the landing page layout is consistent with other list pages
- [x] Verify confirmation dialogs follow existing patterns
- [x] Verify toast notifications (if any) follow existing patterns

### 61.4 Performance Verification

- [x] Verify canvas updates via SignalR have no perceptible delay under normal conditions (PERF-06)
- [x] Verify landing page loads within 500ms (PERF-07)
- [x] Profile canvas rendering with 50+ shapes
- [x] Profile SignalR message throughput with 20 concurrent viewers

### 61.5 Documentation

- [x] Update `WhiteBoardSpecification.md` with any deviations from the specification
- [x] Document the client-side canvas architecture and shape model
- [x] Document the WhiteboardHub API for future reference
- [x] Note stale-board cleanup as a future Hangfire enhancement (out of scope)

---

## Files to Create or Modify

### New Files

| File | Phase | Description |
|------|-------|-------------|
| `Models/Whiteboard.cs` | 52.1 | Whiteboard entity |
| `Models/WhiteboardInvitation.cs` | 52.1 | Invitation entity |
| `Models/WhiteboardChatMessage.cs` | 52.1 | Chat message entity |
| `ViewModels/WhiteboardDto.cs` | 53.1 | Whiteboard list DTO |
| `ViewModels/WhiteboardDetailDto.cs` | 53.1 | Whiteboard detail DTO |
| `ViewModels/WhiteboardInvitationDto.cs` | 53.1 | Invitation DTO |
| `ViewModels/WhiteboardChatMessageDto.cs` | 53.1 | Chat message DTO |
| `ViewModels/CreateWhiteboardViewModel.cs` | 53.1 | Create form view model |
| `ViewModels/WhiteboardLandingViewModel.cs` | 53.1 | Landing page view model |
| `ViewModels/WhiteboardSessionViewModel.cs` | 53.1 | Session page view model |
| `Services/IWhiteboardService.cs` | 53.2 | Whiteboard service interface |
| `Services/WhiteboardService.cs` | 53.2 | Whiteboard service implementation |
| `Services/IWhiteboardInvitationService.cs` | 53.3 | Invitation service interface |
| `Services/WhiteboardInvitationService.cs` | 53.3 | Invitation service implementation |
| `Services/IWhiteboardProjectService.cs` | 53.4 | Project association service interface |
| `Services/WhiteboardProjectService.cs` | 53.4 | Project association service implementation |
| `Services/IWhiteboardChatService.cs` | 57.1 | Chat service interface |
| `Services/WhiteboardChatService.cs` | 57.1 | Chat service implementation |
| `Services/IWhiteboardPresenceTracker.cs` | 55.2 | Presence tracker interface |
| `Services/WhiteboardPresenceTracker.cs` | 55.2 | Presence tracker implementation |
| `Controllers/WhiteboardController.cs` | 54.1 | Whiteboard MVC controller |
| `Hubs/WhiteboardHub.cs` | 55.1 | SignalR hub |
| `Views/Whiteboard/Index.cshtml` | 54.1 | Landing page view |
| `Views/Whiteboard/Create.cshtml` | 54.2 | Create form view |
| `Views/Whiteboard/Session.cshtml` | 56.2 | Session page view |
| `wwwroot/js/whiteboard.js` | 56.4 | SignalR client and session orchestration |
| `wwwroot/js/whiteboard-canvas.js` | 56.3 | Canvas rendering engine and drawing tools |

### Modified Files

| File | Phase | Change |
|------|-------|--------|
| `Data/ApplicationDbContext.cs` | 52.2 | Add DbSets and configure relationships/indexes |
| `Models/NotificationType.cs` (or equivalent enum) | 52.3 | Add `WhiteboardInvitation` value |
| `Models/Project.cs` | 52.3 | Add `Whiteboards` navigation property |
| `Models/ApplicationUser.cs` | 52.3 | Add `OwnedWhiteboards` and `WhiteboardInvitations` navigation properties |
| `Program.cs` | 55.1 | Register `WhiteboardHub` endpoint and DI services |
| `Views/Shared/_Layout.cshtml` (or navigation partial) | 54.3 | Add "Whiteboards" navigation link |

### Test Files

| File | Phase | Description |
|------|-------|-------------|
| `Tests/WhiteboardEntityTests.cs` | 52.4 | Entity CRUD and cascade delete tests |
| `Tests/WhiteboardServiceTests.cs` | 53.2 | Whiteboard service unit tests |
| `Tests/WhiteboardInvitationServiceTests.cs` | 53.3 | Invitation service unit tests |
| `Tests/WhiteboardProjectServiceTests.cs` | 53.4 | Project association service unit tests |
| `Tests/WhiteboardControllerTests.cs` | 54.1, 54.2 | Controller action tests |
| `Tests/WhiteboardHubTests.cs` | 55.1, 56.4, 57.2, 59.1, 59.2 | SignalR hub tests |
| `Tests/WhiteboardPresenceTrackerTests.cs` | 55.2 | Presence tracker tests |
| `Tests/WhiteboardChatServiceTests.cs` | 57.1 | Chat service tests |
| `Tests/WhiteboardIntegrationTests.cs` | 61.2 | End-to-end integration tests |
