# TeamWare - Real-Time Agent Activity Implementation Plan

This document defines the phased implementation plan for real-time agent activity updates on the Task Details page, based on the [Real-Time Agent Activity Design](RealtimeAgentActivity.md). Each phase builds on the previous one and is broken into work items suitable for GitHub Issues.

---

## Progress Summary

| Phase | Description | Status |
|-------|------------|--------|
| 49 | Task SignalR Hub and Server Infrastructure | Complete |
| 50 | Client Integration and Polish | Complete |

---

## Current State

All previous phases (0–48) are complete. The workspace includes:

- Full task management with status changes, comments, and activity logging
- SignalR infrastructure with `LoungeHub` (real-time chat) and `PresenceHub` (online/offline presence)
- htmx used on the Task Details page for inline comment additions
- MCP tools (`TaskTools.update_task_status`, `TaskTools.add_comment`) that agents use to interact with tasks
- Alpine.js toast notifications in `_Notification.cshtml` with auto-dismiss and theming
- Tailwind CSS with light/dark mode throughout

The problem: when an agent changes a task's status or adds a comment via MCP tools, the browser user must manually refresh the Task Details page to see the update. This feature adds real-time push notifications so agent (and human) actions appear immediately.

---

## Guiding Principles

All guiding principles from previous implementation plans continue to apply:

1. **Vertical slices** — Each phase delivers end-to-end working functionality.
2. **Tests accompany every feature** — No phase is complete without its test cases.
3. **One type per file** — Enforced throughout (MAINT-01).
4. **MVC only** — Controllers and Views, no Razor Pages.

Additionally:

5. **Reuse proven patterns** — `TaskHub` mirrors `LoungeHub` for group management and authorization. `task-realtime.js` mirrors `lounge.js` for SignalR client setup. Toast styling matches `_Notification.cshtml`.
6. **Server-rendered partials** — All HTML rendering stays in Razor. SignalR is a notification channel only; htmx fetches server-rendered partials for DOM updates. No client-side template duplication.
7. **Minimal scope** — Only the Task Details page is affected. Only agent-actionable mutations (status changes, comments) are broadcast. Assignee changes, GTD flag toggles, and other human-only actions are excluded.

---

## Phase 49: Task SignalR Hub and Server Infrastructure

Create the `TaskHub`, extract partial views from `Details.cshtml`, add partial endpoints to `TaskController`, and wire up broadcast calls in all mutation sites.

### 49.1 TaskHub Creation

- [x] Create `TaskHub` class in `TeamWare.Web/Hubs/TaskHub.cs`
  - [x] Inherit from `Hub`
  - [x] Add `[Authorize]` attribute (consistent with `LoungeHub`)
  - [x] Implement `static string GetGroupName(int taskId)` returning `$"task-{taskId}"`
  - [x] Implement `JoinTask(int taskId)`:
    - [x] Resolve the authenticated user's ID from `Context.User`
    - [x] Query `ProjectMembers` to verify the user is a member of the project that owns the task (mirrors `LoungeHub.CanAccessRoom`)
    - [x] If authorized, add connection to group via `Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(taskId))`
    - [x] If not authorized, throw `HubException` with access denied message
  - [x] Implement `LeaveTask(int taskId)`:
    - [x] Remove connection from group via `Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupName(taskId))`
- [x] Register hub endpoint in `Program.cs`: `app.MapHub<TaskHub>("/hubs/task")`
- [x] Write unit tests for `TaskHub`:
  - [x] `JoinTask` succeeds for project member
  - [x] `JoinTask` fails for non-member
  - [x] `LeaveTask` succeeds without error
  - [x] `GetGroupName` returns expected format

### 49.2 Partial View Extraction

- [x] Create `_StatusSection.cshtml` in `TeamWare.Web/Views/Task/`
  - [x] Extract the status badge, priority badge, Next Action badge, and Someday/Maybe badge markup from `Details.cshtml` (lines 46–57)
  - [x] Accept a view model or `TaskDetailViewModel` with `Status`, `Priority`, `IsNextAction`, `IsSomedayMaybe`
  - [x] Include the Tailwind CSS class computation for status and priority (currently in the `@{ }` block at the top of `Details.cshtml`)
- [x] Create `_ActivityHistory.cshtml` in `TeamWare.Web/Views/Task/`
  - [x] Extract the activity history `<div>` from `Details.cshtml` (lines 172–219)
  - [x] Accept `List<ActivityLogEntryViewModel>` (or equivalent from `TaskDetailViewModel.ActivityHistory`)
  - [x] Include the `ActivityChangeType` → icon class mapping
  - [x] Include the `_BotBadge` partial reference and `LocalTime` helper
- [x] Update `Details.cshtml` to render these sections via `@await Html.PartialAsync(...)` instead of inline markup
  - [x] Verify the existing `_CommentList.cshtml` partial is already used for the comments section (no extraction needed)
- [x] Write rendering tests:
  - [x] `_StatusSection` renders correct badges for each status/priority combination
  - [x] `_StatusSection` renders Next Action and Someday/Maybe badges when applicable
  - [x] `_ActivityHistory` renders activity entries with correct icon classes
  - [x] `_ActivityHistory` renders empty state message when no activity
  - [x] `Details.cshtml` continues to render identically after partial extraction

### 49.3 Partial Endpoints

- [x] Add `StatusPartial` action to `TaskController`:
  - [x] `[HttpGet]` at route `Task/StatusPartial`
  - [x] Accept `int id` parameter
  - [x] Load task via `ITaskService`, verify user access
  - [x] Return `PartialView("_StatusSection", ...)` with the relevant view model data
- [x] Add `ActivityPartial` action to `TaskController`:
  - [x] `[HttpGet]` at route `Task/ActivityPartial`
  - [x] Accept `int id` parameter
  - [x] Load task with activity history, verify user access
  - [x] Return `PartialView("_ActivityHistory", ...)` with activity log entries
- [x] Add `CommentsPartial` action to `TaskController`:
  - [x] `[HttpGet]` at route `Task/CommentsPartial`
  - [x] Accept `int id` parameter
  - [x] Load task with comments, verify user access
  - [x] Return `PartialView("_CommentList", ...)` with comment data
- [x] Write tests for each partial endpoint:
  - [x] Returns partial HTML for valid task and authorized user
  - [x] Returns 404 or error for nonexistent task
  - [x] Returns 403 or redirect for unauthorized user

### 49.4 Broadcast Integration

- [x] Inject `IHubContext<TaskHub>` into `TaskController`
  - [x] After `ChangeStatus` succeeds, broadcast `TaskUpdated` to `TaskHub.GetGroupName(id)` with `sections: ["status", "activity"]` and summary `"{displayName} changed status to {status}"`
- [x] Inject `IHubContext<TaskHub>` into `CommentController`
  - [x] After `Add` succeeds, broadcast `TaskUpdated` with `sections: ["comments", "activity"]` and summary `"{displayName} added a comment"`
  - [x] After `Edit` succeeds, broadcast `TaskUpdated` with `sections: ["comments", "activity"]` and summary `"{displayName} edited a comment"`
  - [x] After `Delete` succeeds, broadcast `TaskUpdated` with `sections: ["comments", "activity"]` and summary `"{displayName} deleted a comment"`
- [x] Inject `IHubContext<TaskHub>` into `TaskTools` (MCP tools)
  - [x] After `update_task_status` succeeds, broadcast `TaskUpdated` with `sections: ["status", "activity"]` and summary `"{displayName} changed status to {status}"`
  - [x] After `add_comment` succeeds, broadcast `TaskUpdated` with `sections: ["comments", "activity"]` and summary `"{displayName} added a comment"`
- [x] Resolve `displayName` from the authenticated user's claims or `ApplicationUser` record at each call site
- [x] The `TaskUpdated` event payload shape: `{ taskId: int, sections: string[], summary: string }`
- [x] Write tests for broadcast integration:
  - [x] `ChangeStatus` sends `TaskUpdated` with correct sections and summary
  - [x] `Add` comment sends `TaskUpdated` with correct sections and summary
  - [x] `update_task_status` MCP tool sends `TaskUpdated` with correct sections and summary
  - [x] `add_comment` MCP tool sends `TaskUpdated` with correct sections and summary
  - [x] Failed mutations do not broadcast

---

## Phase 50: Client Integration and Polish

Create the client-side JavaScript, wire up `Details.cshtml` with data attributes and script references, implement toast notifications, and perform end-to-end testing.

### 50.1 Client-Side SignalR Script

- [x] Create `wwwroot/js/task-realtime.js`
  - [x] On page load, find the element with `data-task-id` attribute; if absent, exit early (script is safe to include on any page)
  - [x] Read `taskId` from the `data-task-id` attribute
  - [x] Read partial URLs from `data-partial-url` attributes on each section container:
    - [x] `#task-status-section[data-partial-url]`
    - [x] `#comments-section[data-partial-url]`
    - [x] `#task-activity-section[data-partial-url]`
  - [x] Build SignalR connection to `/hubs/task` using `HubConnectionBuilder` with `withAutomaticReconnect()` (mirrors `lounge.js` pattern)
  - [x] On connection start, invoke `hub.JoinTask(taskId)`
  - [x] Register handler for `TaskUpdated` event:
    - [x] Receive `{ taskId, sections[], summary }`
    - [x] For each section in `sections[]`, find the target element by ID and trigger `htmx.ajax("GET", url, { target: element, swap: "innerHTML" })`
    - [x] If `summary` is present, call `showToast(summary)`
  - [x] Handle reconnection: re-invoke `JoinTask(taskId)` on reconnect (via `onreconnected` callback)
  - [x] Implement 200ms debounce: if multiple `TaskUpdated` events arrive in rapid succession, collect all section names within the window and fetch each unique section once
- [x] Write unit tests (or manual test plan) for:
  - [x] Script exits cleanly when `data-task-id` is absent
  - [x] SignalR connection is established and `JoinTask` is invoked
  - [x] `TaskUpdated` triggers htmx fetches for the correct sections
  - [x] Reconnection re-joins the task group

### 50.2 Details.cshtml Integration

- [x] Add `data-task-id="@Model.Id"` to the page container element (outermost `<div>` or a dedicated wrapper)
- [x] Wrap the status badges section in a container with `id="task-status-section"` and `data-partial-url="@Url.Action("StatusPartial", "Task", new { id = Model.Id })"`
  - [x] Render the extracted `_StatusSection` partial inside this container
- [x] Ensure the comments section has `id="comments-section"` and add `data-partial-url="@Url.Action("CommentsPartial", "Task", new { id = Model.Id })"`
- [x] Wrap the activity history section in a container with `id="task-activity-section"` and `data-partial-url="@Url.Action("ActivityPartial", "Task", new { id = Model.Id })"`
  - [x] Render the extracted `_ActivityHistory` partial inside this container
- [x] Add the toast container element:
  ```html
  <div id="task-toast-container"
       class="fixed bottom-4 right-4 z-50 flex flex-col gap-2 max-w-sm">
  </div>
  ```
- [x] Add script references in the `@section Scripts` block:
  ```html
  <script src="~/js/signalr/dist/browser/signalr.min.js"></script>
  <script src="~/js/task-realtime.js"></script>
  ```
- [x] Verify that the SignalR client library (`signalr.min.js`) is already available in `wwwroot/js/signalr/` (used by the lounge); if not, install via `libman` or npm
- [x] Write rendering tests:
  - [x] `data-task-id` attribute is present with the correct task ID
  - [x] Each section container has the correct `id` and `data-partial-url`
  - [x] Toast container is present
  - [x] Script references are included

### 50.3 Toast Notifications

- [x] Implement `showToast(summary)` function in `task-realtime.js`:
  - [x] Create a `<div>` element styled to match `_Notification.cshtml` info variant:
    - [x] Blue background: `bg-blue-50 text-blue-800 dark:bg-blue-900/30 dark:text-blue-300`
    - [x] Rounded, shadow, padding: `rounded-md px-4 py-3 text-sm shadow-lg`
    - [x] Transition classes for fade-in/fade-out: `transition ease-in duration-300`
  - [x] Set inner text to the `summary` string
  - [x] Add a close button (`×`) with `aria-label="Dismiss"` that removes the element on click
  - [x] Append to `#task-toast-container`
  - [x] Auto-dismiss after 5 seconds: fade out and remove the element
  - [x] Stacking: multiple toasts stack vertically (newest at bottom), each dismisses independently
- [x] Write tests for toast behavior:
  - [x] Toast appears with correct text
  - [x] Toast auto-dismisses after timeout
  - [x] Close button removes toast immediately
  - [x] Multiple toasts stack correctly

### 50.4 End-to-End Testing and Polish

- [x] Write integration tests for the full flow:
  - [x] Agent calls `update_task_status` via MCP → browser receives `TaskUpdated` → status section refreshes
  - [x] Agent calls `add_comment` via MCP → browser receives `TaskUpdated` → comments and activity sections refresh
  - [x] Human changes status via web UI → other connected browsers receive `TaskUpdated`
  - [x] Human adds comment via web UI → other connected browsers receive `TaskUpdated`
- [x] Verify no regressions:
  - [x] Task Details page renders identically when no SignalR connection is active (progressive enhancement)
  - [x] Existing htmx comment add form continues to work
  - [x] Page load performance is not degraded
- [x] Verify authorization edge cases:
  - [x] Non-member cannot join task group via SignalR
  - [x] Partial endpoints return 403/redirect for unauthorized users
  - [x] Revoked PAT does not receive broadcasts
- [x] Verify dark mode styling:
  - [x] Toast notifications render correctly in dark mode
  - [x] Refreshed partials maintain dark mode classes
- [x] Verify the human-initiated double-update is harmless:
  - [x] When a human changes status, the POST redirect reloads the page; the SignalR event also arrives and triggers a redundant htmx fetch on the reloaded page — this is invisible and benign
- [x] Document the feature in `RealtimeAgentActivity.md` (mark as implemented, note any deviations from the design)

---

## Files to Create or Modify

| File | Phase | Change |
|---|---|---|
| `Hubs/TaskHub.cs` | 49.1 | **New** — Hub with `JoinTask`/`LeaveTask`, project membership authorization, `GetGroupName` static helper |
| `Program.cs` | 49.1 | Register `app.MapHub<TaskHub>("/hubs/task")` |
| `Views/Task/_StatusSection.cshtml` | 49.2 | **New** — Extracted status/priority/GTD badges from `Details.cshtml` |
| `Views/Task/_ActivityHistory.cshtml` | 49.2 | **New** — Extracted activity log list from `Details.cshtml` |
| `Views/Task/Details.cshtml` | 49.2, 50.2 | Replace inline markup with partial calls, add `data-task-id`, section wrappers with `data-partial-url`, toast container, script references |
| `Controllers/TaskController.cs` | 49.3, 49.4 | Add `StatusPartial`/`ActivityPartial`/`CommentsPartial` endpoints, inject `IHubContext<TaskHub>`, broadcast after `ChangeStatus` |
| `Controllers/CommentController.cs` | 49.4 | Inject `IHubContext<TaskHub>`, broadcast after `Add`/`Edit`/`Delete` |
| `Mcp/Tools/TaskTools.cs` | 49.4 | Inject `IHubContext<TaskHub>`, broadcast after `update_task_status`/`add_comment` |
| `wwwroot/js/task-realtime.js` | 50.1 | **New** — SignalR connection, `TaskUpdated` handler, htmx-triggered partial fetches, `showToast` |
