# TeamWare - Whiteboard Specification

## 1. Introduction

### 1.1 Purpose

This document provides the formal specification for the Whiteboard feature being added to TeamWare. It defines the functional requirements, data model additions, SignalR hub design, and UI/UX requirements needed to support a real-time, collaborative, diagram-focused whiteboard. This specification is a companion to the [main TeamWare specification](Specification.md) and follows the same conventions.

### 1.2 Scope

The Whiteboard feature introduces a lightweight, real-time, visual collaboration space to TeamWare. Users can create a whiteboard, invite others to join, and collaborate around a shared canvas. The interaction model is deliberately narrower than a full multi-editor whiteboard: many users may view a board at once, but only one person is the active presenter at any given time, and only the presenter can draw or manipulate the canvas.

The feature addresses the following gap:

1. **Visual Collaboration** — There is no shared, real-time visual space for sketching, diagramming, or walkthroughs. Task comments and lounge messages are text-based and do not support spatial or visual communication.

### 1.3 Definitions and Acronyms

| Term | Definition |
|------|-----------|
| Whiteboard | A shared, real-time canvas where users can draw shapes, diagrams, and freehand sketches |
| Owner | The user who created the whiteboard. The owner controls presenter assignment, invitations, and deletion |
| Presenter | The single user who currently has drawing and manipulation control over the canvas. Only one presenter exists at a time |
| Viewer | A user who is connected to the whiteboard and can see real-time updates but cannot draw or manipulate the canvas |
| Temporary Board | A whiteboard that has not been saved to a project. Accessible only to invited users and site admins |
| Saved Board | A whiteboard that has been associated with a project. Accessible to all members of that project and site admins |
| WhiteboardHub | A dedicated SignalR hub for real-time whiteboard collaboration, separate from existing hubs |
| Presenter Handoff | The act of the owner transferring presenter control to a currently viewing user |
| Canvas | The drawing surface of a whiteboard, containing shapes, connectors, text, and freehand strokes |

### 1.4 Design Principles

- Whiteboards are intentionally lightweight and quick to create. The creation flow is low-friction, requiring only a title.
- The single-presenter model avoids the complexity of simultaneous multi-user editing, conflict resolution, and operational transforms.
- Whiteboards are not tied to projects when created. Project association is optional and secondary, preserving the temporary-room metaphor.
- Invitations serve as access controls for temporary boards, not just notifications. A user must be invited to access a temporary board.
- The server-rendered MVC + HTMX approach is maintained for page structure. SignalR handles real-time canvas synchronization, presence, and chat delivery.
- The canvas is diagram-focused with freehand drawing as an equal mode. The tool palette emphasizes structured diagramming shapes alongside a pen tool.
- Autosave preserves the latest canvas state for all boards. There is no version history; only the current state is stored.

---

## 2. Technology Additions

| Layer | Technology | Purpose |
|-------|-----------|---------|
| Real-Time Collaboration | ASP.NET Core SignalR (WhiteboardHub) | Real-time canvas synchronization, presence tracking, presenter control, and chat delivery |
| Canvas Rendering | HTML5 Canvas or SVG | Client-side rendering of shapes, connectors, text, and freehand strokes |

All other technology choices remain unchanged from the [main specification](Specification.md).

---

## 3. Functional Requirements

### 3.1 Whiteboard Creation

| ID | Requirement |
|----|------------|
| WB-01 | Any authenticated user shall be able to create a whiteboard from the top-level Whiteboards area |
| WB-02 | The create flow shall require only a title |
| WB-03 | The creator shall become the owner and initial presenter of the new whiteboard |
| WB-04 | A newly created whiteboard shall be a temporary board with no project association |

### 3.2 Ownership

| ID | Requirement |
|----|------------|
| WB-05 | Each whiteboard shall have exactly one owner |
| WB-06 | The owner shall be the only user who can invite other users to the whiteboard |
| WB-07 | The owner shall be the only user who can assign presenter control to a currently viewing user |
| WB-08 | The owner shall be able to reclaim presenter control at any time |
| WB-09 | The owner shall be the only non-admin user who can delete the whiteboard |
| WB-10 | The owner shall be the only user who can save the whiteboard to a project or change its project association |
| WB-11 | If the owner of a saved board loses membership in the associated project, ownership shall transfer to the owner of that project |

### 3.3 Presenter Model

| ID | Requirement |
|----|------------|
| WB-12 | Each whiteboard shall have exactly one presenter at a time |
| WB-13 | The presenter defaults to the owner when the whiteboard is created |
| WB-14 | Only the current presenter shall be able to draw, create, move, resize, edit, or delete elements on the canvas |
| WB-15 | The owner shall be able to assign presenter control to any user who is currently viewing the whiteboard |
| WB-16 | Presenter assignment shall be immediate with no confirmation or decline step |
| WB-17 | The owner shall be able to take presenter control back from the current presenter at any time |
| WB-18 | The presenter must be actively viewing the whiteboard in order to present. If the presenter disconnects, the board remains active for other viewers but no one can draw until the owner reassigns presenter control or the presenter reconnects |
| WB-19 | Presenter assignment shall be performed through a context menu on the active user in the side panel |

### 3.4 Viewers

| ID | Requirement |
|----|------------|
| WB-20 | Multiple users may view the whiteboard simultaneously |
| WB-21 | Viewers shall see real-time canvas updates from the presenter |
| WB-22 | Viewers shall see who is currently presenting |
| WB-23 | Viewers shall see who else is currently viewing the whiteboard |
| WB-24 | Viewers shall be able to send and receive chat messages in the sidebar |
| WB-25 | Viewers shall not be able to draw, create, move, resize, edit, or delete elements on the canvas |

### 3.5 Invitations

| ID | Requirement |
|----|------------|
| WB-26 | The owner shall be able to invite one or more users to the whiteboard |
| WB-27 | Only the owner shall be able to send invitations |
| WB-28 | Invited users shall receive a notification with a link to join the whiteboard, using the existing notification system |
| WB-29 | Invitations shall serve as access controls for temporary boards. A user must have an invitation to access a temporary board unless they are a site admin |
| WB-30 | Invitations shall remain active until the board is deleted or the invitation is revoked |
| WB-31 | If a user follows an invitation link to a deleted whiteboard, the invitation shall be removed and the user shall be informed that the board is no longer available |
| WB-32 | If a user has an invitation to a saved board but is no longer a member of the associated project, the user shall be told they no longer have permission and the invitation shall be deleted |

### 3.6 User Removal

| ID | Requirement |
|----|------------|
| WB-33 | On temporary boards, the owner shall be able to remove a currently viewing user via a context menu in the active user list |
| WB-34 | Removing a user shall revoke their invitation and return them to the whiteboard landing page |
| WB-35 | The "Remove User" action shall only be available on temporary boards, not saved boards |

### 3.7 Visibility and Discovery

| ID | Requirement |
|----|------------|
| WB-36 | The system shall provide a top-level Whiteboards landing page accessible from the main navigation |
| WB-37 | The landing page shall show temporary boards the user has been invited to |
| WB-38 | The landing page shall show saved boards the user can access through project membership |
| WB-39 | Site admins shall be able to see all whiteboards on the landing page |
| WB-40 | The landing page shall emphasize active boards the user is invited to first, then recent boards |
| WB-41 | Temporary boards shall display the name of the user who created them |
| WB-42 | Saved and temporary boards shall not be displayed in separate sections in the initial version |

### 3.8 Canvas and Drawing Tools

| ID | Requirement |
|----|------------|
| WB-43 | The whiteboard canvas shall support two modes that the user can explicitly switch between: diagram mode and freehand drawing mode |
| WB-44 | Diagram mode and freehand drawing mode shall be treated as equal modes in the tool palette |
| WB-45 | Diagram mode shall support the following standard shapes: rectangles/boxes, circles/ellipses, text labels, lines, and arrows |
| WB-46 | Diagram mode shall support the following specialized shapes: servers, desktops, mobile devices, data (database/storage), switches, routers, firewalls, and clouds |
| WB-47 | Diagram mode shall support connectors between shapes |
| WB-48 | Freehand drawing mode shall allow the presenter to draw freely on the canvas with a pen tool |
| WB-49 | Only the current presenter shall be able to create, move, resize, edit, or delete any element on the canvas, including editing existing shapes placed by a previous presenter |
| WB-50 | Canvas changes made by the presenter shall be broadcast to all connected viewers in real time via SignalR |

### 3.9 Chat Sidebar

| ID | Requirement |
|----|------------|
| WB-51 | Each whiteboard shall include a text chat sidebar |
| WB-52 | All users connected to the whiteboard (owner, presenter, and viewers) shall be able to send and receive chat messages |
| WB-53 | Chat messages shall be delivered to all connected users in real time via SignalR |
| WB-54 | Chat message content shall have a maximum length of 4000 characters |
| WB-55 | Chat messages shall display the author's display name and timestamp |

### 3.10 Persistence and Autosave

| ID | Requirement |
|----|------------|
| WB-56 | All whiteboards shall autosave their canvas state while in use |
| WB-57 | Only the latest canvas state shall be stored. There shall be no version history |
| WB-58 | Temporary boards shall persist until explicitly deleted by the owner or a site admin |
| WB-59 | Saved boards shall display the last updated time in the user's local time |

### 3.11 Project Association

| ID | Requirement |
|----|------------|
| WB-60 | The owner may optionally save a whiteboard by associating it with exactly one project |
| WB-61 | Only the owner shall be able to set, change, or clear the project association |
| WB-62 | When a whiteboard is saved to a project, only members of that project and site admins shall be able to access it. Project membership overrides invitations |
| WB-63 | When a whiteboard is saved to a project, the UI shall display a clear note that only project members will be able to see the board |
| WB-64 | Clearing a project association shall return the whiteboard to temporary status, restoring invite-gated access |
| WB-65 | If a project is deleted, all whiteboards associated with that project shall also be deleted |

### 3.12 Deletion

| ID | Requirement |
|----|------------|
| WB-66 | The owner shall be able to delete a whiteboard |
| WB-67 | Site admins shall be able to delete any whiteboard |
| WB-68 | Deletion shall be immediate after a standard confirmation dialog |
| WB-69 | Deletion shall cascade to all associated data: canvas state, chat messages, invitations, and presence records |

### 3.13 Presence and Real-Time Behavior

| ID | Requirement |
|----|------------|
| WB-70 | The whiteboard shall maintain a real-time list of currently connected users |
| WB-71 | The owner and current presenter shall be displayed prominently above the board |
| WB-72 | The side panel shall display the list of active users (without badges), with chat below the user list |
| WB-73 | If the presenter disconnects, the board shall remain active for other connected users. Presenter control does not automatically return to the owner; the owner must manually reclaim or reassign it |
| WB-74 | SignalR shall be used as the real-time transport for all whiteboard collaboration features |

### 3.14 SignalR Hub

| ID | Requirement |
|----|------------|
| WB-75 | A dedicated `WhiteboardHub` shall be mapped at `/hubs/whiteboard`, separate from existing hubs |
| WB-76 | The WhiteboardHub connection shall be established when a user navigates to a whiteboard session |
| WB-77 | Each whiteboard shall map to a SignalR group: `whiteboard-{whiteboardId}` |
| WB-78 | The hub shall support client-to-server methods: `JoinBoard`, `LeaveBoard`, `SendCanvasUpdate`, `SendChatMessage`, `AssignPresenter`, `ReclaimPresenter`, `RemoveUser` |
| WB-79 | The hub shall support server-to-client methods: `CanvasUpdated`, `ChatMessageReceived`, `PresenterChanged`, `UserJoined`, `UserLeft`, `UserRemoved`, `BoardDeleted` |
| WB-80 | All hub methods shall enforce authorization checks server-side (invitation or project membership as appropriate) |

### 3.15 Authorization Summary

| ID | Requirement |
|----|------------|
| WB-81 | Accessing a temporary whiteboard shall require an active invitation or site admin status |
| WB-82 | Accessing a saved whiteboard shall require membership in the associated project or site admin status |
| WB-83 | Drawing and manipulating the canvas shall require being the current presenter |
| WB-84 | Sending chat messages shall require being connected to the whiteboard (any role) |
| WB-85 | Inviting users shall require being the whiteboard owner |
| WB-86 | Assigning and reclaiming presenter control shall require being the whiteboard owner |
| WB-87 | Removing a user (temporary boards only) shall require being the whiteboard owner |
| WB-88 | Saving to a project, changing project association, or clearing project association shall require being the whiteboard owner |
| WB-89 | Deleting a whiteboard shall require being the whiteboard owner or a site admin |
| WB-90 | Authorization checks shall be enforced both in the WhiteboardHub and in MVC controller actions |

---

## 4. Data Model

### 4.1 New Entities

#### Whiteboard

| Field | Type | Constraints |
|-------|------|------------|
| Id | int | Primary key, auto-increment |
| Title | string | Required, max 200 characters |
| OwnerId | string | Foreign key to ApplicationUser; required |
| ProjectId | int? | Foreign key to Project; nullable (null means temporary board) |
| CurrentPresenterId | string? | Foreign key to ApplicationUser; nullable (null when no presenter is connected) |
| CanvasData | string? | Nullable; JSON-serialized canvas state |
| CreatedAt | datetime | Required, default UTC now |
| UpdatedAt | datetime | Required, updated on every canvas save |

**Indexes:**
- `IX_Whiteboard_OwnerId` (OwnerId) — For listing boards by owner
- `IX_Whiteboard_ProjectId` (ProjectId) — For listing boards by project and cascade delete queries

#### WhiteboardInvitation

| Field | Type | Constraints |
|-------|------|------------|
| Id | int | Primary key, auto-increment |
| WhiteboardId | int | Foreign key to Whiteboard; required |
| UserId | string | Foreign key to ApplicationUser; required |
| InvitedByUserId | string | Foreign key to ApplicationUser; required (always the owner) |
| CreatedAt | datetime | Required, default UTC now |

**Indexes:**
- `IX_WhiteboardInvitation_WhiteboardId` (WhiteboardId) — For listing invitations per board
- `IX_WhiteboardInvitation_UserId` (UserId) — For listing boards a user is invited to
- `IX_WhiteboardInvitation_WhiteboardId_UserId` (WhiteboardId, UserId) — Unique constraint; one invitation per user per board

#### WhiteboardChatMessage

| Field | Type | Constraints |
|-------|------|------------|
| Id | int | Primary key, auto-increment |
| WhiteboardId | int | Foreign key to Whiteboard; required |
| UserId | string | Foreign key to ApplicationUser; required (message author) |
| Content | string | Required, max 4000 characters |
| CreatedAt | datetime | Required, default UTC now |

**Indexes:**
- `IX_WhiteboardChatMessage_WhiteboardId_CreatedAt` (WhiteboardId, CreatedAt) — For loading chat history

### 4.2 Modified Entities

#### Notification

The existing Notification entity's `Type` enum adds the following value:

| New Type Value | Description |
|----------------|------------|
| `WhiteboardInvitation` | Generated when a user is invited to join a whiteboard |

The existing `ReferenceId` field shall reference the `Whiteboard.Id` for whiteboard invitation notifications.

#### Project

The existing Project entity adds the following navigation property:

| Property | Type | Description |
|----------|------|------------|
| `Whiteboards` | `ICollection<Whiteboard>` | All whiteboards saved to this project |

When a Project is deleted, all associated Whiteboards shall be cascade deleted (WB-65).

#### ApplicationUser

The existing ApplicationUser entity adds the following navigation properties:

| Property | Type | Description |
|----------|------|------------|
| `OwnedWhiteboards` | `ICollection<Whiteboard>` | Whiteboards owned by this user |
| `WhiteboardInvitations` | `ICollection<WhiteboardInvitation>` | Whiteboard invitations received by this user |

### 4.3 New Entity Relationships

- A **Project** has many **Whiteboards** (nullable FK; null means temporary board). Cascade delete.
- A **User** has many **Whiteboards** as owner (via OwnerId).
- A **User** may be the current presenter of zero or one **Whiteboards** (via CurrentPresenterId).
- A **Whiteboard** has many **WhiteboardInvitations**. Cascade delete.
- A **Whiteboard** has many **WhiteboardChatMessages**. Cascade delete.
- A **User** has many **WhiteboardInvitations**.
- A **User** has many **WhiteboardChatMessages** (as author).

---

## 5. SignalR Hub Design

### 5.1 WhiteboardHub

A dedicated `WhiteboardHub` is mapped at `/hubs/whiteboard`, separate from existing hubs. The hub requires authentication.

#### Server-to-Client Methods

| Method | Payload | Description |
|--------|---------|-------------|
| `CanvasUpdated` | `{ whiteboardId, canvasData, presenterId }` | The presenter has made changes to the canvas. Contains the updated canvas state or incremental delta |
| `ChatMessageReceived` | `{ whiteboardId, messageId, userId, displayName, content, createdAt }` | A new chat message was posted |
| `PresenterChanged` | `{ whiteboardId, newPresenterId, newPresenterDisplayName }` | Presenter control has been assigned to a different user |
| `UserJoined` | `{ whiteboardId, userId, displayName }` | A user has connected to the whiteboard |
| `UserLeft` | `{ whiteboardId, userId, displayName }` | A user has disconnected from the whiteboard |
| `UserRemoved` | `{ whiteboardId, userId }` | A user has been removed from the whiteboard by the owner. The removed user's client uses this to navigate away |
| `BoardDeleted` | `{ whiteboardId }` | The whiteboard has been deleted. All connected clients navigate away |

#### Client-to-Server Methods

| Method | Parameters | Description |
|--------|-----------|-------------|
| `JoinBoard` | `whiteboardId` | Subscribe to updates for a whiteboard; adds connection to SignalR group. Validates invitation or project membership |
| `LeaveBoard` | `whiteboardId` | Unsubscribe from a whiteboard; removes connection from SignalR group |
| `SendCanvasUpdate` | `whiteboardId`, `canvasData` | Broadcast a canvas update to all viewers. Requires current presenter status |
| `SendChatMessage` | `whiteboardId`, `content` | Post a chat message to the whiteboard's sidebar |
| `AssignPresenter` | `whiteboardId`, `userId` | Assign presenter control to a currently viewing user. Requires owner status |
| `ReclaimPresenter` | `whiteboardId` | Owner reclaims presenter control. Requires owner status |
| `RemoveUser` | `whiteboardId`, `userId` | Remove a user from a temporary whiteboard. Requires owner status. Revokes invitation |

### 5.2 SignalR Groups

| Context | Group Name |
|---------|-----------|
| Whiteboard Session | `whiteboard-{whiteboardId}` |

When a user calls `JoinBoard`, the hub validates authorization (invitation for temporary boards, project membership for saved boards, or site admin) and adds the connection to the corresponding group.

---

## 6. Changes to Existing Requirements

The following existing requirements from the [main specification](Specification.md) are affected by this work:

| Requirement | Change |
|-------------|--------|
| NOTIF-01 through NOTIF-05 | Extended. A new notification type `WhiteboardInvitation` is added for whiteboard invitations. |

---

## 7. Non-Functional Requirements

The following non-functional requirements from the main specification apply with these additional considerations:

| ID | Requirement |
|----|------------|
| PERF-06 | Real-time canvas updates via SignalR shall not introduce perceptible delay under normal operating conditions (fewer than 20 concurrent whiteboard users per board) |
| PERF-07 | The whiteboard landing page shall load within 500 milliseconds under normal operating conditions |
| SEC-08 | All SignalR hub methods shall enforce authorization checks server-side before processing |
| SEC-09 | Canvas data shall be validated server-side to prevent injection of malicious content |
| SEC-10 | Chat message content shall be sanitized to prevent XSS |
| TEST-13 | All whiteboard service methods shall have unit tests |
| TEST-14 | The WhiteboardHub shall have integration tests verifying authorization, canvas synchronization, presenter control, and group management |
| TEST-15 | The whiteboard MVC controllers shall have integration tests verifying creation, invitation, project association, deletion, and access control |
| TEST-16 | The whiteboard invitation notification flow shall have integration tests |

---

## 8. UI/UX Requirements

| ID | Requirement |
|----|------------|
| UI-18 | The main navigation shall include a top-level "Whiteboards" link visible to all authenticated users |
| UI-19 | The Whiteboards landing page shall display boards the user can access, emphasizing active invited boards first, then recent boards |
| UI-20 | Each board entry on the landing page shall show the title, creator name (for temporary boards), project name (for saved boards), and last updated time |
| UI-21 | The whiteboard session page shall display the owner and current presenter names prominently above the canvas |
| UI-22 | The whiteboard session page shall include a side panel with the active user list at the top and chat below |
| UI-23 | The active user list shall not include badges. Owner and presenter identity shall be communicated through the prominent display above the board |
| UI-24 | The owner shall be able to right-click or use a context menu on an active user in the side panel to access "Make Presenter" and (on temporary boards) "Remove User" actions |
| UI-25 | The tool palette shall present diagram mode and freehand drawing mode as equal, explicitly switchable modes |
| UI-26 | The diagram tool palette shall include standard shapes (rectangles, circles, text, lines, arrows) and specialized shapes (servers, desktops, mobile devices, data, switches, routers, firewalls, clouds) |
| UI-27 | The "Save to Project" action shall display a clear note that only project members will be able to see the board after saving |
| UI-28 | The whiteboard UI shall follow existing TeamWare styling conventions (Tailwind CSS 4, light/dark theme support) |
| UI-29 | On mobile viewports, the whiteboard should prioritize the canvas view with the side panel accessible via a toggle |

---

## 9. Edge Cases

The following edge cases shall be handled:

| ID | Scenario | Behavior |
|----|----------|----------|
| EDGE-01 | Owner of a saved board loses project membership | Ownership transfers to the owner of the associated project |
| EDGE-02 | Project is deleted | All whiteboards associated with that project are deleted, including cascading to invitations, chat messages, and canvas data |
| EDGE-03 | User follows invitation link to a deleted whiteboard | Invitation is removed and user is shown a message that the board is no longer available |
| EDGE-04 | User has invitation to a saved board but is no longer a project member | User is told they no longer have permission and the invitation is deleted |
| EDGE-05 | Presenter disconnects | Board remains active for viewers. No one can draw. Owner must manually reclaim or reassign presenter control |
| EDGE-06 | Owner disconnects | Board remains active. Presenter (if different from owner) retains presenter status. Owner can reconnect and resume control |
| EDGE-07 | Project association is cleared on a saved board | Board returns to temporary status. Invite-gated access is restored. Existing invitations remain active |

---

## 10. Future Considerations

The following features are out of scope for this release but may be considered for future iterations:

- **Stale board cleanup** — A Hangfire recurring job to automatically delete temporary boards that have not been accessed for a configurable period
- **Lightweight reactions or pointer pings** — Allowing viewers to react or ping a location on the canvas without drawing
- **Request presenter control** — Allowing viewers to request control through the UI rather than asking through chat
- **Export to image** — Exporting the current canvas state as a PNG or SVG file
- **Templates** — Pre-built diagram templates for common architecture patterns
- **Multiple presenters** — Allowing more than one user to draw simultaneously
- **Version history** — Storing and browsing previous canvas states
- **Deep integrations** — Linking whiteboards to tasks, comments, or lounge messages

---

## 11. References

- [Main TeamWare Specification](Specification.md)
- [Social Features Specification](SocialFeaturesSpecification.md)
- [Project Lounge Specification](ProjectLoungeSpecification.md)
- [Whiteboard Idea Document](WhiteBoardIdea.md)
