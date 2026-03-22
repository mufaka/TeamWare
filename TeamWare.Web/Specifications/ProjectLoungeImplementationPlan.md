# TeamWare - Project Lounge Implementation Plan

This document defines the phased implementation plan for the TeamWare Project Lounge feature based on the [Project Lounge Specification](ProjectLoungeSpecification.md). Each phase builds on the previous one and is broken into work items suitable for GitHub Issues. Check off items as they are completed to track progress.

---

## Progress Summary

| Phase | Description | Status |
|-------|------------|--------|
| 15 | Lounge Data Layer | Complete |
| 16 | Lounge Service Layer | Complete |
| 17 | Lounge SignalR Hub | Complete |
| 18 | Lounge Controllers and Views | Complete |
| 19 | Lounge Notifications and Mentions | Complete |
| 20 | Lounge Background Jobs | Not Started |
| 21 | Lounge Polish and Hardening | Not Started |

---

## Current State

All original phases (0-9) and social feature phases (10-14) are complete. The workspace is an ASP.NET Core MVC project (.NET 10) with:

- Full project and task management (CRUD, assignment, filtering, GTD workflow)
- Inbox capture/clarify workflow with Someday/Maybe list
- Progress tracking with activity log and deadline visibility
- Task commenting with notifications
- In-app notification system (task assignments, deadlines, status changes, comments, inbox/review prompts, project invitations)
- GTD review workflow
- User profile management and personal dashboard
- Site-wide admin role and admin dashboard
- User directory with profile pages
- Real-time online/offline presence via SignalR (`PresenceHub` at `/hubs/presence`)
- Project invitation accept/decline workflow
- Security hardening, performance optimization, and UI polish

The Project Lounge builds on top of this foundation. It reuses the SignalR infrastructure from Phase 12 (ACTV-06) and the notification system from Phase 6.

---

## Guiding Principles

All guiding principles from the [original implementation plan](ImplementationPlan.md) and [social features implementation plan](SocialFeaturesImplementationPlan.md) continue to apply:

1. **Vertical slices** — Each phase delivers end-to-end working functionality (model, data access, service, controller, view, tests).
2. **Tests accompany every feature** — No phase is complete without its test cases (MAINT-02, TEST-01 through TEST-12).
3. **One type per file** — Enforced throughout (MAINT-01).
4. **MVC only** — Controllers and Views, no Razor Pages (project guideline).
5. **SignalR as foundation** — The `PresenceHub` infrastructure is reused as the pattern for `LoungeHub`. The two hubs remain separate per single-responsibility.

Additionally:

6. **Hangfire for background jobs** — The message retention cleanup uses Hangfire rather than a custom hosted service. This is the first use of Hangfire in TeamWare.
7. **Real-time via SignalR, structure via MVC + HTMX** — Page structure and history loading use standard MVC controller actions with HTMX partials. SignalR handles only real-time message delivery, reactions, and read position updates.

---

## Phase 15: Lounge Data Layer

Create the domain entities, EF Core configuration, and database migration for lounge messages, reactions, and read positions.

### 15.1 LoungeMessage Entity

- [x] Create `LoungeMessage` entity with fields: `Id`, `ProjectId`, `UserId`, `Content`, `CreatedAt`, `IsEdited`, `EditedAt`, `IsPinned`, `PinnedByUserId`, `PinnedAt`, `CreatedTaskId` (LOUNGE-04, LOUNGE-06, LOUNGE-07, LOUNGE-11, LOUNGE-25, LOUNGE-42)
- [x] Create EF Core configuration for `LoungeMessage`:
  - [x] `ProjectId` as nullable FK to `Project` (null means #general) (LOUNGE-01, LOUNGE-02)
  - [x] `UserId` as required FK to `ApplicationUser` (message author)
  - [x] `PinnedByUserId` as nullable FK to `ApplicationUser`
  - [x] `CreatedTaskId` as nullable FK to `TaskItem` (LOUNGE-47)
  - [x] Index `IX_LoungeMessage_ProjectId_CreatedAt` (ProjectId, CreatedAt)
  - [x] Index `IX_LoungeMessage_UserId` (UserId)
- [x] Add `LoungeMessages` navigation property to `Project` entity
- [x] Add `LoungeMessages` navigation property to `ApplicationUser` entity
- [x] Write unit tests for `LoungeMessage` entity validation (content required, max 4000 chars)

### 15.2 LoungeReaction Entity

- [x] Create `LoungeReaction` entity with fields: `Id`, `LoungeMessageId`, `UserId`, `ReactionType`, `CreatedAt` (LOUNGE-31, LOUNGE-32, LOUNGE-33)
- [x] Create EF Core configuration for `LoungeReaction`:
  - [x] `LoungeMessageId` as required FK to `LoungeMessage` with cascade delete (LOUNGE-18)
  - [x] `UserId` as required FK to `ApplicationUser`
  - [x] Unique index `IX_LoungeReaction_MessageId_UserId_Type` (LoungeMessageId, UserId, ReactionType) (LOUNGE-33)
  - [x] Index `IX_LoungeReaction_MessageId` (LoungeMessageId)
- [x] Write unit tests for `LoungeReaction` entity validation (reaction type constrained to valid values)

### 15.3 LoungeReadPosition Entity

- [x] Create `LoungeReadPosition` entity with fields: `Id`, `UserId`, `ProjectId`, `LastReadMessageId`, `UpdatedAt` (LOUNGE-37)
- [x] Create EF Core configuration for `LoungeReadPosition`:
  - [x] `UserId` as required FK to `ApplicationUser`
  - [x] `ProjectId` as nullable FK to `Project` (null means #general)
  - [x] `LastReadMessageId` as required FK to `LoungeMessage`
  - [x] Unique index `IX_LoungeReadPosition_UserId_ProjectId` (UserId, ProjectId)
- [x] Write unit tests for `LoungeReadPosition` entity validation

### 15.4 Notification Type Extension

- [x] Add `LoungeMention` value to the `NotificationType` enum (LOUNGE-22)

### 15.5 Migration

- [x] Add and apply the EF Core migration for all lounge entities
- [x] Write integration tests verifying migration applies cleanly and all indexes and constraints are created

---

## Phase 16: Lounge Service Layer

Create the service interfaces and implementations for lounge messaging, reactions, read positions, and task conversion.

### 16.1 Lounge Message Services

- [x] Create `ILoungeService` interface
- [x] Create `LoungeService` implementation
  - [x] `SendMessage(int? projectId, string userId, string content)` — Validates content length, persists, returns the message (LOUNGE-04, LOUNGE-05, LOUNGE-07)
  - [x] `EditMessage(int messageId, string userId, string content)` — Updates content, sets `IsEdited`/`EditedAt`, validates author-only (LOUNGE-11, LOUNGE-12, LOUNGE-13, LOUNGE-64)
  - [x] `DeleteMessage(int messageId, string userId)` — Hard delete with cascade to reactions, validates authorization (LOUNGE-15, LOUNGE-16, LOUNGE-17, LOUNGE-18, LOUNGE-65, LOUNGE-66)
  - [x] `GetMessages(int? projectId, DateTime? before, int count)` — Paginated history with reactions eager-loaded (LOUNGE-10)
  - [x] `GetMessage(int messageId)` — Single message with author and reactions
- [x] Write unit tests for all message service methods including authorization rules (TEST-08)

### 16.2 Pin Services

- [x] Add to `ILoungeService` / `LoungeService`:
  - [x] `PinMessage(int messageId, string userId)` — Sets `IsPinned`, `PinnedByUserId`, `PinnedAt`, validates authorization (LOUNGE-25, LOUNGE-26, LOUNGE-67, LOUNGE-68)
  - [x] `UnpinMessage(int messageId, string userId)` — Clears pin fields, validates authorization (LOUNGE-28, LOUNGE-67, LOUNGE-68)
  - [x] `GetPinnedMessages(int? projectId)` — Returns pinned messages for a room (LOUNGE-27)
- [x] Write unit tests for pin/unpin including authorization rules (TEST-08)

### 16.3 Reaction Services

- [x] Add to `ILoungeService` / `LoungeService`:
  - [x] `ToggleReaction(int messageId, string userId, string reactionType)` — Adds or removes a reaction, validates type is in the allowed set, validates room membership (LOUNGE-31, LOUNGE-32, LOUNGE-33, LOUNGE-34, LOUNGE-69, LOUNGE-70)
  - [x] `GetReactionsForMessage(int messageId)` — Returns reaction counts and current user's reactions (LOUNGE-35)
- [x] Write unit tests for reaction toggle including uniqueness constraint (TEST-08)

### 16.4 Unread Tracking Services

- [x] Add to `ILoungeService` / `LoungeService`:
  - [x] `UpdateReadPosition(string userId, int? projectId, int lastReadMessageId)` — Creates or updates the read position (LOUNGE-37, LOUNGE-40, LOUNGE-41)
  - [x] `GetUnreadCounts(string userId)` — Returns a dictionary of room (projectId) to unread message count (LOUNGE-38)
  - [x] `GetReadPosition(string userId, int? projectId)` — Returns the last-read message ID for a room (LOUNGE-39)
- [x] Write unit tests for read position tracking and unread count calculation (TEST-08)

### 16.5 Message-to-Task Conversion

- [x] Add to `ILoungeService` / `LoungeService`:
  - [x] `CreateTaskFromMessage(int messageId, string userId)` — Validates project room, validates project membership, creates a `TaskItem` pre-populated from the message content, sets `CreatedTaskId` on the message, returns the created task (LOUNGE-42, LOUNGE-44, LOUNGE-45, LOUNGE-46, LOUNGE-47, LOUNGE-48, LOUNGE-71)
- [x] Write unit tests for task creation including pre-population logic and authorization (TEST-08)

### 16.6 Message Retention

- [x] Add to `ILoungeService` / `LoungeService`:
  - [x] `CleanupExpiredMessages()` — Deletes non-pinned messages older than 30 days, cascades to reactions, cleans up orphaned read positions (LOUNGE-50, LOUNGE-51, LOUNGE-53, LOUNGE-54, LOUNGE-55)
- [x] Write unit tests verifying retention logic: 30-day cutoff, pinned message exemption, cascade behavior, orphaned read position cleanup (TEST-08, TEST-11)

---

## Phase 17: Lounge SignalR Hub

Create the `LoungeHub` for real-time message delivery, reactions, and read position updates.

### 17.1 LoungeHub Implementation

- [x] Create `LoungeHub` class inheriting from `Hub`, decorated with `[Authorize]` (LOUNGE-56, LOUNGE-61)
- [x] Implement client-to-server methods (LOUNGE-59):
  - [x] `JoinRoom(int? projectId)` — Validate authorization (project membership or authenticated for #general), add connection to SignalR group (LOUNGE-58, LOUNGE-62, LOUNGE-63)
  - [x] `LeaveRoom(int? projectId)` — Remove connection from SignalR group
  - [x] `SendMessage(int? projectId, string content)` — Validate authorization and content, call `ILoungeService.SendMessage`, broadcast `ReceiveMessage` to group (LOUNGE-09, LOUNGE-62, LOUNGE-63)
  - [x] `EditMessage(int messageId, string content)` — Validate author-only, call `ILoungeService.EditMessage`, broadcast `MessageEdited` to group (LOUNGE-14, LOUNGE-64)
  - [x] `DeleteMessage(int messageId)` — Validate authorization, call `ILoungeService.DeleteMessage`, broadcast `MessageDeleted` to group (LOUNGE-19, LOUNGE-65, LOUNGE-66)
  - [x] `ToggleReaction(int messageId, string reactionType)` — Validate room membership, call `ILoungeService.ToggleReaction`, broadcast `ReactionUpdated` to group (LOUNGE-36, LOUNGE-69, LOUNGE-70)
  - [x] `MarkAsRead(int? projectId, int lastReadMessageId)` — Call `ILoungeService.UpdateReadPosition` (LOUNGE-40)
- [x] Implement server-to-client method broadcasts (LOUNGE-60):
  - [x] `ReceiveMessage` — New message payload with author info
  - [x] `MessageEdited` — Updated content and edit timestamp
  - [x] `MessageDeleted` — Message ID
  - [x] `MessagePinned` / `MessageUnpinned` — Pin status changes
  - [x] `ReactionUpdated` — Reaction type, count, and current user status
  - [x] `TaskCreatedFromMessage` — Task ID and title

### 17.2 Hub Registration

- [x] Map `LoungeHub` at `/hubs/lounge` in `Program.cs` (LOUNGE-56)
- [x] Verify the hub is independent of the existing `PresenceHub` at `/hubs/presence`

### 17.3 Hub Tests

- [x] Write integration tests for `JoinRoom` / `LeaveRoom` authorization (project membership, authenticated for #general) (TEST-09)
- [x] Write integration tests for `SendMessage` authorization and real-time delivery (TEST-09)
- [x] Write integration tests for `EditMessage` author-only enforcement (TEST-09)
- [x] Write integration tests for `DeleteMessage` authorization (author, project admin, site admin) (TEST-09)
- [x] Write integration tests for `ToggleReaction` room membership enforcement (TEST-09)
- [x] Write integration tests for SignalR group management (correct group names, isolation between rooms) (TEST-09)

---

## Phase 18: Lounge Controllers and Views

Create the MVC controllers, views, and client-side JavaScript for the lounge UI.

### 18.1 LoungeController

- [x] Create `LoungeController` with actions:
  - [x] `Room(int? projectId)` — Render the room view for a project lounge or #general, validate authorization (LOUNGE-62, LOUNGE-63)
  - [x] `Messages(int? projectId, DateTime? before, int count)` — Return paginated message history as an HTMX partial (LOUNGE-10)
  - [x] `PinnedMessages(int? projectId)` — Return pinned messages as an HTMX partial (LOUNGE-27)
  - [x] `CreateTaskFromMessage(int messageId)` — Create a task from a lounge message, return result (LOUNGE-42 through LOUNGE-49)
- [x] Enforce authorization on all actions (LOUNGE-72)

### 18.2 Room View

- [x] Build the room view with Tailwind CSS 4, light/dark theme support (LOUNGE-08, UI-11, UI-16):
  - [x] Room header displaying room name (UI-11)
  - [x] Pinned message banner (UI-12, LOUNGE-27)
  - [x] Scrollable message area with author avatar, display name, timestamp, and content (LOUNGE-08)
  - [x] "Edited" indicator on edited messages (LOUNGE-12)
  - [x] Emoji reaction buttons beneath each message (LOUNGE-35)
  - [x] "Create Task" action on messages in project rooms (LOUNGE-42, LOUNGE-43)
  - [x] "New messages" divider for unread tracking (LOUNGE-39)
  - [x] Fixed message input field at the bottom (UI-11, UI-14)
- [x] Implement "Load older messages" button or scroll-up trigger via HTMX (LOUNGE-10)
- [x] Implement responsive layout: full-width on mobile, compact message format (UI-15)
- [x] Verify no emoticons or emojis in UI chrome or labels (UI-17)

### 18.3 Sidebar Integration

- [x] Add "Lounge" link for each project in the project sidebar or project detail page (UI-08)
- [x] Add "#general" link under a "Lounge" section in the main sidebar (UI-09)
- [x] Implement unread count badges via a ViewComponent (LOUNGE-38, UI-10)

### 18.4 Client-Side JavaScript

- [x] Create `lounge.js` for SignalR connection and real-time message handling (LOUNGE-57):
  - [x] Establish `LoungeHub` connection only when on a lounge view (LOUNGE-57)
  - [x] Call `JoinRoom` on page load, `LeaveRoom` on navigation away
  - [x] Handle `ReceiveMessage` — append new message to the message list
  - [x] Handle `MessageEdited` — update message content in-place
  - [x] Handle `MessageDeleted` — remove message from the list
  - [x] Handle `MessagePinned` / `MessageUnpinned` — update pinned banner
  - [x] Handle `ReactionUpdated` — update reaction counts and toggle state
  - [x] Handle `TaskCreatedFromMessage` — display task creation note on message
  - [x] Auto-scroll to bottom on new messages if already at bottom (UI-13)
  - [x] Show "new messages" indicator when scrolled up (UI-13)
  - [x] Call `MarkAsRead` when scrolled to the bottom (LOUNGE-40)

### 18.5 Mention Autocomplete UI

- [x] Implement `@mention` autocomplete in the message input (LOUNGE-20, LOUNGE-21):
  - [x] Trigger on `@` character in the input field
  - [x] Populate from project members (project rooms) or all users (#general)
  - [x] Insert the selected username into the message content
- [x] Visually highlight mentioned usernames in rendered messages (LOUNGE-24)

### 18.6 Controller Tests

- [x] Write integration tests for `LoungeController.Room` authorization (project membership, authenticated for #general) (TEST-10)
- [x] Write integration tests for `LoungeController.Messages` pagination (TEST-10)
- [x] Write integration tests for `LoungeController.CreateTaskFromMessage` (TEST-10)
- [x] Write integration tests for sidebar unread badge ViewComponent

---

## Phase 19: Lounge Notifications and Mentions

Integrate `@mention` parsing with the existing notification system.

### 19.1 Mention Parsing

- [x] Implement `@mention` parsing in `LoungeService.SendMessage`:
  - [x] Extract `@username` references from message content (LOUNGE-20)
  - [x] Resolve usernames to user IDs
  - [x] Filter to only room members (project members for project rooms, all users for #general) (LOUNGE-21)

### 19.2 Notification Integration

- [x] Create `LoungeMention` notifications via `INotificationService` for each mentioned user (LOUNGE-22):
  - [x] Notification message includes the mentioning user's display name and room context
  - [x] `ReferenceId` set to the `LoungeMessage.Id` (LOUNGE-23)
  - [x] `Type` set to `NotificationType.LoungeMention`
- [x] Ensure self-mentions do not generate a notification

### 19.3 Notification Tests

- [x] Write integration tests verifying `@mention` parsing extracts correct usernames (TEST-12)
- [x] Write integration tests verifying `LoungeMention` notifications are created for valid room members (TEST-12)
- [x] Write integration tests verifying self-mentions are excluded (TEST-12)
- [x] Write integration tests verifying non-member mentions in project rooms are excluded (TEST-12)

---

## Phase 20: Lounge Background Jobs

Set up Hangfire and the message retention recurring job.

### 20.1 Hangfire Setup

- [ ] Add NuGet packages: `Hangfire.Core`, `Hangfire.AspNetCore`, and a storage provider suitable for SQLite (e.g., `Hangfire.Storage.SQLite` or `Hangfire.InMemory`)
- [ ] Configure Hangfire services in `Program.cs`
- [ ] Configure Hangfire dashboard (optional, admin-only access)
- [ ] Map Hangfire endpoints in the request pipeline

### 20.2 LoungeRetentionJob

- [ ] Create `LoungeRetentionJob` class with a public method for the cleanup logic (LOUNGE-52):
  - [ ] Call `ILoungeService.CleanupExpiredMessages()` (LOUNGE-50, LOUNGE-51, LOUNGE-53, LOUNGE-54)
- [ ] Register a Hangfire recurring job that calls `LoungeRetentionJob` once per day (LOUNGE-52)
- [ ] Verify the job ID and cron schedule are correct

### 20.3 Retention Job Tests

- [ ] Write tests verifying `LoungeRetentionJob` invokes `CleanupExpiredMessages` (TEST-11)
- [ ] Write tests verifying the Hangfire recurring job is registered with daily frequency (TEST-11)
- [ ] Write end-to-end tests verifying retention cleanup with test data: messages older than 30 days deleted, pinned messages retained, reactions and orphaned read positions cleaned up (TEST-11)

---

## Phase 21: Lounge Polish and Hardening

Final pass on cross-cutting concerns for the lounge feature.

### 21.1 Security and Authorization

- [ ] Audit all lounge endpoints (controller actions and hub methods) for authorization enforcement (SEC-07, LOUNGE-72)
- [ ] Verify Markdown rendering includes XSS sanitization on all message content (SEC-06)
- [ ] Review input validation on message content, reaction types, and mention parsing (SEC-04)
- [ ] Verify CSRF tokens on all lounge POST actions (SEC-03)
- [ ] Write security-focused integration tests

### 21.2 Performance

- [ ] Verify real-time message delivery latency under normal conditions (PERF-04)
- [ ] Verify paginated history loading response times (PERF-05)
- [ ] Profile message queries and optimize indexes if needed
- [ ] Test SignalR group management with multiple concurrent rooms
- [ ] Test retention job performance with large message volumes

### 21.3 UI/UX Consistency

- [ ] Verify lounge views are responsive across breakpoints (UI-01, UI-15)
- [ ] Verify light/dark theme consistency on all lounge views (UI-02, UI-16)
- [ ] Verify no emoticons or emojis in lounge UI chrome or labels (UI-07, UI-17)
- [ ] Verify message input remains fixed at bottom on all screen sizes (UI-14)
- [ ] Verify auto-scroll and "new messages" indicator behavior (UI-13)
- [ ] Verify unread badge accuracy across multiple tabs and sessions
- [ ] Review all lounge views for clarity and consistency with existing design (UI-03)

### 21.4 Accessibility

- [ ] Verify keyboard navigation through message list, input field, and reactions
- [ ] Verify screen reader support for message content, reactions, and unread indicators
- [ ] Ensure focus management when new messages arrive

### 21.5 Documentation

- [ ] Update `README.md` with lounge feature overview and configuration (Hangfire setup, SignalR hub)
- [ ] Document the lounge feature for end users (rooms, mentions, reactions, pinned messages, task conversion)
- [ ] Document the message retention policy (30-day cleanup, pinned message exemption)
- [ ] Document admin capabilities in #general room (message deletion, pinning)

---

## Phase Dependency Summary

```
Phase 15: Lounge Data Layer
  |
  +---> Phase 16: Lounge Service Layer (depends on entities from Phase 15)
          |
          +---> Phase 17: Lounge SignalR Hub (depends on services from Phase 16)
          |       |
          |       +---> Phase 18: Lounge Controllers and Views (depends on hub from Phase 17 and services from Phase 16)
          |
          +---> Phase 19: Lounge Notifications and Mentions (depends on services from Phase 16)
          |
          +---> Phase 20: Lounge Background Jobs (depends on services from Phase 16)
          |
          +---> Phase 21: Lounge Polish and Hardening (after Phases 17-20)
```

Phases 17, 19, and 20 can be worked in parallel after Phase 16 is complete. Phase 18 depends on Phase 17 (hub must exist for client-side JavaScript). Phase 21 is a final pass after all feature phases are done.

---

## Requirement Traceability

| Spec Requirement | Covered In |
|-----------------|-----------|
| LOUNGE-01, LOUNGE-02, LOUNGE-03 | Phase 15.1 |
| LOUNGE-04, LOUNGE-05, LOUNGE-07 | Phase 16.1 |
| LOUNGE-06 | Phase 16.1, Phase 21.1 |
| LOUNGE-08 | Phase 18.2 |
| LOUNGE-09 | Phase 17.1 |
| LOUNGE-10 | Phase 18.1, Phase 18.2 |
| LOUNGE-11 through LOUNGE-14 | Phase 16.1, Phase 17.1 |
| LOUNGE-15 through LOUNGE-19 | Phase 16.1, Phase 17.1 |
| LOUNGE-20, LOUNGE-21 | Phase 18.5, Phase 19.1 |
| LOUNGE-22, LOUNGE-23, LOUNGE-24 | Phase 19.2, Phase 18.5 |
| LOUNGE-25 through LOUNGE-30 | Phase 16.2, Phase 17.1, Phase 18.2 |
| LOUNGE-31 through LOUNGE-36 | Phase 16.3, Phase 17.1, Phase 18.2 |
| LOUNGE-37 through LOUNGE-41 | Phase 16.4, Phase 17.1, Phase 18.2, Phase 18.3 |
| LOUNGE-42 through LOUNGE-49 | Phase 16.5, Phase 17.1, Phase 18.1, Phase 18.2 |
| LOUNGE-50 through LOUNGE-55 | Phase 16.6, Phase 20.2 |
| LOUNGE-56 through LOUNGE-61 | Phase 17.1, Phase 17.2 |
| LOUNGE-62 through LOUNGE-72 | Phase 17.1, Phase 18.1, Phase 21.1 |
| PERF-04, PERF-05 | Phase 21.2 |
| SEC-06, SEC-07 | Phase 21.1 |
| TEST-08 | Phase 16 (all sub-phases) |
| TEST-09 | Phase 17.3 |
| TEST-10 | Phase 18.6 |
| TEST-11 | Phase 16.6, Phase 20.3 |
| TEST-12 | Phase 19.3 |
| UI-08 through UI-17 | Phase 18.2, Phase 18.3, Phase 21.3 |
| MAINT-01 through MAINT-03 | All Phases (enforced throughout) |
