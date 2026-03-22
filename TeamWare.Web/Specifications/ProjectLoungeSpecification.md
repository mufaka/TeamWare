# TeamWare - Project Lounge Specification

## 1. Introduction

### 1.1 Purpose

This document provides the formal specification for the Project Lounge feature being added to TeamWare. It defines the functional requirements, data model additions, SignalR hub design, and background job requirements needed to support real-time, persistent chat within projects and across the site. This specification is a companion to the [main TeamWare specification](Specification.md) and the [Social Features specification](SocialFeaturesSpecification.md), and follows the same conventions.

### 1.2 Scope

The Project Lounge introduces real-time messaging to TeamWare, giving team members a place to have quick conversations, share updates, and collaborate without leaving the application. The feature draws from classic groupware chat rooms (Lotus Notes rooms, IRC channels, FirstClass conferences) reimagined for a modern, lightweight, self-hosted context.

The feature addresses the following gap:

1. **Informal Communication** — There is no lightweight, real-time channel for team conversation. Task comments serve a different purpose (structured discussion tied to a specific work item) and do not support spontaneous or cross-cutting conversation.

### 1.3 Definitions and Acronyms

| Term | Definition |
|------|-----------|
| Project Lounge | A per-project persistent chat room available to all members of that project |
| #general | A single site-wide chat room available to all authenticated users |
| Room | A chat context — either a Project Lounge (scoped to a project) or the #general room |
| LoungeHub | A dedicated SignalR hub for real-time lounge message delivery, separate from PresenceHub |
| Read Position | The last message a user has seen in a given room, used for unread tracking |
| Reaction | A lightweight emoji response to a message from a fixed set of types |
| Pinned Message | A message elevated to a persistent banner at the top of a room by an authorized user |
| Message Retention | The automatic deletion of non-pinned messages older than 30 days |
| Hangfire | A background job framework used to schedule the recurring message retention cleanup |

### 1.4 Design Principles

- The Project Lounge complements task comments; it does not replace them. Task comments remain the structured discussion mechanism for specific work items.
- Rooms are implicit, not user-created. Each project has exactly one lounge, and there is exactly one #general room. This avoids room management overhead.
- The implementation leverages the existing SignalR infrastructure (introduced in Phase 12 for presence) as a foundation, using a separate hub for single-responsibility.
- The server-rendered MVC + HTMX approach is maintained for page structure. SignalR handles only real-time message delivery, not page rendering.
- Message retention keeps the SQLite database manageable for self-hosted deployments. Pinned messages are exempt from cleanup.

---

## 2. Technology Additions

| Layer | Technology | Purpose |
|-------|-----------|---------|
| Real-Time Messaging | ASP.NET Core SignalR (LoungeHub) | Real-time message delivery, reactions, and read position updates |
| Background Jobs | Hangfire | Scheduled daily message retention cleanup |

All other technology choices remain unchanged from the [main specification](Specification.md).

---

## 3. Functional Requirements

### 3.1 Rooms

| ID | Requirement |
|----|------------|
| LOUNGE-01 | Each project shall automatically have a chat room (the "Project Lounge") accessible to all members of that project |
| LOUNGE-02 | The system shall provide a single site-wide chat room ("#general") accessible to all authenticated users |
| LOUNGE-03 | Rooms shall not be user-created. They exist implicitly as part of a project or as the single global room |

### 3.2 Messages

| ID | Requirement |
|----|------------|
| LOUNGE-04 | Project members shall be able to post text messages to their project's lounge |
| LOUNGE-05 | All authenticated users shall be able to post text messages to the #general room |
| LOUNGE-06 | Messages shall support Markdown formatting (bold, italic, code, links), rendered server-side with XSS sanitization |
| LOUNGE-07 | Message content shall have a maximum length of 4000 characters |
| LOUNGE-08 | Each message shall display the author's display name, avatar, and timestamp |
| LOUNGE-09 | Messages shall be delivered to all connected room members in real-time via SignalR |
| LOUNGE-10 | The message list shall support paginated history loading (older messages loaded on demand via HTMX) |

### 3.3 Message Editing

| ID | Requirement |
|----|------------|
| LOUNGE-11 | Message authors shall be able to edit the content of their own messages |
| LOUNGE-12 | Edited messages shall display an "edited" indicator with the timestamp of the last edit |
| LOUNGE-13 | No edit history shall be kept; only the current content is stored |
| LOUNGE-14 | Message edits shall be broadcast to all connected room members in real-time via SignalR |

### 3.4 Message Deletion

| ID | Requirement |
|----|------------|
| LOUNGE-15 | Message authors shall be able to delete their own messages |
| LOUNGE-16 | In project rooms, project owners and admins shall be able to delete any message |
| LOUNGE-17 | In the #general room, site admins shall be able to delete any message |
| LOUNGE-18 | Message deletion shall be a hard delete with cascade to associated reactions |
| LOUNGE-19 | Message deletions shall be broadcast to all connected room members in real-time via SignalR |

### 3.5 Mentions

| ID | Requirement |
|----|------------|
| LOUNGE-20 | Users shall be able to `@mention` other room members in message content |
| LOUNGE-21 | Mention autocomplete shall draw from the room's member list (project members for project rooms, all users for #general) |
| LOUNGE-22 | Mentions shall generate an in-app notification of type `LoungeMention` using the existing notification system |
| LOUNGE-23 | The notification shall link to the room, scrolled to the relevant message |
| LOUNGE-24 | Mentioned usernames shall be visually highlighted in the rendered message |

### 3.6 Pinned Messages

| ID | Requirement |
|----|------------|
| LOUNGE-25 | In project rooms, project owners and admins shall be able to pin a message to the top of the room |
| LOUNGE-26 | In the #general room, site admins shall be able to pin a message |
| LOUNGE-27 | Pinned messages shall be displayed as a persistent banner at the top of the room view |
| LOUNGE-28 | Authorized users shall be able to unpin a pinned message |
| LOUNGE-29 | Pin and unpin actions shall be broadcast to all connected room members in real-time via SignalR |
| LOUNGE-30 | Pinned messages shall be exempt from message retention cleanup |

### 3.7 Emoji Reactions

| ID | Requirement |
|----|------------|
| LOUNGE-31 | Users shall be able to add emoji reactions to messages from a fixed set of types |
| LOUNGE-32 | The supported reaction types shall be: `thumbsup`, `heart`, `laugh`, `rocket`, `eyes` |
| LOUNGE-33 | Each user shall be limited to one reaction of each type per message |
| LOUNGE-34 | Clicking an existing reaction shall toggle the current user's participation (add or remove) |
| LOUNGE-35 | Reactions shall be displayed as small counts beneath the message |
| LOUNGE-36 | Reaction changes shall be broadcast to all connected room members in real-time via SignalR |

### 3.8 Unread Tracking

| ID | Requirement |
|----|------------|
| LOUNGE-37 | The system shall track the last-read message per user per room |
| LOUNGE-38 | Unread count badges shall be displayed in the sidebar next to each project lounge and #general |
| LOUNGE-39 | A "new messages" divider shall be displayed in the message list showing where unread messages begin |
| LOUNGE-40 | The read position shall be automatically updated when the user scrolls to the bottom of the room |
| LOUNGE-41 | The read position shall not be updated when the user has only loaded older history (to avoid false read marking) |

### 3.9 Message-to-Task Conversion

| ID | Requirement |
|----|------------|
| LOUNGE-42 | In project rooms, a "Create Task" action shall appear on each message |
| LOUNGE-43 | The "Create Task" action shall not be available in the #general room |
| LOUNGE-44 | Clicking "Create Task" shall open a pre-filled task creation form with the title populated from the first line or first 200 characters of the message content |
| LOUNGE-45 | The task description shall be pre-populated with the full message content, prefixed with a reference to the lounge message (e.g., "From lounge message by @User on DATE") |
| LOUNGE-46 | The project field shall be locked to the current project; all other task fields shall be left at defaults |
| LOUNGE-47 | After task creation, the original lounge message shall display a system note indicating a task was created (e.g., "Task #123 created from this message") |
| LOUNGE-48 | Any project member shall be able to create a task from a message |
| LOUNGE-49 | The task creation link shall be broadcast to all connected room members in real-time via SignalR |

### 3.10 Message Retention

| ID | Requirement |
|----|------------|
| LOUNGE-50 | Messages older than 30 days shall be automatically deleted |
| LOUNGE-51 | Pinned messages shall be exempt from retention cleanup |
| LOUNGE-52 | The retention cleanup shall run once per day as a Hangfire recurring job |
| LOUNGE-53 | Retention cleanup shall cascade delete associated reactions |
| LOUNGE-54 | Retention cleanup shall remove orphaned read positions that reference deleted messages |
| LOUNGE-55 | The 30-day retention period is a fixed default for V1 |

### 3.11 SignalR Hub

| ID | Requirement |
|----|------------|
| LOUNGE-56 | A dedicated `LoungeHub` shall be mapped at `/hubs/lounge`, separate from the existing `PresenceHub` |
| LOUNGE-57 | The LoungeHub connection shall be established only when a user navigates to a lounge view |
| LOUNGE-58 | Each room shall map to a SignalR group: `lounge-project-{projectId}` for project rooms and `lounge-general` for the global room |
| LOUNGE-59 | The hub shall support client-to-server methods: `JoinRoom`, `LeaveRoom`, `SendMessage`, `EditMessage`, `DeleteMessage`, `ToggleReaction`, `MarkAsRead` |
| LOUNGE-60 | The hub shall support server-to-client methods: `ReceiveMessage`, `MessageEdited`, `MessageDeleted`, `MessagePinned`, `MessageUnpinned`, `ReactionUpdated`, `TaskCreatedFromMessage` |
| LOUNGE-61 | All hub methods shall enforce authorization checks server-side (project membership for project rooms, authenticated for #general) |

### 3.12 Authorization Summary

| ID | Requirement |
|----|------------|
| LOUNGE-62 | Viewing and sending messages in a project room shall require project membership |
| LOUNGE-63 | Viewing and sending messages in #general shall require authentication |
| LOUNGE-64 | Editing a message shall require being the message author |
| LOUNGE-65 | Deleting a message in a project room shall require being the message author, a project owner, or a project admin |
| LOUNGE-66 | Deleting a message in #general shall require being the message author or a site admin |
| LOUNGE-67 | Pinning and unpinning messages in a project room shall require the project owner or admin role |
| LOUNGE-68 | Pinning and unpinning messages in #general shall require the site admin role |
| LOUNGE-69 | Adding and removing reactions in a project room shall require project membership |
| LOUNGE-70 | Adding and removing reactions in #general shall require authentication |
| LOUNGE-71 | Creating a task from a message shall require project membership (project rooms only) |
| LOUNGE-72 | Authorization checks shall be enforced both in the LoungeHub and in MVC controller actions |

---

## 4. Data Model

### 4.1 New Entities

#### LoungeMessage

| Field | Type | Constraints |
|-------|------|------------|
| Id | int | Primary key, auto-increment |
| ProjectId | int? | Foreign key to Project; nullable (null means #general room) |
| UserId | string | Foreign key to ApplicationUser; required (message author) |
| Content | string | Required, max 4000 characters; Markdown-formatted text |
| CreatedAt | datetime | Required, default UTC now |
| IsEdited | bool | Default false; set to true when content is updated |
| EditedAt | datetime? | Nullable; timestamp of last edit |
| IsPinned | bool | Default false |
| PinnedByUserId | string? | Foreign key to ApplicationUser; nullable (who pinned it) |
| PinnedAt | datetime? | Nullable; when it was pinned |
| CreatedTaskId | int? | Foreign key to TaskItem; nullable (task created from this message) |

**Indexes:**
- `IX_LoungeMessage_ProjectId_CreatedAt` (ProjectId, CreatedAt) — For loading room history and retention cleanup
- `IX_LoungeMessage_UserId` (UserId) — For user activity queries

#### LoungeReaction

| Field | Type | Constraints |
|-------|------|------------|
| Id | int | Primary key, auto-increment |
| LoungeMessageId | int | Foreign key to LoungeMessage; required |
| UserId | string | Foreign key to ApplicationUser; required |
| ReactionType | string | Required, max 20 characters; one of: `thumbsup`, `heart`, `laugh`, `rocket`, `eyes` |
| CreatedAt | datetime | Required, default UTC now |

**Indexes:**
- `IX_LoungeReaction_MessageId` (LoungeMessageId) — For loading reactions with messages
- `IX_LoungeReaction_MessageId_UserId_Type` (LoungeMessageId, UserId, ReactionType) — Unique constraint; one reaction per type per user per message

#### LoungeReadPosition

| Field | Type | Constraints |
|-------|------|------------|
| Id | int | Primary key, auto-increment |
| UserId | string | Foreign key to ApplicationUser; required |
| ProjectId | int? | Foreign key to Project; nullable (null means #general room) |
| LastReadMessageId | int | Foreign key to LoungeMessage; required |
| UpdatedAt | datetime | Required; when the position was last updated |

**Indexes:**
- `IX_LoungeReadPosition_UserId_ProjectId` (UserId, ProjectId) — Unique constraint; one position per user per room

### 4.2 Modified Entities

#### Notification

The existing Notification entity's `Type` enum adds the following value:

| New Type Value | Description |
|----------------|------------|
| `LoungeMention` | Generated when a user is `@mentioned` in a lounge message |

The existing `ReferenceId` field shall reference the `LoungeMessage.Id` for mention notifications.

#### Project

The existing Project entity adds the following navigation property:

| Property | Type | Description |
|----------|------|------------|
| `LoungeMessages` | `ICollection<LoungeMessage>` | All lounge messages in the project's room |

#### ApplicationUser

The existing ApplicationUser entity adds the following navigation property:

| Property | Type | Description |
|----------|------|------------|
| `LoungeMessages` | `ICollection<LoungeMessage>` | All lounge messages authored by the user |

### 4.3 New Entity Relationships

- A **Project** has many **LoungeMessages** (nullable FK; null means #general).
- A **User** has many **LoungeMessages** (as author).
- A **User** has many **LoungeMessages** (as pinner, via PinnedByUserId).
- A **LoungeMessage** has many **LoungeReactions** (cascade delete).
- A **LoungeMessage** has one optional **TaskItem** (via CreatedTaskId).
- A **User** has many **LoungeReactions**.
- A **User** has many **LoungeReadPositions**.
- A **Project** has many **LoungeReadPositions** (nullable FK; null means #general).
- A **LoungeReadPosition** references one **LoungeMessage** (via LastReadMessageId).

---

## 5. SignalR Hub Design

### 5.1 LoungeHub

A dedicated `LoungeHub` is mapped at `/hubs/lounge`, separate from the existing `PresenceHub`. The hub requires authentication.

#### Server-to-Client Methods

| Method | Payload | Description |
|--------|---------|-------------|
| `ReceiveMessage` | `{ messageId, projectId, userId, displayName, avatarUrl, content, createdAt }` | New message posted to the room |
| `MessageEdited` | `{ messageId, content, editedAt }` | A message's content was updated |
| `MessageDeleted` | `{ messageId }` | A message was deleted |
| `MessagePinned` | `{ messageId, pinnedByDisplayName }` | A message was pinned |
| `MessageUnpinned` | `{ messageId }` | A message was unpinned |
| `ReactionUpdated` | `{ messageId, reactionType, count, userReacted }` | A reaction was added or removed |
| `TaskCreatedFromMessage` | `{ messageId, taskId, taskTitle }` | A task was created from a lounge message |

#### Client-to-Server Methods

| Method | Parameters | Description |
|--------|-----------|-------------|
| `JoinRoom` | `projectId` (null for #general) | Subscribe to messages for a room; adds connection to SignalR group |
| `LeaveRoom` | `projectId` (null for #general) | Unsubscribe from a room; removes connection from SignalR group |
| `SendMessage` | `projectId`, `content` | Post a message to a room |
| `EditMessage` | `messageId`, `content` | Update a message's content (author only) |
| `DeleteMessage` | `messageId` | Delete a message |
| `ToggleReaction` | `messageId`, `reactionType` | Add or remove a reaction |
| `MarkAsRead` | `projectId`, `lastReadMessageId` | Update the user's read position |

### 5.2 SignalR Groups

| Room Type | Group Name |
|-----------|-----------|
| Project Lounge | `lounge-project-{projectId}` |
| #general | `lounge-general` |

When a user calls `JoinRoom`, the hub validates authorization (project membership or authentication) and adds the connection to the corresponding group.

---

## 6. Background Jobs

### 6.1 Message Retention Job

| Property | Value |
|----------|-------|
| Job Class | `LoungeRetentionJob` |
| Scheduler | Hangfire recurring job |
| Frequency | Once per day |
| Retention Period | 30 days |

The job performs the following operations:
1. Delete all `LoungeMessage` records where `IsPinned` is false and `CreatedAt` is older than 30 days
2. Cascade delete associated `LoungeReaction` records (handled by EF Core cascade delete configuration)
3. Remove orphaned `LoungeReadPosition` records where the `LastReadMessageId` references a deleted message

---

## 7. Changes to Existing Requirements

The following existing requirements from the [main specification](Specification.md) and [Social Features specification](SocialFeaturesSpecification.md) are affected by this work:

| Requirement | Change |
|-------------|--------|
| ACTV-06 | Fulfilled. The SignalR infrastructure introduced for presence is now reused by the LoungeHub for real-time messaging. |
| NOTIF-01 through NOTIF-05 | Extended. A new notification type `LoungeMention` is added for `@mention` notifications in lounge messages. |

---

## 8. Non-Functional Requirements

The following non-functional requirements from the main specification apply with these additional considerations:

| ID | Requirement |
|----|------------|
| PERF-04 | Real-time message delivery via SignalR shall not introduce perceptible delay under normal operating conditions (fewer than 50 concurrent lounge users) |
| PERF-05 | Paginated message history loading via HTMX shall complete within 500 milliseconds under normal operating conditions |
| SEC-06 | Markdown content in lounge messages shall be rendered server-side with XSS sanitization |
| SEC-07 | All SignalR hub methods shall enforce authorization checks server-side before processing |
| TEST-08 | All lounge service methods shall have unit tests |
| TEST-09 | The LoungeHub shall have integration tests verifying authorization, message delivery, and group management |
| TEST-10 | The LoungeController shall have integration tests verifying room views, history loading, and task creation |
| TEST-11 | The LoungeRetentionJob shall have tests verifying correct cleanup behavior (retention period, pinned message exemption, cascade delete) |
| TEST-12 | The `@mention` notification flow shall have integration tests |

---

## 9. UI/UX Requirements

| ID | Requirement |
|----|------------|
| UI-08 | Each project the user is a member of shall display a "Lounge" link in the project sidebar or project detail page |
| UI-09 | The main sidebar shall display a "#general" link under a "Lounge" section, visible to all authenticated users |
| UI-10 | Unread count badges shall be displayed next to each lounge link in the sidebar |
| UI-11 | The room view shall display a header with the room name, a scrollable message area, and a fixed message input field at the bottom |
| UI-12 | Pinned messages shall appear as a banner between the room header and the message area |
| UI-13 | New messages shall auto-scroll to the bottom if the user is already at the bottom; a "new messages" indicator shall appear if the user has scrolled up |
| UI-14 | The message input shall remain fixed at the bottom of the viewport on all screen sizes |
| UI-15 | On mobile viewports, the lounge view shall be full-width with a compact message format |
| UI-16 | The lounge UI shall follow the existing TeamWare styling conventions (Tailwind CSS 4, light/dark theme support) |
| UI-17 | The lounge UI shall not contain emoticons or emojis in chrome or labels (consistent with UI-07); emoji reactions use text labels or Unicode code points only |

---

## 10. Future Considerations

The following features are out of scope for this release but may be considered for future iterations:

- **Typing indicators** — Deferred to avoid unnecessary SignalR traffic
- **Full-text message search** — May require SQLite FTS5 support; revisit in a later iteration
- **Configurable retention period** — The 30-day retention period is fixed for V1
- **User-created rooms** — Rooms are implicit for V1; custom rooms may be added later
- **File sharing in chat** — Deferred to a future File Sharing feature
- **Threads** — Reply threading within lounge messages
- **Rich embeds** — Link previews, image embeds

---

## 11. References

- [ProjectLoungeIdea.md](ProjectLoungeIdea.md) — Original idea document and design exploration
- [PossibleFeatures.md](PossibleFeatures.md) — Feature #1: Project Lounge
- [Specification.md](Specification.md) — Main TeamWare specification
- [SocialFeaturesSpecification.md](SocialFeaturesSpecification.md) — Social Features specification (ACTV-06: SignalR infrastructure)
- [PresenceHub.cs](../Hubs/PresenceHub.cs) — Existing SignalR hub for presence
- [IPresenceService.cs](../Services/IPresenceService.cs) — Existing presence service interface
- [presence.js](../wwwroot/js/presence.js) — Existing client-side SignalR usage pattern
