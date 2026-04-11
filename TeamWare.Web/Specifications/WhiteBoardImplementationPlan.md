# TeamWare - Whiteboard Implementation Plan

This document defines the phased implementation plan for the Whiteboard feature, based on the [Whiteboard Specification](WhiteBoardSpecification.md). Each phase builds on the previous one and is broken into work items suitable for GitHub Issues.

---

## Progress Summary

| Phase | Description | Status |
|-------|------------|--------|
| 52 | Whiteboard Data Model | Not Started |
| 53 | Whiteboard Service Layer | Not Started |
| 54 | Whiteboard Landing Page | Not Started |
| 55 | WhiteboardHub and Presence | Not Started |
| 56 | Whiteboard Session Page and Canvas | Not Started |
| 57 | Chat Sidebar | Not Started |
| 58 | Invitations and Notifications | Not Started |
| 59 | Presenter Control | Not Started |
| 60 | Project Association and Saved Boards | Not Started |
| 61 | Whiteboard Polish and Hardening | Not Started |

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

- [ ] Create `Whiteboard` entity in `TeamWare.Web/Models/Whiteboard.cs` (WB-01 through WB-04, Section 4.1)
  - [ ] `Id` (int, PK, auto-increment)
  - [ ] `Title` (string, required, max 200 characters)
  - [ ] `OwnerId` (string, FK to ApplicationUser, required)
  - [ ] `ProjectId` (int?, FK to Project, nullable — null means temporary board)
  - [ ] `CurrentPresenterId` (string?, FK to ApplicationUser, nullable)
  - [ ] `CanvasData` (string?, nullable — JSON-serialized canvas state)
  - [ ] `CreatedAt` (DateTime, required, default UTC now)
  - [ ] `UpdatedAt` (DateTime, required, updated on every canvas save)
  - [ ] Navigation properties: `Owner`, `Project`, `CurrentPresenter`, `Invitations`, `ChatMessages`
- [ ] Create `WhiteboardInvitation` entity in `TeamWare.Web/Models/WhiteboardInvitation.cs` (WB-26 through WB-32, Section 4.1)
  - [ ] `Id` (int, PK, auto-increment)
  - [ ] `WhiteboardId` (int, FK to Whiteboard, required)
  - [ ] `UserId` (string, FK to ApplicationUser, required)
  - [ ] `InvitedByUserId` (string, FK to ApplicationUser, required)
  - [ ] `CreatedAt` (DateTime, required, default UTC now)
  - [ ] Navigation properties: `Whiteboard`, `User`, `InvitedByUser`
- [ ] Create `WhiteboardChatMessage` entity in `TeamWare.Web/Models/WhiteboardChatMessage.cs` (WB-51 through WB-55, Section 4.1)
  - [ ] `Id` (int, PK, auto-increment)
  - [ ] `WhiteboardId` (int, FK to Whiteboard, required)
  - [ ] `UserId` (string, FK to ApplicationUser, required)
  - [ ] `Content` (string, required, max 4000 characters)
  - [ ] `CreatedAt` (DateTime, required, default UTC now)
  - [ ] Navigation properties: `Whiteboard`, `User`

### 52.2 DbContext and EF Core Configuration

- [ ] Add `DbSet<Whiteboard>`, `DbSet<WhiteboardInvitation>`, `DbSet<WhiteboardChatMessage>` to `ApplicationDbContext`
- [ ] Configure EF Core relationships and constraints in `OnModelCreating`:
  - [ ] `Whiteboard` → `ApplicationUser` (Owner): many-to-one via `OwnerId`, restrict delete
  - [ ] `Whiteboard` → `ApplicationUser` (CurrentPresenter): many-to-one via `CurrentPresenterId`, set null on delete
  - [ ] `Whiteboard` → `Project`: many-to-one via `ProjectId`, cascade delete (WB-65)
  - [ ] `WhiteboardInvitation` → `Whiteboard`: many-to-one, cascade delete (WB-69)
  - [ ] `WhiteboardInvitation` → `ApplicationUser` (User): many-to-one via `UserId`, restrict delete
  - [ ] `WhiteboardInvitation` → `ApplicationUser` (InvitedBy): many-to-one via `InvitedByUserId`, restrict delete
  - [ ] `WhiteboardChatMessage` → `Whiteboard`: many-to-one, cascade delete (WB-69)
  - [ ] `WhiteboardChatMessage` → `ApplicationUser`: many-to-one via `UserId`, restrict delete
- [ ] Configure indexes:
  - [ ] `IX_Whiteboard_OwnerId` (OwnerId)
  - [ ] `IX_Whiteboard_ProjectId` (ProjectId)
  - [ ] `IX_WhiteboardInvitation_WhiteboardId` (WhiteboardId)
  - [ ] `IX_WhiteboardInvitation_UserId` (UserId)
  - [ ] `IX_WhiteboardInvitation_WhiteboardId_UserId` — Unique constraint
  - [ ] `IX_WhiteboardChatMessage_WhiteboardId_CreatedAt` (WhiteboardId, CreatedAt)
- [ ] Configure max length constraints:
  - [ ] `Whiteboard.Title`: 200
  - [ ] `WhiteboardChatMessage.Content`: 4000

### 52.3 Modified Entities

- [ ] Add `WhiteboardInvitation` notification type to the existing `NotificationType` enum (Section 4.2)
- [ ] Add `Whiteboards` navigation property (`ICollection<Whiteboard>`) to `Project` entity
- [ ] Add `OwnedWhiteboards` navigation property (`ICollection<Whiteboard>`) to `ApplicationUser` entity
- [ ] Add `WhiteboardInvitations` navigation property (`ICollection<WhiteboardInvitation>`) to `ApplicationUser` entity

### 52.4 Migration

- [ ] Create EF Core migration `AddWhiteboard`
- [ ] Write tests:
  - [ ] Migration applies cleanly and existing data is unaffected
  - [ ] CRUD operations on all three entities work correctly
  - [ ] Cascade delete from Whiteboard removes invitations and chat messages
  - [ ] Cascade delete from Project removes associated whiteboards
  - [ ] Unique constraint on WhiteboardInvitation (WhiteboardId, UserId) is enforced

---

## Phase 53: Whiteboard Service Layer

Create the service interfaces, implementations, DTOs, and view models for whiteboard CRUD operations.

### 53.1 DTOs and View Models

- [ ] Create `WhiteboardDto` in `TeamWare.Web/ViewModels/WhiteboardDto.cs`
  - [ ] `Id`, `Title`, `OwnerId`, `OwnerDisplayName`, `ProjectId`, `ProjectName`, `CurrentPresenterId`, `CurrentPresenterDisplayName`, `CreatedAt`, `UpdatedAt`, `IsTemporary` (computed)
- [ ] Create `WhiteboardDetailDto` in `TeamWare.Web/ViewModels/WhiteboardDetailDto.cs`
  - [ ] Extends `WhiteboardDto` with `CanvasData`, `Invitations` (list), `ActiveUsers` (list)
- [ ] Create `WhiteboardInvitationDto` in `TeamWare.Web/ViewModels/WhiteboardInvitationDto.cs`
  - [ ] `Id`, `WhiteboardId`, `UserId`, `UserDisplayName`, `InvitedByUserId`, `CreatedAt`
- [ ] Create `WhiteboardChatMessageDto` in `TeamWare.Web/ViewModels/WhiteboardChatMessageDto.cs`
  - [ ] `Id`, `WhiteboardId`, `UserId`, `UserDisplayName`, `Content`, `CreatedAt`
- [ ] Create `CreateWhiteboardViewModel` in `TeamWare.Web/ViewModels/CreateWhiteboardViewModel.cs`
  - [ ] `Title` (required, max 200)
- [ ] Create `WhiteboardLandingViewModel` in `TeamWare.Web/ViewModels/WhiteboardLandingViewModel.cs`
  - [ ] `Whiteboards` (list of `WhiteboardDto`), sorted by active-first then recent
- [ ] Create `WhiteboardSessionViewModel` in `TeamWare.Web/ViewModels/WhiteboardSessionViewModel.cs`
  - [ ] `Whiteboard` (WhiteboardDetailDto), `IsOwner`, `IsPresenter`, `CanDraw`, `IsTemporary`, `IsSiteAdmin`, `AvailableProjects` (for save-to-project dropdown)

### 53.2 Whiteboard Service Interface and Implementation

- [ ] Create `IWhiteboardService` interface in `TeamWare.Web/Services/IWhiteboardService.cs`
  - [ ] `CreateAsync(string userId, string title)` → `ServiceResult<int>` (WB-01 through WB-04)
  - [ ] `GetByIdAsync(int whiteboardId)` → `ServiceResult<WhiteboardDetailDto?>`
  - [ ] `GetLandingPageAsync(string userId, bool isSiteAdmin)` → `ServiceResult<List<WhiteboardDto>>` (WB-36 through WB-42)
  - [ ] `DeleteAsync(int whiteboardId, string userId, bool isSiteAdmin)` → `ServiceResult` (WB-66 through WB-69)
  - [ ] `SaveCanvasAsync(int whiteboardId, string canvasData, string presenterId)` → `ServiceResult` (WB-56, WB-57)
  - [ ] `CanAccessAsync(int whiteboardId, string userId, bool isSiteAdmin)` → `ServiceResult<bool>` (WB-81, WB-82)
- [ ] Implement `WhiteboardService` in `TeamWare.Web/Services/WhiteboardService.cs`
  - [ ] `CreateAsync`: create Whiteboard with OwnerId and CurrentPresenterId both set to the creator (WB-03)
  - [ ] `GetByIdAsync`: load whiteboard with owner, presenter, invitations, include navigation properties
  - [ ] `GetLandingPageAsync`: query boards the user is invited to (temporary) or can access via project membership (saved), union with all boards for site admins. Order by active-first, then recent (WB-40)
  - [ ] `DeleteAsync`: verify ownership or site admin, cascade delete (WB-66, WB-67, WB-69)
  - [ ] `SaveCanvasAsync`: verify presenter status, update CanvasData and UpdatedAt (WB-14, WB-56)
  - [ ] `CanAccessAsync`: check invitation for temporary boards, project membership for saved boards, site admin override (WB-81, WB-82)
- [ ] Register `IWhiteboardService` in DI
- [ ] Write unit tests for all service methods

### 53.3 Invitation Service

- [ ] Create `IWhiteboardInvitationService` interface in `TeamWare.Web/Services/IWhiteboardInvitationService.cs`
  - [ ] `InviteAsync(int whiteboardId, string invitedUserId, string ownerUserId)` → `ServiceResult` (WB-26 through WB-28)
  - [ ] `RevokeAsync(int whiteboardId, string userId)` → `ServiceResult` (WB-30, WB-34)
  - [ ] `HasInvitationAsync(int whiteboardId, string userId)` → `bool` (WB-29)
  - [ ] `CleanupInvalidInvitationsAsync(int whiteboardId)` → `ServiceResult` (WB-32)
- [ ] Implement `WhiteboardInvitationService` in `TeamWare.Web/Services/WhiteboardInvitationService.cs`
  - [ ] `InviteAsync`: verify caller is owner (WB-27), create invitation, send notification via existing notification system (WB-28)
  - [ ] `RevokeAsync`: delete invitation record
  - [ ] `HasInvitationAsync`: check for active invitation
  - [ ] `CleanupInvalidInvitationsAsync`: for saved boards, delete invitations where user is no longer a project member (WB-32)
- [ ] Register `IWhiteboardInvitationService` in DI
- [ ] Write unit tests for all invitation service methods

### 53.4 Project Association Service

- [ ] Create `IWhiteboardProjectService` interface in `TeamWare.Web/Services/IWhiteboardProjectService.cs`
  - [ ] `SaveToProjectAsync(int whiteboardId, int projectId, string userId)` → `ServiceResult` (WB-60, WB-61)
  - [ ] `ClearProjectAsync(int whiteboardId, string userId)` → `ServiceResult` (WB-64)
  - [ ] `TransferOwnershipIfNeededAsync(int whiteboardId)` → `ServiceResult` (WB-11, EDGE-01)
- [ ] Implement `WhiteboardProjectService` in `TeamWare.Web/Services/WhiteboardProjectService.cs`
  - [ ] `SaveToProjectAsync`: verify caller is owner, set ProjectId, cleanup invalid invitations (WB-62)
  - [ ] `ClearProjectAsync`: verify caller is owner, set ProjectId to null (WB-64)
  - [ ] `TransferOwnershipIfNeededAsync`: if owner is not a member of the associated project, transfer ownership to the project owner (WB-11, EDGE-01)
- [ ] Register `IWhiteboardProjectService` in DI
- [ ] Write unit tests for all project association methods, including ownership transfer edge case

---

## Phase 54: Whiteboard Landing Page

Create the WhiteboardController, landing page view, and create flow.

### 54.1 Whiteboard Controller — Landing Page

- [ ] Create `WhiteboardController` in `TeamWare.Web/Controllers/WhiteboardController.cs`
  - [ ] `[Authorize]` attribute
  - [ ] Inject `IWhiteboardService`, `IWhiteboardInvitationService`, `IWhiteboardProjectService`
  - [ ] `Index` action (GET): call `GetLandingPageAsync`, return view with `WhiteboardLandingViewModel` (WB-36 through WB-42)
- [ ] Create `Views/Whiteboard/Index.cshtml`
  - [ ] List of boards showing title, creator name (temporary), project name (saved), last updated time (WB-20, WB-41)
  - [ ] Active boards emphasized first, then recent (WB-40)
  - [ ] "New Whiteboard" button (WB-01)
  - [ ] Tailwind CSS 4 styling with light/dark theme support (UI-28)
- [ ] Write tests:
  - [ ] Landing page shows boards the user is invited to
  - [ ] Landing page shows boards accessible via project membership
  - [ ] Site admin sees all boards (WB-39)
  - [ ] Unauthenticated user is redirected to login

### 54.2 Whiteboard Controller — Create Flow

- [ ] Add `Create` action (GET) to `WhiteboardController`: return view with `CreateWhiteboardViewModel`
- [ ] Add `Create` action (POST) to `WhiteboardController`:
  - [ ] Validate model (title required, max 200)
  - [ ] Call `WhiteboardService.CreateAsync`
  - [ ] Redirect to the new whiteboard session page
- [ ] Create `Views/Whiteboard/Create.cshtml`
  - [ ] Simple form with title field and submit button (WB-02)
  - [ ] Tailwind CSS styling consistent with other create forms
- [ ] Write tests:
  - [ ] Create with valid title succeeds and redirects
  - [ ] Create with empty title fails validation
  - [ ] Create with title exceeding 200 characters fails validation
  - [ ] Creator is set as owner and initial presenter (WB-03)

### 54.3 Navigation Integration

- [ ] Add "Whiteboards" link to the main navigation layout (UI-18)
  - [ ] Visible to all authenticated users
  - [ ] Follows existing navigation conventions
- [ ] Write tests:
  - [ ] "Whiteboards" link is visible to authenticated users
  - [ ] "Whiteboards" link is not visible to unauthenticated users

---

## Phase 55: WhiteboardHub and Presence

Create the SignalR hub for real-time whiteboard collaboration and presence tracking.

### 55.1 WhiteboardHub Creation

- [ ] Create `WhiteboardHub` class in `TeamWare.Web/Hubs/WhiteboardHub.cs` (WB-75 through WB-80)
  - [ ] Inherit from `Hub`
  - [ ] Add `[Authorize]` attribute
  - [ ] Inject `IWhiteboardService`, `IWhiteboardInvitationService`
  - [ ] Implement `static string GetGroupName(int whiteboardId)` returning `$"whiteboard-{whiteboardId}"`
  - [ ] Implement `JoinBoard(int whiteboardId)`:
    - [ ] Resolve authenticated user from `Context.User`
    - [ ] Call `CanAccessAsync` to verify authorization (WB-80, WB-81, WB-82)
    - [ ] If authorized, add connection to group
    - [ ] Broadcast `UserJoined` to the group with `userId` and `displayName`
    - [ ] If not authorized, throw `HubException`
  - [ ] Implement `LeaveBoard(int whiteboardId)`:
    - [ ] Remove connection from group
    - [ ] Broadcast `UserLeft` to the group
  - [ ] Override `OnDisconnectedAsync`:
    - [ ] Track which board the user was connected to
    - [ ] Broadcast `UserLeft` to the appropriate group
- [ ] Register hub endpoint in `Program.cs`: `app.MapHub<WhiteboardHub>("/hubs/whiteboard")`
- [ ] Write unit tests:
  - [ ] `JoinBoard` succeeds for invited user (temporary board)
  - [ ] `JoinBoard` succeeds for project member (saved board)
  - [ ] `JoinBoard` succeeds for site admin
  - [ ] `JoinBoard` fails for unauthorized user
  - [ ] `LeaveBoard` succeeds and broadcasts `UserLeft`
  - [ ] `GetGroupName` returns expected format

### 55.2 Presence Tracking

- [ ] Create `IWhiteboardPresenceTracker` interface in `TeamWare.Web/Services/IWhiteboardPresenceTracker.cs`
  - [ ] `AddConnectionAsync(int whiteboardId, string userId, string connectionId)` → track active connections
  - [ ] `RemoveConnectionAsync(string connectionId)` → remove connection, return (whiteboardId, userId) if last connection for that user
  - [ ] `GetActiveUsersAsync(int whiteboardId)` → list of active user IDs
  - [ ] `IsUserActiveAsync(int whiteboardId, string userId)` → bool
- [ ] Implement `WhiteboardPresenceTracker` in `TeamWare.Web/Services/WhiteboardPresenceTracker.cs`
  - [ ] In-memory `ConcurrentDictionary` tracking connectionId → (whiteboardId, userId) and whiteboardId → set of userIds
  - [ ] Handle multiple connections per user (multiple tabs)
- [ ] Register `IWhiteboardPresenceTracker` as singleton in DI
- [ ] Integrate presence tracker with `WhiteboardHub.JoinBoard`, `LeaveBoard`, and `OnDisconnectedAsync`
- [ ] Write unit tests:
  - [ ] Adding and removing connections updates the active user list
  - [ ] Multiple connections from same user count as one active user
  - [ ] Removing last connection for a user removes them from active list
  - [ ] `GetActiveUsersAsync` returns correct list

---

## Phase 56: Whiteboard Session Page and Canvas

Create the session page, canvas rendering, and drawing tools.

### 56.1 Session Page Controller Actions

- [ ] Add `Session` action (GET) to `WhiteboardController`:
  - [ ] Accept `int id` parameter
  - [ ] Verify access via `CanAccessAsync` (WB-81, WB-82)
  - [ ] Handle edge cases: deleted board (EDGE-03), lost project membership (EDGE-04, WB-32)
  - [ ] Build `WhiteboardSessionViewModel` with board details, user role flags, available projects
  - [ ] Return session view
- [ ] Add `Delete` action (POST) to `WhiteboardController`:
  - [ ] Verify ownership or site admin (WB-66, WB-67)
  - [ ] Call `WhiteboardService.DeleteAsync`
  - [ ] Broadcast `BoardDeleted` via `IHubContext<WhiteboardHub>` to connected clients (WB-69)
  - [ ] Redirect to landing page
- [ ] Write tests:
  - [ ] Session page loads for authorized user
  - [ ] Session page returns 403 for unauthorized user
  - [ ] Deleted board invitation cleanup works (EDGE-03)
  - [ ] Delete succeeds for owner
  - [ ] Delete succeeds for site admin
  - [ ] Delete fails for non-owner non-admin

### 56.2 Session Page View

- [ ] Create `Views/Whiteboard/Session.cshtml`
  - [ ] Display owner and current presenter names prominently above the canvas (WB-71, UI-21)
  - [ ] Main area: canvas element with `data-whiteboard-id` attribute
  - [ ] Side panel: active user list at top, chat section below (WB-72, UI-22)
  - [ ] Tool palette below or beside the canvas with mode switcher (WB-43, UI-25)
  - [ ] "Save to Project" dropdown (visible to owner only) with project list and warning note (WB-63, UI-27)
  - [ ] "Invite Users" button (visible to owner only)
  - [ ] "Delete" button (visible to owner and site admin) with confirmation dialog (WB-68)
  - [ ] `data-whiteboard-id`, `data-is-owner`, `data-is-presenter` attributes for client-side JavaScript
  - [ ] Script references: `signalr.min.js`, `whiteboard.js`, `whiteboard-canvas.js`
  - [ ] Tailwind CSS 4 styling with light/dark theme support (UI-28)
  - [ ] Mobile responsive: canvas prioritized, side panel togglable (UI-29)
- [ ] Write rendering tests:
  - [ ] Owner sees invite, save-to-project, and delete controls
  - [ ] Non-owner viewer does not see owner-only controls
  - [ ] Presenter indicator is displayed correctly
  - [ ] Canvas element and data attributes are present

### 56.3 Canvas Client-Side Implementation

- [ ] Create `wwwroot/js/whiteboard-canvas.js`
  - [ ] Canvas rendering engine using HTML5 Canvas or SVG
  - [ ] Shape model: each element has `id`, `type`, `x`, `y`, `width`, `height`, `rotation`, `properties` (type-specific)
  - [ ] Supported standard shapes (WB-45): rectangles, circles/ellipses, text labels, lines, arrows
  - [ ] Supported specialized shapes (WB-46): servers, desktops, mobile devices, data (database/storage), switches, routers, firewalls, clouds
  - [ ] Connectors between shapes (WB-47): line/arrow connecting two shape endpoints
  - [ ] Freehand drawing (WB-48): pen tool recording point arrays
  - [ ] Mode switching: diagram mode and freehand mode as equal toggleable modes (WB-43, WB-44)
  - [ ] Shape selection, move, resize, delete (presenter only) (WB-49)
  - [ ] Canvas pan and zoom
  - [ ] Serialize canvas state to JSON for persistence and SignalR transmission
  - [ ] Deserialize canvas state from JSON for rendering received updates
  - [ ] Presenter-gated input: if `data-is-presenter` is false, disable all drawing/manipulation input (WB-25)
- [ ] Write tests:
  - [ ] Shape creation, selection, move, resize, delete
  - [ ] Canvas serialization/deserialization round-trip
  - [ ] Mode switching between diagram and freehand
  - [ ] Input disabled when not presenter

### 56.4 Canvas SignalR Integration

- [ ] Create `wwwroot/js/whiteboard.js`
  - [ ] On page load, read `data-whiteboard-id` from the page container
  - [ ] Build SignalR connection to `/hubs/whiteboard` with `withAutomaticReconnect()`
  - [ ] On connection start, invoke `hub.JoinBoard(whiteboardId)`
  - [ ] Register handler for `CanvasUpdated`: deserialize and render the updated canvas state
  - [ ] When presenter makes a canvas change, call `hub.SendCanvasUpdate(whiteboardId, canvasData)` (WB-50)
  - [ ] Implement debounce (200ms) for canvas updates to avoid flooding
  - [ ] Register handler for `PresenterChanged`: update presenter display, toggle drawing input (WB-16)
  - [ ] Register handler for `UserJoined` / `UserLeft`: update active user list in side panel (WB-70)
  - [ ] Register handler for `UserRemoved`: if current user was removed, navigate to landing page (WB-34)
  - [ ] Register handler for `BoardDeleted`: navigate to landing page
  - [ ] Handle reconnection: re-invoke `JoinBoard` on reconnect
  - [ ] Load initial canvas state from the server-rendered `CanvasData` on page load
- [ ] Add `SendCanvasUpdate` method to `WhiteboardHub`:
  - [ ] Verify caller is current presenter (WB-83)
  - [ ] Save canvas state via `WhiteboardService.SaveCanvasAsync` (WB-56)
  - [ ] Broadcast `CanvasUpdated` to group (excluding caller)
- [ ] Write tests:
  - [ ] SignalR connection established and `JoinBoard` invoked
  - [ ] Canvas updates are sent and received
  - [ ] Non-presenter cannot send canvas updates
  - [ ] Presenter change updates UI and drawing permissions

---

## Phase 57: Chat Sidebar

Add real-time chat to the whiteboard session page.

### 57.1 Chat Service

- [ ] Create `IWhiteboardChatService` interface in `TeamWare.Web/Services/IWhiteboardChatService.cs`
  - [ ] `SendMessageAsync(int whiteboardId, string userId, string content)` → `ServiceResult<WhiteboardChatMessageDto>` (WB-51 through WB-55)
  - [ ] `GetMessagesAsync(int whiteboardId, int page, int pageSize)` → `ServiceResult<List<WhiteboardChatMessageDto>>`
- [ ] Implement `WhiteboardChatService` in `TeamWare.Web/Services/WhiteboardChatService.cs`
  - [ ] `SendMessageAsync`: validate content length (max 4000, WB-54), create message, return DTO
  - [ ] `GetMessagesAsync`: load messages ordered by CreatedAt descending, paginated
- [ ] Register `IWhiteboardChatService` in DI
- [ ] Write unit tests for send and get methods

### 57.2 Chat Hub Methods

- [ ] Add `SendChatMessage` method to `WhiteboardHub` (WB-78):
  - [ ] Verify caller is connected to the whiteboard (WB-84)
  - [ ] Call `WhiteboardChatService.SendMessageAsync`
  - [ ] Broadcast `ChatMessageReceived` to group with message details (WB-53)
- [ ] Write tests:
  - [ ] Chat message is saved and broadcast
  - [ ] Message exceeding 4000 characters is rejected
  - [ ] Unauthorized user cannot send chat messages

### 57.3 Chat UI

- [ ] Add chat section to `Session.cshtml` side panel (UI-22):
  - [ ] Scrollable message list showing author display name and timestamp (WB-55)
  - [ ] Message input field at the bottom of the chat area
  - [ ] Auto-scroll to newest message
  - [ ] Load initial chat history from server on page load (paginated)
- [ ] Add chat handling to `whiteboard.js`:
  - [ ] Register `ChatMessageReceived` handler: append message to chat list, auto-scroll
  - [ ] On message submit: call `hub.SendChatMessage(whiteboardId, content)`, clear input
- [ ] Write tests:
  - [ ] Chat messages display with author and timestamp
  - [ ] New messages auto-scroll the chat area
  - [ ] Chat input clears after sending

---

## Phase 58: Invitations and Notifications

Implement the invitation flow including sending invitations, receiving notifications, and access enforcement.

### 58.1 Invitation Controller Actions

- [ ] Add `Invite` action (POST) to `WhiteboardController`:
  - [ ] Accept whiteboard ID and user ID(s) to invite
  - [ ] Verify caller is owner (WB-27, WB-85)
  - [ ] Call `WhiteboardInvitationService.InviteAsync` for each user
  - [ ] Return success/failure response (HTMX partial or JSON)
- [ ] Add `InviteForm` action (GET) to `WhiteboardController`:
  - [ ] Return partial view with user search/selection for inviting users
  - [ ] Exclude users already invited and the owner
- [ ] Write tests:
  - [ ] Owner can invite users
  - [ ] Non-owner cannot invite users
  - [ ] Duplicate invitation is handled gracefully
  - [ ] Invited user receives notification

### 58.2 Notification Integration

- [ ] Create `WhiteboardInvitation` notification handler using existing notification patterns
  - [ ] Notification links to the whiteboard session page
  - [ ] Notification text includes whiteboard title and inviter name
- [ ] Handle notification click:
  - [ ] If board still exists and user has access, navigate to session page
  - [ ] If board is deleted, clean up invitation and show "board no longer available" message (EDGE-03, WB-31)
  - [ ] If saved board and user lost project membership, clean up invitation and show "no longer have permission" (EDGE-04, WB-32)
- [ ] Write tests:
  - [ ] Notification is created when invitation is sent
  - [ ] Notification links to correct session page
  - [ ] Deleted board invitation cleanup on notification click
  - [ ] Lost project membership invitation cleanup on notification click

### 58.3 Invitation UI on Session Page

- [ ] Add invite user modal/dropdown to session page (owner only):
  - [ ] User search field to find users by name
  - [ ] "Invite" button per user result
  - [ ] Show already-invited users as disabled
  - [ ] HTMX for inline invite without page reload
- [ ] Add invited users list to session page sidebar (visible to owner):
  - [ ] Show invited users who are not currently connected
- [ ] Write tests:
  - [ ] Invite UI is visible only to owner
  - [ ] User search returns correct results
  - [ ] Invitation is sent and reflected in the UI

---

## Phase 59: Presenter Control

Implement presenter assignment, reclamation, and user removal.

### 59.1 Presenter Hub Methods

- [ ] Add `AssignPresenter` method to `WhiteboardHub` (WB-78):
  - [ ] Verify caller is owner (WB-86)
  - [ ] Verify target user is currently viewing the whiteboard via presence tracker (WB-15, WB-18)
  - [ ] Update `CurrentPresenterId` in database
  - [ ] Broadcast `PresenterChanged` to group (WB-16)
- [ ] Add `ReclaimPresenter` method to `WhiteboardHub` (WB-78):
  - [ ] Verify caller is owner (WB-86)
  - [ ] Update `CurrentPresenterId` to owner's user ID
  - [ ] Broadcast `PresenterChanged` to group (WB-17)
- [ ] Write tests:
  - [ ] Owner can assign presenter to active viewer
  - [ ] Owner cannot assign presenter to non-active user
  - [ ] Non-owner cannot assign presenter
  - [ ] Owner can reclaim presenter
  - [ ] Non-owner cannot reclaim presenter
  - [ ] `PresenterChanged` is broadcast after assignment/reclamation

### 59.2 User Removal Hub Method

- [ ] Add `RemoveUser` method to `WhiteboardHub` (WB-78):
  - [ ] Verify caller is owner (WB-87)
  - [ ] Verify board is temporary (WB-35)
  - [ ] Revoke invitation via `WhiteboardInvitationService.RevokeAsync` (WB-34)
  - [ ] Broadcast `UserRemoved` to the removed user's connections
  - [ ] Remove the user's connections from the SignalR group
  - [ ] If removed user was presenter, clear `CurrentPresenterId` and broadcast `PresenterChanged`
- [ ] Write tests:
  - [ ] Owner can remove user from temporary board
  - [ ] Owner cannot remove user from saved board (WB-35)
  - [ ] Non-owner cannot remove user
  - [ ] Removed user's invitation is revoked
  - [ ] Removed user receives `UserRemoved` event
  - [ ] If removed user was presenter, presenter is cleared

### 59.3 Presenter Context Menu UI

- [ ] Add context menu to active user list items in session page (UI-24):
  - [ ] "Make Presenter" option: visible to owner for any non-presenter active user (WB-19)
  - [ ] "Remove User" option: visible to owner on temporary boards only (WB-33, WB-35)
  - [ ] Alpine.js for context menu show/hide behavior
- [ ] Wire context menu actions to SignalR hub calls:
  - [ ] "Make Presenter" → `hub.AssignPresenter(whiteboardId, userId)`
  - [ ] "Remove User" → `hub.RemoveUser(whiteboardId, userId)`
- [ ] Update presenter display above board when `PresenterChanged` is received (UI-21)
- [ ] Write tests:
  - [ ] Context menu appears for owner
  - [ ] Context menu does not appear for non-owner
  - [ ] "Remove User" not shown on saved boards
  - [ ] Presenter change updates the prominent display

---

## Phase 60: Project Association and Saved Boards

Implement save-to-project, change project, clear project, and ownership transfer.

### 60.1 Project Association Controller Actions

- [ ] Add `SaveToProject` action (POST) to `WhiteboardController`:
  - [ ] Accept whiteboard ID and project ID
  - [ ] Verify caller is owner (WB-88)
  - [ ] Verify caller is a member of the target project
  - [ ] Call `WhiteboardProjectService.SaveToProjectAsync` (WB-60)
  - [ ] Return updated session page or HTMX partial
- [ ] Add `ClearProject` action (POST) to `WhiteboardController`:
  - [ ] Verify caller is owner (WB-88)
  - [ ] Call `WhiteboardProjectService.ClearProjectAsync` (WB-64)
  - [ ] Return updated session page or HTMX partial
- [ ] Write tests:
  - [ ] Owner can save board to project they belong to
  - [ ] Owner cannot save board to project they do not belong to
  - [ ] Non-owner cannot save board to project
  - [ ] Clearing project restores temporary status (EDGE-07)

### 60.2 Save-to-Project UI

- [ ] Add project selection dropdown to session page (owner only):
  - [ ] List projects the owner is a member of
  - [ ] Show current project association if saved
  - [ ] "Clear Project" button if currently saved (WB-64)
  - [ ] Warning note: "Only project members will be able to see this board" (WB-63, UI-27)
- [ ] Wire save/clear actions to controller endpoints
- [ ] Write tests:
  - [ ] Project dropdown is visible only to owner
  - [ ] Warning note is displayed
  - [ ] Save and clear actions work correctly

### 60.3 Ownership Transfer

- [ ] Integrate ownership transfer into project membership change hooks:
  - [ ] When a user is removed from a project, check if they own any whiteboards saved to that project
  - [ ] If so, call `WhiteboardProjectService.TransferOwnershipIfNeededAsync` (WB-11, EDGE-01)
  - [ ] Transfer ownership to the project owner
- [ ] Integrate project deletion cascade:
  - [ ] Verify that EF Core cascade delete configuration handles project deletion → whiteboard deletion (WB-65, EDGE-02)
- [ ] Write tests:
  - [ ] Ownership transfers when owner loses project membership (EDGE-01)
  - [ ] New owner is the project owner
  - [ ] Project deletion cascades to all associated whiteboards (EDGE-02)

---

## Phase 61: Whiteboard Polish and Hardening

Final phase: security review, edge cases, UI polish, accessibility, and documentation.

### 61.1 Security Hardening

- [ ] Verify all WhiteboardHub methods enforce authorization server-side (WB-80, WB-90, SEC-08)
- [ ] Verify all controller actions enforce authorization (WB-90)
- [ ] Verify canvas data is validated server-side to prevent injection (SEC-09)
- [ ] Verify chat message content is sanitized to prevent XSS (SEC-10)
- [ ] Verify invitation-gated access for temporary boards (WB-29, WB-81)
- [ ] Verify project membership access for saved boards (WB-62, WB-82)
- [ ] Verify site admin can access all boards (WB-39, WB-67, WB-89)

### 61.2 Edge Cases and Regression Testing

- [ ] Presenter disconnects: board remains active, no one can draw, owner must reclaim (EDGE-05, WB-73)
- [ ] Owner disconnects: board remains active, presenter retains status (EDGE-06)
- [ ] User follows invitation to deleted board: invitation cleaned up, user informed (EDGE-03, WB-31)
- [ ] User with invitation to saved board loses project membership: invitation cleaned up, access denied (EDGE-04, WB-32)
- [ ] Project association cleared: board returns to temporary, invitations remain (EDGE-07)
- [ ] Multiple browser tabs from same user: presence tracking handles correctly
- [ ] Rapid canvas updates: debounce prevents flooding
- [ ] Large canvas state: JSON serialization handles large payloads
- [ ] Concurrent presenter assignment: no race conditions

### 61.3 UI Consistency Review

- [ ] Verify Tailwind CSS 4 styling matches existing TeamWare conventions (UI-28)
- [ ] Verify light/dark theme support throughout all whiteboard views
- [ ] Verify mobile responsiveness: canvas prioritized, side panel togglable (UI-29)
- [ ] Verify the landing page layout is consistent with other list pages
- [ ] Verify confirmation dialogs follow existing patterns
- [ ] Verify toast notifications (if any) follow existing patterns

### 61.4 Performance Verification

- [ ] Verify canvas updates via SignalR have no perceptible delay under normal conditions (PERF-06)
- [ ] Verify landing page loads within 500ms (PERF-07)
- [ ] Profile canvas rendering with 50+ shapes
- [ ] Profile SignalR message throughput with 20 concurrent viewers

### 61.5 Documentation

- [ ] Update `WhiteBoardSpecification.md` with any deviations from the specification
- [ ] Document the client-side canvas architecture and shape model
- [ ] Document the WhiteboardHub API for future reference
- [ ] Note stale-board cleanup as a future Hangfire enhancement (out of scope)

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
