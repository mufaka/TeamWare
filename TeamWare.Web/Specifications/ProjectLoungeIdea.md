# TeamWare - Project Lounge Idea Document

## Overview

The Project Lounge brings real-time, persistent chat to TeamWare. It gives team members a place to have quick conversations, share updates, and collaborate without leaving the application. The feature draws from classic groupware chat rooms (Lotus Notes rooms, IRC channels, FirstClass conferences) reimagined for a modern, lightweight, self-hosted context.

## Goals

- Provide a low-friction communication channel within projects that complements task comments
- Leverage the existing SignalR infrastructure (PresenceHub) to deliver real-time messaging
- Keep the implementation lightweight and consistent with TeamWare's "small team, self-hosted" philosophy
- Maintain the server-rendered MVC + HTMX approach for page structure while using SignalR for real-time message delivery

## Non-Goals

- Full-featured Slack/Teams replacement (threads, rich embeds, bots, integrations)
- End-to-end encryption
- File upload within chat (defer to a future File Sharing feature)
- Voice or video calling
- Typing indicators
- Full-text message search (defer to a later iteration)

---

## Concepts

### Rooms

Each project automatically gets a chat room (the "Project Lounge"). There is also a single global room ("#general") available to all authenticated users for cross-project conversation.

| Room Type | Scope | Access |
|-----------|-------|--------|
| Project Lounge | One per project | Project members only |
| #general | Site-wide | All authenticated users |

Rooms are not user-created. They exist implicitly as part of a project or as the single global room. This keeps the feature simple and avoids room management overhead.

### Messages

A message is a short text post by an authenticated user within a room.

Key attributes:
- **Content** — Markdown-formatted text (bold, italic, code, links). Rendered server-side with XSS sanitization, consistent with existing Markdown support elsewhere in TeamWare
- **Author** — The user who posted the message
- **Timestamp** — UTC datetime of when the message was sent
- **Room** — Which project lounge or global room the message belongs to
- **Mentions** — Optional `@username` references parsed from content
- **Edited** — Boolean flag indicating the message was edited after posting (no edit history is kept)
- **Maximum length** — 4000 characters

### Mentions

Users can `@mention` other room members. Mentions should:
- Autocomplete from the room's member list (project members for project rooms, all users for #general)
- Generate an in-app notification using the existing notification system
- Visually highlight the mentioned username in the rendered message

### Pinned Messages

Project owners and admins can pin a message to the top of a room. Pinned messages act as lightweight announcements or reference points. Only one or a small number of messages can be pinned at a time.

### Emoji Reactions

Users can add lightweight emoji reactions to messages (e.g., thumbs up, heart, laugh, rocket, eyes). Each user can add one reaction of each type per message. Reactions are displayed as small counts beneath the message. Clicking an existing reaction toggles the current user's participation.

A fixed set of reaction types keeps the UI simple and avoids the complexity of a full emoji picker:

| Reaction | Emoji | Code |
|----------|-------|------|
| Thumbs Up | +1 | `thumbsup` |
| Heart | heart | `heart` |
| Laugh | laugh | `laugh` |
| Rocket | rocket | `rocket` |
| Eyes | eyes | `eyes` |

### Unread Tracking

The system tracks the last-read message per user per room. This enables:
- **Unread count badges** in the sidebar next to each project lounge and #general
- **"New messages" divider** in the message list showing where unread messages begin
- **Auto-mark as read** when the user scrolls to the bottom of the room

The read position is updated when the user views a room and scrolls to the latest messages. This avoids marking messages as read when the user has only loaded older history.

### Message-to-Task Conversion

In project rooms, users can convert a lounge message into a task for that project. This provides a quick path from conversation to action:

- A "Create Task" action appears on each message in a project room (not available in #general since it has no associated project)
- Clicking it opens a pre-filled task creation form with:
  - **Title** pre-populated from the first line or first 200 characters of the message content
  - **Description** pre-populated with the full message content, prefixed with a reference to the lounge message (e.g., "From lounge message by @User on DATE")
  - **Project** locked to the current project
  - All other task fields (status, priority, assignee, due date) left at defaults for the user to fill in
- After task creation, a system note is appended to the original lounge message indicating a task was created (e.g., "Task #123 created from this message")
- Authorization: any project member can create a task from a message

### Message Retention

Lounge messages older than 30 days are automatically deleted. A Hangfire recurring job runs once per day to perform the cleanup. This keeps the SQLite database size manageable for self-hosted deployments.

- Pinned messages are exempt from retention cleanup
- The 30-day retention period is a fixed default for V1
- Associated reactions and read positions for deleted messages are cleaned up as well

---

## Data Model (Draft)

### LoungeMessage Entity

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| Id | int | PK, auto-increment | |
| ProjectId | int? | FK to Project, nullable | Null for #general room |
| UserId | string | FK to ApplicationUser, required | Message author |
| Content | string | Required, max 4000 chars | Markdown-formatted text |
| CreatedAt | DateTime | Required, default UTC now | |
| IsEdited | bool | Default false | Set to true when content is updated |
| EditedAt | DateTime? | Nullable | Timestamp of last edit |
| IsPinned | bool | Default false | |
| PinnedByUserId | string? | FK to ApplicationUser, nullable | Who pinned it |
| PinnedAt | DateTime? | Nullable | When it was pinned |
| CreatedTaskId | int? | FK to TaskItem, nullable | Task created from this message |

**Indexes:**
- `IX_LoungeMessage_ProjectId_CreatedAt` (ProjectId, CreatedAt) — For loading room history and retention cleanup
- `IX_LoungeMessage_UserId` — For user activity queries

**Relationships:**
- `LoungeMessage.ProjectId` → `Project.Id` (nullable; null means #general)
- `LoungeMessage.UserId` → `ApplicationUser.Id`
- `LoungeMessage.PinnedByUserId` → `ApplicationUser.Id`
- `LoungeMessage.CreatedTaskId` → `TaskItem.Id` (nullable; set when message is converted to a task)

### LoungeReaction Entity

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| Id | int | PK, auto-increment | |
| LoungeMessageId | int | FK to LoungeMessage, required | |
| UserId | string | FK to ApplicationUser, required | |
| ReactionType | string | Required, max 20 chars | One of: `thumbsup`, `heart`, `laugh`, `rocket`, `eyes` |
| CreatedAt | DateTime | Required, default UTC now | |

**Indexes:**
- `IX_LoungeReaction_MessageId` (LoungeMessageId) — For loading reactions with messages
- `IX_LoungeReaction_MessageId_UserId_Type` (LoungeMessageId, UserId, ReactionType) — Unique constraint, one reaction per type per user per message

**Relationships:**
- `LoungeReaction.LoungeMessageId` → `LoungeMessage.Id` (cascade delete)
- `LoungeReaction.UserId` → `ApplicationUser.Id`

### LoungeReadPosition Entity

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| Id | int | PK, auto-increment | |
| UserId | string | FK to ApplicationUser, required | |
| ProjectId | int? | FK to Project, nullable | Null for #general room |
| LastReadMessageId | int | FK to LoungeMessage, required | The most recent message the user has seen |
| UpdatedAt | DateTime | Required | When the position was last updated |

**Indexes:**
- `IX_LoungeReadPosition_UserId_ProjectId` (UserId, ProjectId) — Unique constraint, one position per user per room

**Relationships:**
- `LoungeReadPosition.UserId` → `ApplicationUser.Id`
- `LoungeReadPosition.ProjectId` → `Project.Id` (nullable; null means #general)
- `LoungeReadPosition.LastReadMessageId` → `LoungeMessage.Id`

### Navigation Properties

Add to `Project`:
```
public ICollection<LoungeMessage> LoungeMessages { get; set; }
```

Add to `ApplicationUser`:
```
public ICollection<LoungeMessage> LoungeMessages { get; set; }
```

Add to `LoungeMessage`:
```
public ICollection<LoungeReaction> Reactions { get; set; }
public TaskItem? CreatedTask { get; set; }
```

---

## SignalR Design

### Separate LoungeHub

A dedicated `LoungeHub` is mapped at `/hubs/lounge`, separate from the existing `PresenceHub`. This follows the single-responsibility principle and allows the lounge connection to be established only when a user navigates to a lounge view. The `PresenceHub` connection continues to handle online/offline status independently.

**Hub Methods (Server to Client):**

| Method | Payload | Description |
|--------|---------|-------------|
| `ReceiveMessage` | `{ messageId, projectId, userId, displayName, avatarUrl, content, createdAt }` | New message posted to a room the client is viewing |
| `MessageEdited` | `{ messageId, content, editedAt }` | A message's content was updated |
| `MessageDeleted` | `{ messageId }` | A message was deleted |
| `MessagePinned` | `{ messageId, pinnedByDisplayName }` | A message was pinned |
| `MessageUnpinned` | `{ messageId }` | A message was unpinned |
| `ReactionUpdated` | `{ messageId, reactionType, count, userReacted }` | A reaction was added or removed |
| `TaskCreatedFromMessage` | `{ messageId, taskId, taskTitle }` | A task was created from a lounge message |

**Hub Methods (Client to Server):**

| Method | Parameters | Description |
|--------|-----------|-------------|
| `JoinRoom` | `projectId` (null for #general) | Subscribe to messages for a room |
| `LeaveRoom` | `projectId` (null for #general) | Unsubscribe from a room |
| `SendMessage` | `projectId`, `content` | Post a message to a room |
| `EditMessage` | `messageId`, `content` | Update a message's content (author only) |
| `DeleteMessage` | `messageId` | Delete a message |
| `ToggleReaction` | `messageId`, `reactionType` | Add or remove a reaction |
| `MarkAsRead` | `projectId`, `lastReadMessageId` | Update the user's read position for a room |

### SignalR Groups

Each room maps to a SignalR group:
- Project room: group name `"lounge-project-{projectId}"`
- Global room: group name `"lounge-general"`

When a user calls `JoinRoom`, the hub adds their connection to the corresponding group. Authorization is checked server-side (project membership for project rooms, authenticated for #general).

---

## UI / UX Design

### Entry Points

1. **Project sidebar or project detail page** — A "Lounge" link/tab for each project the user is a member of
2. **Main sidebar** — A "#general" link under a "Lounge" section, always visible to authenticated users
3. **Dashboard widget** (optional) — Show recent lounge activity or unread indicator

### Room View Layout

```
+----------------------------------------------+
| Room Header: "Project X Lounge"  [Pin icon]  |
+----------------------------------------------+
| [Pinned message banner, if any]              |
+----------------------------------------------+
|                                               |
|  [Avatar] User A  10:32 AM                   |
|  Hey, has anyone looked at the API docs?      |
|                                               |
|  [Avatar] User B  10:34 AM                   |
|  Yes, I updated the wiki page yesterday.     |
|                                               |
|  [Avatar] User A  10:35 AM                   |
|  Perfect, thanks @UserB!                      |
|                                               |
|  ... (scrollable message area) ...            |
|                                               |
+----------------------------------------------+
| [Message input field]           [Send button] |
+----------------------------------------------+
```

### Interaction Patterns

- **Message list** loads via standard MVC controller action (server-rendered HTML via HTMX partial)
- **New messages** arrive in real-time via SignalR and are appended to the message list with JavaScript
- **Send message** can use either SignalR (`SendMessage` hub method) or an HTMX POST to a controller action. The SignalR approach is simpler for real-time; the controller approach provides better error handling and validation consistency
- **Scroll behavior** — Auto-scroll to bottom on new messages if the user is already at the bottom; show a "new messages" indicator if they've scrolled up
- **Load more** — Paginated history loaded on demand (scroll up or "Load older messages" button) via HTMX

### Responsive Considerations

- On mobile, the lounge view should be full-width
- The message input should remain fixed at the bottom of the viewport
- Consider a compact message format for smaller screens (inline avatar + name + message)

---

## Authorization

| Action | Project Room | #general |
|--------|-------------|----------|
| View messages | Project members only | All authenticated users |
| Send message | Project members only | All authenticated users |
| Edit message | Message author only | Message author only |
| Delete message | Message author, Project Owner/Admin | Message author, Site Admin |
| Pin/unpin message | Project Owner or Admin | Site Admin only |
| Add/remove reaction | Project members only | All authenticated users |
| Create task from message | Project members only | N/A (no associated project) |

Authorization checks must happen both:
1. In the `LoungeHub` when processing hub method calls
2. In the MVC controller when serving room views, history, and task creation

---

## Notification Integration

- `@mention` in a lounge message creates a notification of type `"LoungeMention"`
- The notification links directly to the room, ideally scrolled to the relevant message
- Notifications use the existing `INotificationService` and notification bell/count infrastructure

---

## Decisions

The following questions have been resolved:

| # | Topic | Decision |
|---|-------|----------|
| 1 | Message editing/deletion | **Both supported.** Users can edit and delete their own messages. No edit history is kept; edited messages display an "edited" indicator with timestamp. Project owners/admins (or site admins in #general) can also delete any message. |
| 2 | Message formatting | **Markdown for V1.** Consistent with existing Markdown support elsewhere in TeamWare. Rendered server-side with XSS sanitization. |
| 3 | Message length limit | **4000 characters.** |
| 4 | Typing indicators | **Not included.** Deferred to avoid unnecessary SignalR traffic. |
| 5 | Unread tracking | **Included in V1.** A `LoungeReadPosition` table tracks the last-read message per user per room. Enables unread count badges and a "new messages" divider in the message list. |
| 6 | Message search | **Deferred.** May require SQLite FTS5 support; revisit in a later iteration. |
| 7 | Emoji reactions | **Included in V1.** Fixed set of 5 reaction types (thumbsup, heart, laugh, rocket, eyes). One reaction per type per user per message. |
| 8 | Hub choice | **Separate LoungeHub.** Mapped at `/hubs/lounge`, independent of the existing `PresenceHub`. |
| 9 | Message retention | **30-day retention.** Messages older than 30 days are automatically deleted. Pinned messages are exempt. A background cleanup runs periodically. |
| 10 | Global room moderation | **Site admins.** Site admins are the moderators for #general (can pin/unpin and delete messages). |

### Additional Feature: Message-to-Task Conversion

In project rooms, any project member can convert a lounge message into a task for that project. This bridges conversation and action. Not available in #general (no associated project). See the [Message-to-Task Conversion](#message-to-task-conversion) concept section for details.

---

## Implementation Sketch

This is a rough phase outline, not a formal implementation plan. A formal plan will be created in a separate specification document once the design is finalized.

### Step 1: Data Layer
- Create `LoungeMessage` entity with `IsEdited`, `EditedAt`, `CreatedTaskId` columns and EF Core configuration
- Create `LoungeReaction` entity with unique constraint and EF Core configuration
- Create `LoungeReadPosition` entity with unique constraint and EF Core configuration
- Add migration
- Write entity validation tests

### Step 2: Service Layer
- Create `ILoungeService` / `LoungeService`
  - `SendMessage(projectId, userId, content)` — Validates, persists, parses Markdown, returns the message
  - `EditMessage(messageId, userId, content)` — Updates content, sets `IsEdited`/`EditedAt`
  - `DeleteMessage(messageId, userId)` — Hard delete with cascade to reactions
  - `GetMessages(projectId, before, count)` — Paginated history with reactions
  - `PinMessage(messageId, userId)` / `UnpinMessage(messageId, userId)`
  - `GetPinnedMessages(projectId)`
  - `ToggleReaction(messageId, userId, reactionType)` — Add or remove a reaction
  - `GetUnreadCounts(userId)` — Returns unread message counts per room
  - `UpdateReadPosition(userId, projectId, lastReadMessageId)`
  - `CreateTaskFromMessage(messageId, userId)` — Creates a task pre-populated from the message, sets `CreatedTaskId`
  - `CleanupExpiredMessages()` — Deletes non-pinned messages older than 30 days
- Write unit tests for all service methods

### Step 3: SignalR Hub
- Create `LoungeHub` with methods: `JoinRoom`, `LeaveRoom`, `SendMessage`, `EditMessage`, `DeleteMessage`, `ToggleReaction`, `MarkAsRead`
- Implement authorization checks (project membership, author-only editing, admin deletion)
- Map hub at `/hubs/lounge` in `Program.cs`
- Write hub integration tests

### Step 4: Controllers and Views
- Create `LoungeController` with actions for room view, message history, and task creation from message
- Build room view with HTMX for history loading and Alpine.js for UI behavior
- Implement unread badges in sidebar via ViewComponent
- Implement emoji reaction UI (toggle buttons beneath messages)
- Implement "Create Task" action on project room messages
- Create `lounge.js` client script for SignalR connection, real-time message rendering, editing, reactions, and unread tracking
- Write controller integration tests

### Step 5: Notification Integration
- Parse `@mentions` from message content
- Create `LoungeMention` notifications via `INotificationService`
- Write notification integration tests

### Step 6: Background Services (Hangfire)
- Create a `LoungeRetentionJob` class with a public method for the cleanup logic
- Register a Hangfire recurring job that calls `LoungeRetentionJob` once per day
- Delete non-pinned `LoungeMessage` records older than 30 days
- Cascade delete associated `LoungeReaction` records
- Clean up orphaned `LoungeReadPosition` records
- Write retention job tests

### Step 7: Polish
- Mobile responsiveness
- Accessibility (keyboard navigation, screen reader support)
- Performance testing with message volume
- Verify unread tracking accuracy across multiple tabs/sessions

---

## References

- [PossibleFeatures.md](PossibleFeatures.md) — Feature #1: Project Lounge
- [SocialFeaturesSpecification.md](SocialFeaturesSpecification.md) — ACTV-06: SignalR as foundational infrastructure
- [PresenceHub.cs](../Hubs/PresenceHub.cs) — Existing SignalR hub for presence
- [IPresenceService.cs](../Services/IPresenceService.cs) — Existing presence service interface
- [presence.js](../wwwroot/js/presence.js) — Existing client-side SignalR usage pattern
