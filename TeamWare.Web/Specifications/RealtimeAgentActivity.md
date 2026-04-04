# Real-Time Agent Activity on Task Details — Ideas

**GitHub Issue:** [#276](https://github.com/mufaka/TeamWare/issues/276)
**TeamWare Task:** #50
**Scope:** Task Details view (`Views/Task/Details.cshtml`)

This document is for brainstorming and discussion around making agent actions (status changes, comments, activity log entries) appear in real time on the Task Details page, so the user doesn't have to manually refresh while an agent is working.

---

## Context

When an agent (Copilot, Claude, or Codex) is assigned a task, it interacts with TeamWare exclusively through MCP tools hosted in TeamWare.Web:

| Agent Action | MCP Tool | Server-Side Service |
|---|---|---|
| Change task status | `TaskTools.update_task_status` | `ITaskService.ChangeStatus` |
| Add a comment | `TaskTools.add_comment` | `ICommentService.AddComment` |
| Post to lounge | `LoungeTools.post_lounge_message` | `ILoungeService.SendMessage` |

Currently, the Task Details page renders these sections server-side on load:

- **Status badge** — static `<span>` with Tailwind classes based on `Model.Status`
- **Comments section** (`#comments-section`) — rendered by `_CommentList` partial; already supports htmx for human-initiated adds
- **Activity History** — static `<ul>` of activity log entries

When an agent calls `update_task_status` or `add_comment`, the database is updated but no connected browser is notified. The user must refresh the page to see what the agent did.

The lounge already solves this exact problem for chat messages using SignalR (`LoungeHub` + `lounge.js`). The pattern is proven and well-understood within this codebase.

### Key Constraints

- **Task Details only** — This feature targets the Task Details page. Broader real-time updates (dashboards, task lists) are out of scope for this idea.
- **No polling** — The solution should use push (SignalR), not client-side polling. The lounge already establishes this convention.
- **Minimal UI changes** — The existing layout and Tailwind styling should be preserved. New elements should be appended/updated in place, not require a full page restructure.
- **Agent and human actions** — The solution should push updates regardless of whether the change was initiated by an agent (via MCP tool) or a human (via the web UI). This avoids special-casing.
- **Existing hub reuse vs. new hub** — The lounge has its own dedicated `LoungeHub`. Task activity could either piggyback on a general-purpose hub or get its own. Worth discussing.

---

## Idea 1: Dedicated TaskHub with Per-Task Groups

### Approach

Create a new `TaskHub` (similar to `LoungeHub`) that manages SignalR groups per task ID. When a user opens the Task Details page, the client joins group `task-{id}`. When the task is modified (via any path), the server broadcasts the change to that group.

### Server-Side Components

**New `TaskHub` class** (`Hubs/TaskHub.cs`):
```
JoinTask(int taskId)    — adds connection to group "task-{taskId}" (after authorization check)
LeaveTask(int taskId)   — removes connection from group
```

Authorization: verify the user is a member of the task's project (same pattern as `LoungeHub.CanAccessRoom`).

**Broadcast from services or controllers:**

The simplest integration point is wherever the service calls return successfully. Two options:

1. **Controller/tool level** — After `taskService.ChangeStatus(...)` succeeds in `TaskController.ChangeStatus` and `TaskTools.update_task_status`, inject `IHubContext<TaskHub>` and broadcast. This is explicit but means every call site must remember to broadcast.

2. **Service level** — Add broadcast logic inside `TaskService.ChangeStatus`, `CommentService.AddComment`, etc. Centralizes the broadcast but couples the service layer to SignalR.

3. **Decorator / event pattern** — Services raise domain events; a handler broadcasts via `IHubContext<TaskHub>`. Cleanest separation but more infrastructure.

Option 1 (controller/tool level) is consistent with how the lounge works today — `LoungeHub.SendMessage` broadcasts directly, and `LoungeController.CreateTask` uses `IHubContext<LoungeHub>` for non-hub-initiated broadcasts.

### Client-Side Events

| Server Event | Payload | UI Update |
|---|---|---|
| `TaskStatusChanged` | `{ taskId, newStatus, updatedAt, changedByName, isAgent }` | Update status badge text + Tailwind classes; prepend activity entry |
| `TaskCommentAdded` | `{ taskId, commentId, content, authorName, isAgent, createdAt }` | Append new comment to `#comments-section` |
| `TaskActivityAdded` | `{ taskId, activityId, changeType, description, userName, isAgent, createdAt }` | Prepend entry to Activity History list |
| `TaskAssignmentChanged` | `{ taskId, assignees[], changedByName }` | Re-render assignee list in sidebar |

### Client-Side Script

New `wwwroot/js/task-realtime.js`:

```
1. Check for a data attribute (e.g., data-task-id) on the page to know which task is open
2. Build SignalR connection to /hubs/task
3. On connect: call hub.JoinTask(taskId)
4. Register handlers for each event:
   - TaskStatusChanged → update the badge DOM
   - TaskCommentAdded → build comment HTML and append
   - TaskActivityAdded → build activity entry HTML and prepend
   - TaskAssignmentChanged → rebuild assignee sidebar section
5. On page unload: call hub.LeaveTask(taskId)
```

### Pros
- Clean separation — task real-time logic is fully isolated from lounge
- Group granularity is per-task, so only users viewing that specific task receive updates
- Follows the proven `LoungeHub` pattern already in the codebase

### Cons
- Another hub to register in `Program.cs` and another JS file to maintain
- The comment and activity HTML must be constructed in JavaScript, duplicating the Razor partial logic

---

## Idea 2: Reuse Existing htmx Pattern with SignalR-Triggered Refresh

### Approach

Instead of building individual DOM elements in JavaScript, use SignalR purely as a notification channel. When a change occurs, the server broadcasts a lightweight "refresh" signal. The client receives it and uses htmx to fetch updated partials from the server.

### How It Would Work

1. **SignalR broadcasts a simple event**: `TaskUpdated { taskId, section: "comments" | "status" | "activity" | "assignees" }`
2. **Client receives the event** and triggers an htmx request to fetch the updated section:
   - `GET /Task/CommentsPartial?id={taskId}` → returns `_CommentList` partial
   - `GET /Task/StatusPartial?id={taskId}` → returns just the status badge HTML
   - `GET /Task/ActivityPartial?id={taskId}` → returns the activity history HTML
3. **htmx swaps** the returned HTML into the correct `#target` element

### New Controller Endpoints

```
[HttpGet] Task/CommentsPartial?id={taskId}     → PartialView("_CommentList", ...)
[HttpGet] Task/StatusPartial?id={taskId}       → PartialView("_StatusBadge", ...)
[HttpGet] Task/ActivityPartial?id={taskId}     → PartialView("_ActivityHistory", ...)
[HttpGet] Task/AssigneesPartial?id={taskId}    → PartialView("_AssigneeList", ...)
```

### Pros
- **No HTML construction in JavaScript** — all rendering stays in Razor partials
- **Existing partial reuse** — `_CommentList` is already used with htmx for human-initiated comment adds; this extends the same pattern
- **Simpler JS** — the client script is just "on signal, fetch partial" — no template logic
- **Consistent** — Razor remains the single source of truth for markup

### Cons
- Extra HTTP round-trip per update (SignalR event → htmx GET → response)
- If multiple sections update simultaneously (e.g., status change creates an activity entry too), the client makes multiple fetches
- Slightly higher server load compared to pushing pre-rendered data through the socket

### Mitigation for multiple fetches
Use a short debounce (e.g., 200ms) to coalesce rapid-fire events, or send a single `TaskUpdated { sections: ["status", "activity"] }` and batch the fetches.

---

## Idea 3: Hybrid — Push Data for Simple Updates, Fetch Partials for Complex Ones

### Approach

Combine Ideas 1 and 2 based on update complexity:

| Update Type | Strategy | Rationale |
|---|---|---|
| Status change | **Push** — send new status string + CSS class in the SignalR event | Status badge is a single `<span>` — trivial to update via JS |
| New comment | **Fetch partial** — trigger htmx to reload `_CommentList` | Comments have markdown rendering, timestamps, bot badges, edit/delete buttons — too complex for JS |
| Activity entry | **Push** — send activity description + metadata | Activity entries are simple text lines — easy to construct in JS |
| Assignee change | **Fetch partial** — trigger htmx to reload assignee section | Assignee section has forms for remove/add — complex markup |

### Pros
- Best of both worlds: instant UI update for simple elements, server-rendered correctness for complex ones
- Minimizes both JavaScript complexity and unnecessary HTTP round-trips

### Cons
- Two different update patterns on one page — slightly more cognitive load for maintenance
- Must decide per-section which approach to use

---

## Broadcast Integration Points

Regardless of which client approach is chosen, the server must broadcast events when task data changes. The key question is: **where does the broadcast call go?**

### Option A: In Each Controller / MCP Tool (Explicit)

```csharp
// TaskController.ChangeStatus (human)
var result = await _taskService.ChangeStatus(id, status, userId);
if (result.Succeeded)
    await _taskHub.Clients.Group($"task-{id}").SendAsync("TaskStatusChanged", ...);

// TaskTools.update_task_status (agent via MCP)
var result = await taskService.ChangeStatus(taskId, parsedStatus, userId);
if (result.Succeeded)
    await taskHub.Clients.Group($"task-{taskId}").SendAsync("TaskStatusChanged", ...);
```

**Pro:** Explicit, easy to understand, consistent with how `LoungeController` already uses `IHubContext<LoungeHub>`.
**Con:** Every call site must remember to broadcast. Adding a new controller or tool that modifies tasks requires adding the broadcast call.

### Option B: In the Service Layer

```csharp
// TaskService.ChangeStatus (centralized)
public async Task<ServiceResult<TaskItem>> ChangeStatus(int taskId, TaskItemStatus status, string userId)
{
    // ... existing logic ...
    await _taskHubContext.Clients.Group($"task-{taskId}").SendAsync("TaskStatusChanged", ...);
    return ServiceResult<TaskItem>.Success(task);
}
```

**Pro:** Single broadcast point — any caller (controller, MCP tool, future API) automatically triggers it.
**Con:** Couples the service layer to SignalR's `IHubContext`, which may be unwanted if services are meant to be infrastructure-agnostic.

### Option C: Domain Events (Decoupled)

Services publish a domain event (e.g., `TaskStatusChangedEvent`). A separate handler subscribes and broadcasts via `IHubContext<TaskHub>`.

**Pro:** Cleanest separation; services don't know about SignalR.
**Con:** More infrastructure (event bus, handlers). May be overkill for the current scope.

### Recommendation

**Option A** for now — it's consistent with the lounge pattern and avoids introducing new architectural layers. If the number of broadcast call sites grows significantly, refactoring to Option B or C can happen then.

---

## Visual Indicator: "Agent is working..."

When a user is viewing a task that an agent is actively processing, a subtle indicator could show that changes may appear at any time.

### How to detect "active" state

The agent transitions the task to `InProgress` via `update_task_status` when it starts working. The SignalR status change event already carries this information. The client could show a persistent indicator when:

- `status == "InProgress"` AND
- The status was changed by an agent user (`isAgent == true`)

### Possible UI

A small animated element near the status badge:

```
[In Progress] 🤖 Agent is working on this task...
```

Or a pulsing dot / spinner in the header area that disappears when the status changes away from `InProgress`.

This is a nice-to-have that could be deferred.

---

## Notification Sound / Toast (Optional)

For awareness, the page could show a subtle toast notification (e.g., "Agent updated status to In Review") when changes arrive. This is similar to how the lounge shows new message indicators when the user scrolls up.

Low priority — the DOM updates themselves provide visual feedback.

---

## Open Questions

1. **Hub choice** — Should we create a dedicated `TaskHub`, or extend `PresenceHub` / create a general-purpose `ActivityHub` that could also serve future dashboard real-time needs?

2. **Authorization granularity** — Should the hub verify project membership on `JoinTask`, or is the `[Authorize]` attribute (authenticated user) sufficient? The lounge hub checks project membership for project rooms.

3. **Partial extraction** — The current `Details.cshtml` renders comments, activity, and assignees inline. To support Idea 2 (htmx refresh), these sections need to be extractable as standalone partials. `_CommentList` already exists. We'd need `_ActivityHistory` and `_AssigneeList` partials. Is this refactoring welcome?

4. **Human-initiated changes** — When a human changes the status on the same page via the form buttons, the page already redirects/refreshes. Should SignalR updates be suppressed for the initiating user (to avoid a flicker), or is the double-update harmless?

5. **Scope of "task" events** — Should we also broadcast when the task title, description, priority, due date, or GTD flags change? Or limit to the four main categories (status, comments, activity, assignees) initially?

6. **Connection lifecycle** — Should the SignalR connection be established globally on every page (and join/leave task groups as needed), or should it only connect on the Task Details page? Global is more flexible for future dashboard use; per-page is simpler.

---

## Files Likely to Be Modified or Created

| File | Change |
|---|---|
| `Hubs/TaskHub.cs` | **New** — Hub with `JoinTask` / `LeaveTask`, authorization |
| `Program.cs` | Register `TaskHub` endpoint |
| `wwwroot/js/task-realtime.js` | **New** — SignalR client, event handlers |
| `Views/Task/Details.cshtml` | Add `data-task-id` attribute, include script reference |
| `Controllers/TaskController.cs` | Inject `IHubContext<TaskHub>`, broadcast after mutations; possibly add partial endpoints |
| `Mcp/Tools/TaskTools.cs` | Inject `IHubContext<TaskHub>`, broadcast after `update_task_status`, `add_comment`, `assign_task` |
| `Views/Task/_ActivityHistory.cshtml` | **New** (if Idea 2) — extracted from `Details.cshtml` |
| `Views/Task/_AssigneeList.cshtml` | **New** (if Idea 2) — extracted from `Details.cshtml` |
| `Views/Task/_StatusBadge.cshtml` | **New** (if Idea 2) — extracted from `Details.cshtml` |
