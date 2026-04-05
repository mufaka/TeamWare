# Real-Time Agent Activity on Task Details

**GitHub Issue:** [#276](https://github.com/mufaka/TeamWare/issues/276)
**TeamWare Task:** #50
**Scope:** Task Details view (`Views/Task/Details.cshtml`)
**Chosen Approach:** SignalR notification → htmx partial refresh (Idea 2)
**Status:** ✅ Implemented (Phase 49–50)

This document describes the design for making agent actions (status changes, comments, activity log entries) appear in real time on the Task Details page, so the user doesn't have to manually refresh while an agent is working.

### Implementation Notes

The design was implemented as specified with the following deviations:

- **SignalR client library path**: The plan referenced `~/js/signalr/dist/browser/signalr.min.js` but the actual location in the codebase is `~/lib/signalr/signalr.min.js`. The implementation uses the correct path.
- **Fetch fallback**: `task-realtime.js` includes a `fetch` fallback when `htmx` is not available, though in practice htmx is always loaded on the Task Details page.
- **Toast implementation**: Toasts use CSS opacity transitions rather than Alpine.js `x-transition`, since the script creates elements dynamically outside of Alpine's scope. The visual behavior matches `_Notification.cshtml`.

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

- **Task Details only** — This feature targets the Task Details page. Broader real-time updates (dashboards, task lists) are out of scope.
- **No polling** — The solution should use push (SignalR), not client-side polling. The lounge already establishes this convention.
- **Minimal UI changes** — The existing layout and Tailwind styling should be preserved. New elements should be appended/updated in place, not require a full page restructure.
- **Agent and human actions** — The solution should push updates regardless of whether the change was initiated by an agent (via MCP tool) or a human (via the web UI). This avoids special-casing.

---

## Design: SignalR Notification → htmx Partial Refresh

### Core Idea

Use SignalR purely as a **notification channel** — not a data channel. When a task is modified, the server broadcasts a lightweight signal identifying *which sections* changed. The client receives the signal and uses htmx to fetch server-rendered partials for those sections.

This keeps all HTML rendering in Razor (single source of truth for markup) and avoids duplicating template logic in JavaScript. The extra HTTP round-trip per update is trivial for a self-hosted, small-team application.

### Data Flow

```
Agent/Human mutates task
        │
        ▼
Service layer persists change
        │
        ▼
Controller / MCP tool broadcasts via IHubContext<TaskHub>
        │
        ▼
SignalR sends to group "task-{taskId}":
    TaskUpdated { sections: ["status", "activity"] }
        │
        ▼
Client JS receives event, triggers htmx requests:
    GET /Task/StatusPartial?id={taskId}    → swap into #task-status-section
    GET /Task/ActivityPartial?id={taskId}  → swap into #task-activity-section
        │
        ▼
Server returns rendered partial HTML
        │
        ▼
htmx replaces DOM content — user sees update
```

### SignalR Event

A single event type covers all mutations:

```
TaskUpdated { taskId: int, sections: string[], summary: string }
```

Where `sections` is one or more of: `"status"`, `"comments"`, `"activity"`.

A status change typically produces `["status", "activity"]` (the status badge updates and a new activity log entry is created). A new comment produces `["comments", "activity"]`.

Sending sections as an array lets the client batch its htmx fetches and avoids redundant requests when a single action affects multiple sections.

The `summary` field is a human-readable description of the change, displayed as a toast notification. Examples:
- `"Agent updated status to In Review"`
- `"Agent added a comment"`
- `"Bill changed status to Done"`

---

## Server-Side Components

### 1. TaskHub (`Hubs/TaskHub.cs`) — New

A lightweight hub with group management. No client-to-server data methods — all broadcasting is done via `IHubContext<TaskHub>` from controllers and MCP tools.

```
Methods:
    JoinTask(int taskId)   — verify project membership, add to group "task-{taskId}"
    LeaveTask(int taskId)  — remove from group

Authorization:
    [Authorize] attribute on the hub class (same as LoungeHub).
    JoinTask verifies the user is a member of the task's project (consistency with LoungeHub.CanAccessRoom).

Group naming:
    static string GetGroupName(int taskId) => $"task-{taskId}"
```

`JoinTask` must query `ProjectMembers` (via `DbContext` or a service) to verify the calling user has access to the project that owns the task. This mirrors `LoungeHub.CanAccessRoom` and prevents unauthorized users from subscribing to task updates by guessing task IDs.

### 2. Partial Endpoints on TaskController — New

Three new `[HttpGet]` actions that return partial views for each refreshable section:

| Endpoint | Partial View | Target Element |
|---|---|---|
| `GET /Task/StatusPartial?id={taskId}` | `_StatusSection` | `#task-status-section` |
| `GET /Task/CommentsPartial?id={taskId}` | `_CommentList` (existing) | `#comments-section` |
| `GET /Task/ActivityPartial?id={taskId}` | `_ActivityHistory` | `#task-activity-section` |

Each endpoint loads the relevant data from the service layer and returns `PartialView(...)`. Authorization is handled by the existing `[Authorize]` attribute on `TaskController`.

The `_CommentList` partial already exists and is used by the htmx comment-add form. The other two partials need to be extracted from the current inline markup in `Details.cshtml`.

> **Note:** Assignees are not included because agents cannot change task assignments — only humans do that through the web UI, which already triggers a page refresh.

### 3. Partial Views — New (2) + Existing (1)

**`_StatusSection.cshtml`** — Extracted from `Details.cshtml` lines 43–57. Contains the status badge `<span>`, priority badge, Next Action/Someday-Maybe badges. Receives a view model with `Status`, `Priority`, `IsNextAction`, `IsSomedayMaybe`.

**`_ActivityHistory.cshtml`** — Extracted from `Details.cshtml` lines 172–219. The activity log `<ul>` with colored dots and timestamps. Receives `List<ActivityLogEntryViewModel>`.

**`_CommentList.cshtml`** — Already exists. No changes needed.

### 4. Broadcast Calls — Additions to Existing Files

Inject `IHubContext<TaskHub>` into `TaskController` and the MCP `TaskTools` class. After each successful mutation, broadcast `TaskUpdated` to the task's group.

**Broadcast locations:**

| Call Site | Sections to Broadcast | Notes |
|---|---|---|
| `TaskController.ChangeStatus` | `["status", "activity"]` | Human-initiated; harmless duplicate on page refresh |
| `CommentController.Add` / `Edit` / `Delete` | `["comments", "activity"]` | Human-initiated; comment add already uses htmx |
| `TaskTools.update_task_status` | `["status", "activity"]` | Agent-initiated — primary use case |
| `TaskTools.add_comment` | `["comments", "activity"]` | Agent-initiated — primary use case |

> **Excluded:** `TaskController.Assign/Unassign`, `TaskController.ToggleNextAction/ToggleSomedayMaybe`, and `TaskTools.assign_task` are not broadcast. Agents cannot change assignments, and GTD flag toggles are human-only actions that already cause a page refresh. These can be added later if needed.

This is the explicit approach (Option A from the original brainstorm) — consistent with how `LoungeController` uses `IHubContext<LoungeHub>`.

### 5. Hub Registration — `Program.cs`

Add the hub endpoint alongside the existing lounge and presence hubs:

```csharp
app.MapHub<TaskHub>("/hubs/task");
```

---

## Client-Side Components

### `wwwroot/js/task-realtime.js` — New

The script is structured similarly to `lounge.js` but much simpler — it only receives events and triggers htmx fetches.

```
Pseudocode:

1. Find element with data-task-id attribute; if absent, exit (not on task details page)
2. Read taskId from the attribute
3. Read partial URLs from data attributes on each section container:
   - #task-status-section[data-partial-url]
   - #comments-section[data-partial-url]
   - #task-activity-section[data-partial-url]
4. Build SignalR connection to /hubs/task with automatic reconnect
5. On connection start: invoke hub.JoinTask(taskId)
6. Register handler for "TaskUpdated":
   - Receive { taskId, sections[], summary }
   - For each section in sections[]:
     - Find the target element and its data-partial-url
     - Trigger htmx.ajax("GET", url, { target: element, swap: "innerHTML" })
   - If summary is present, call showToast(summary)
7. Handle reconnection: re-invoke JoinTask on reconnect
```

### Debounce Strategy

If a single agent action triggers multiple `TaskUpdated` events in rapid succession (unlikely but possible with concurrent operations), use a 200ms debounce. Collect all section names received within the window, then fetch each unique section once.

### Details.cshtml Changes

1. Add `data-task-id="@Model.Id"` to the page container element.
2. Wrap each refreshable section in a container with an `id` and `data-partial-url`:
   ```html
   <div id="task-status-section" data-partial-url="@Url.Action("StatusPartial", "Task", new { id = Model.Id })">
       @await Html.PartialAsync("_StatusSection", ...)
   </div>
   ```
3. Same pattern for activity. Comments section already has `#comments-section`.
4. Include the script reference:
   ```html
   @section Scripts {
       <script src="~/js/signalr/dist/browser/signalr.min.js"></script>
       <script src="~/js/task-realtime.js"></script>
   }
   ```

---

## Broadcast Integration Points

The broadcast calls go in each controller and MCP tool (Option A — explicit, consistent with the lounge pattern):

```csharp
// Example: TaskController.ChangeStatus
var result = await _taskService.ChangeStatus(id, status, userId);
if (result.Succeeded)
{
    await _taskHubContext.Clients.Group(TaskHub.GetGroupName(id))
        .SendAsync("TaskUpdated", new
        {
            taskId = id,
            sections = new[] { "status", "activity" },
            summary = $"{displayName} changed status to {status}"
        });
}

// Example: TaskTools.update_task_status (MCP)
var result = await taskService.ChangeStatus(taskId, parsedStatus, userId);
if (result.Succeeded)
{
    await taskHubContext.Clients.Group(TaskHub.GetGroupName(taskId))
        .SendAsync("TaskUpdated", new
        {
            taskId,
            sections = new[] { "status", "activity" },
            summary = $"{displayName} changed status to {parsedStatus}"
        });
}
```

**Why Option A (explicit) over centralizing in the service layer:**
- Consistent with how the lounge works today
- Avoids coupling the service layer to SignalR infrastructure
- The number of call sites is bounded and manageable (4 locations)
- If it grows unwieldy, refactoring to a service-layer or domain-event approach is straightforward

---

## Toast Notifications

When a `TaskUpdated` event arrives, the client displays a brief toast notification using the `summary` field from the event payload. This gives the user immediate awareness that something changed, even if the updated section is scrolled out of view.

### Design

The toast should match the existing notification style from `_Notification.cshtml`, which uses Alpine.js for show/hide transitions:

- **Position:** Fixed to the bottom-right of the viewport (avoids overlapping the page header and content).
- **Style:** Uses the `info` notification variant — blue background, matching the existing `_Notification.cshtml` blue pattern (`bg-blue-50 text-blue-800 dark:bg-blue-900/30 dark:text-blue-300`).
- **Auto-dismiss:** Fades out after 5 seconds (consistent with existing `_Notification.cshtml` auto-dismiss behavior).
- **Manual dismiss:** Includes a close button (`×`).
- **Stacking:** If multiple updates arrive in quick succession, toasts stack vertically (newest at bottom). Each dismisses independently.

### Implementation

The toast container is a fixed-position `<div>` added to `Details.cshtml` (or the layout, if preferred). `task-realtime.js` creates toast elements dynamically when `TaskUpdated` events arrive:

```
Pseudocode:

function showToast(summary) {
    1. Create a <div> element styled to match _Notification.cshtml info variant
    2. Set inner text to summary
    3. Add close button that removes the element on click
    4. Append to #task-toast-container
    5. After 5 seconds, fade out and remove the element
}

// In the TaskUpdated handler:
connection.on("TaskUpdated", function (event) {
    // ... existing htmx refresh logic ...
    if (event.summary) {
        showToast(event.summary);
    }
});
```

### Toast Container Markup

Added to `Details.cshtml`:

```html
<div id="task-toast-container"
     class="fixed bottom-4 right-4 z-50 flex flex-col gap-2 max-w-sm">
    <!-- Toasts appended here dynamically by task-realtime.js -->
</div>
```

Each toast element follows this structure (created in JS):

```html
<div class="flex items-center justify-between rounded-md bg-blue-50 px-4 py-3 text-sm text-blue-800
            shadow-lg dark:bg-blue-900/30 dark:text-blue-300
            transition ease-in duration-300"
     role="alert">
    <span>Agent changed status to In Review</span>
    <button class="ml-4 text-blue-600 hover:text-blue-800
                   dark:text-blue-400 dark:hover:text-blue-200"
            aria-label="Dismiss">&times;</button>
</div>
```

### Summary Strings by Action

| Call Site | Summary Template |
|---|---|
| `TaskController.ChangeStatus` | `"{displayName} changed status to {status}"` |
| `CommentController.Add` | `"{displayName} added a comment"` |
| `CommentController.Edit` | `"{displayName} edited a comment"` |
| `CommentController.Delete` | `"{displayName} deleted a comment"` |
| `TaskTools.update_task_status` | `"{displayName} changed status to {status}"` |
| `TaskTools.add_comment` | `"{displayName} added a comment"` |

The `displayName` is resolved from the authenticated user's claims or the `ApplicationUser` record. For agents, this will be the agent's configured display name (e.g., "Copilot Agent").

---

## Decisions

The following questions were raised during design and have been resolved:

1. **Authorization granularity** — `TaskHub.JoinTask` will verify project membership (not just `[Authorize]`). This is consistent with `LoungeHub.CanAccessRoom` and prevents unauthorized users from subscribing to task updates by guessing task IDs.

2. **Human-initiated double-update** — Ignored. When a human changes status via the form buttons, the page refreshes from the POST redirect. The SignalR event also arrives, triggering a redundant htmx fetch on the reloaded page. This is harmless and invisible to the user. Suppressing it would add complexity for no visible benefit.

3. **Scope of events** — Limited to actions agents can perform: **status changes** and **comments**. Agents cannot change assignments, so assignees are excluded. GTD flag toggles, priority edits, and other property changes are human-only actions that already cause page refreshes. The activity section is refreshed as a side-effect whenever status or comments change. Additional sections can be added later if needed.

4. **Connection lifecycle** — The SignalR connection is established only on the Task Details page (`task-realtime.js` checks for `data-task-id`). A global connection could serve future dashboard real-time needs but is unnecessary complexity now. Starting per-page keeps the implementation focused and can be evolved later.

---

## Files to Create or Modify

| File | Change |
|---|---|
| `Hubs/TaskHub.cs` | **New** — Hub with `JoinTask` / `LeaveTask`, project membership authorization, `GetGroupName` static helper |
| `Program.cs` | Register `app.MapHub<TaskHub>("/hubs/task")` |
| `wwwroot/js/task-realtime.js` | **New** — SignalR connection, `TaskUpdated` handler, htmx-triggered partial fetches |
| `Views/Task/Details.cshtml` | Add `data-task-id`, wrap sections with `id` + `data-partial-url`, extract inline markup into partials, add `#task-toast-container`, add script reference |
| `Views/Task/_StatusSection.cshtml` | **New** — extracted status/priority/GTD badges from `Details.cshtml` |
| `Views/Task/_ActivityHistory.cshtml` | **New** — extracted activity log list from `Details.cshtml` |
| `Controllers/TaskController.cs` | Inject `IHubContext<TaskHub>`, add `StatusPartial` / `ActivityPartial` / `CommentsPartial` endpoints, broadcast `TaskUpdated` after `ChangeStatus` |
| `Controllers/CommentController.cs` | Inject `IHubContext<TaskHub>`, broadcast `TaskUpdated` after `Add` / `Edit` / `Delete` |
| `Mcp/Tools/TaskTools.cs` | Inject `IHubContext<TaskHub>`, broadcast `TaskUpdated` after `update_task_status` / `add_comment` |

---

## Considered Alternatives

### Idea 1: Pure SignalR Push with Client-Side DOM Construction

Push full data payloads through SignalR and build HTML elements in JavaScript (similar to how `lounge.js` constructs chat message DOM elements).

**Rejected because:** Comments have markdown rendering, timestamps with `Html.LocalTime`, bot badges, edit/delete buttons, and attachment links. Activity entries have color-coded icons and formatted descriptions. Reconstructing this markup in JavaScript would duplicate the Razor partial logic and diverge over time as the UI evolves. The lounge gets away with JS-based rendering because message markup is simpler and more uniform.

### Idea 3: Hybrid Push + Fetch

Push data for simple elements (status badge text), fetch partials for complex ones (comments, assignees).

**Rejected because:** Maintaining two different update patterns on one page adds cognitive load for a marginal performance benefit. The htmx round-trip is negligible for a self-hosted app, and consistency (always fetch partials) is easier to reason about and maintain.
