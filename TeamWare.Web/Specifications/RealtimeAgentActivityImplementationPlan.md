# TeamWare - Real-Time Agent Activity Implementation Plan

This document defines the phased implementation plan for real-time agent activity updates on the Task Details page, based on the [Real-Time Agent Activity Design](RealtimeAgentActivity.md). Each phase builds on the previous one and is broken into work items suitable for GitHub Issues.

---

## Progress Summary

| Phase | Description | Status |
|-------|------------|--------|
| 49 | Task SignalR Hub and Server Infrastructure | Not Started |
| 50 | Client Integration and Polish | Not Started |

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

- [ ] Create `TaskHub` class in `TeamWare.Web/Hubs/TaskHub.cs`
  - [ ] Inherit from `Hub`
  - [ ] Add `[Authorize]` attribute (consistent with `LoungeHub`)
  - [ ] Implement `static string GetGroupName(int taskId)` returning `$"task-{taskId}"`
  - [ ] Implement `JoinTask(int taskId)`:
    - [ ] Resolve the authenticated user's ID from `Context.User`
    - [ ] Query `ProjectMembers` to verify the user is a member of the project that owns the task (mirrors `LoungeHub.CanAccessRoom`)
    - [ ] If authorized, add connection to group via `Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(taskId))`
    - [ ] If not authorized, throw `HubException` with access denied message
  - [ ] Implement `LeaveTask(int taskId)`:
    - [ ] Remove connection from group via `Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupName(taskId))`
- [ ] Register hub endpoint in `Program.cs`: `app.MapHub<TaskHub>("/hubs/task")`
- [ ] Write unit tests for `TaskHub`:
  - [ ] `JoinTask` succeeds for project member
  - [ ] `JoinTask` fails for non-member
  - [ ] `LeaveTask` succeeds without error
  - [ ] `GetGroupName` returns expected format

### 49.2 Partial View Extraction

- [ ] Create `_StatusSection.cshtml` in `TeamWare.Web/Views/Task/`
  - [ ] Extract the status badge, priority badge, Next Action badge, and Someday/Maybe badge markup from `Details.cshtml` (lines 46–57)
  - [ ] Accept a view model or `TaskDetailViewModel` with `Status`, `Priority`, `IsNextAction`, `IsSomedayMaybe`
  - [ ] Include the Tailwind CSS class computation for status and priority (currently in the `@{ }` block at the top of `Details.cshtml`)
- [ ] Create `_ActivityHistory.cshtml` in `TeamWare.Web/Views/Task/`
  - [ ] Extract the activity history `<div>` from `Details.cshtml` (lines 172–219)
  - [ ] Accept `List<ActivityLogEntryViewModel>` (or equivalent from `TaskDetailViewModel.ActivityHistory`)
  - [ ] Include the `ActivityChangeType` → icon class mapping
  - [ ] Include the `_BotBadge` partial reference and `LocalTime` helper
- [ ] Update `Details.cshtml` to render these sections via `@await Html.PartialAsync(...)` instead of inline markup
  - [ ] Verify the existing `_CommentList.cshtml` partial is already used for the comments section (no extraction needed)
- [ ] Write rendering tests:
  - [ ] `_StatusSection` renders correct badges for each status/priority combination
  - [ ] `_StatusSection` renders Next Action and Someday/Maybe badges when applicable
  - [ ] `_ActivityHistory` renders activity entries with correct icon classes
  - [ ] `_ActivityHistory` renders empty state message when no activity
  - [ ] `Details.cshtml` continues to render identically after partial extraction

### 49.3 Partial Endpoints

- [ ] Add `StatusPartial` action to `TaskController`:
  - [ ] `[HttpGet]` at route `Task/StatusPartial`
  - [ ] Accept `int id` parameter
  - [ ] Load task via `ITaskService`, verify user access
  - [ ] Return `PartialView("_StatusSection", ...)` with the relevant view model data
- [ ] Add `ActivityPartial` action to `TaskController`:
  - [ ] `[HttpGet]` at route `Task/ActivityPartial`
  - [ ] Accept `int id` parameter
  - [ ] Load task with activity history, verify user access
  - [ ] Return `PartialView("_ActivityHistory", ...)` with activity log entries
- [ ] Add `CommentsPartial` action to `TaskController`:
  - [ ] `[HttpGet]` at route `Task/CommentsPartial`
  - [ ] Accept `int id` parameter
  - [ ] Load task with comments, verify user access
  - [ ] Return `PartialView("_CommentList", ...)` with comment data
- [ ] Write tests for each partial endpoint:
  - [ ] Returns partial HTML for valid task and authorized user
  - [ ] Returns 404 or error for nonexistent task
  - [ ] Returns 403 or redirect for unauthorized user

### 49.4 Broadcast Integration

- [ ] Inject `IHubContext<TaskHub>` into `TaskController`
  - [ ] After `ChangeStatus` succeeds, broadcast `TaskUpdated` to `TaskHub.GetGroupName(id)` with `sections: ["status", "activity"]` and summary `"{displayName} changed status to {status}"`
- [ ] Inject `IHubContext<TaskHub>` into `CommentController`
  - [ ] After `Add` succeeds, broadcast `TaskUpdated` with `sections: ["comments", "activity"]` and summary `"{displayName} added a comment"`
  - [ ] After `Edit` succeeds, broadcast `TaskUpdated` with `sections: ["comments", "activity"]` and summary `"{displayName} edited a comment"`
  - [ ] After `Delete` succeeds, broadcast `TaskUpdated` with `sections: ["comments", "activity"]` and summary `"{displayName} deleted a comment"`
- [ ] Inject `IHubContext<TaskHub>` into `TaskTools` (MCP tools)
  - [ ] After `update_task_status` succeeds, broadcast `TaskUpdated` with `sections: ["status", "activity"]` and summary `"{displayName} changed status to {status}"`
  - [ ] After `add_comment` succeeds, broadcast `TaskUpdated` with `sections: ["comments", "activity"]` and summary `"{displayName} added a comment"`
- [ ] Resolve `displayName` from the authenticated user's claims or `ApplicationUser` record at each call site
- [ ] The `TaskUpdated` event payload shape: `{ taskId: int, sections: string[], summary: string }`
- [ ] Write tests for broadcast integration:
  - [ ] `ChangeStatus` sends `TaskUpdated` with correct sections and summary
  - [ ] `Add` comment sends `TaskUpdated` with correct sections and summary
  - [ ] `update_task_status` MCP tool sends `TaskUpdated` with correct sections and summary
  - [ ] `add_comment` MCP tool sends `TaskUpdated` with correct sections and summary
  - [ ] Failed mutations do not broadcast

---

## Phase 50: Client Integration and Polish

Create the client-side JavaScript, wire up `Details.cshtml` with data attributes and script references, implement toast notifications, and perform end-to-end testing.

### 50.1 Client-Side SignalR Script

- [ ] Create `wwwroot/js/task-realtime.js`
  - [ ] On page load, find the element with `data-task-id` attribute; if absent, exit early (script is safe to include on any page)
  - [ ] Read `taskId` from the `data-task-id` attribute
  - [ ] Read partial URLs from `data-partial-url` attributes on each section container:
    - [ ] `#task-status-section[data-partial-url]`
    - [ ] `#comments-section[data-partial-url]`
    - [ ] `#task-activity-section[data-partial-url]`
  - [ ] Build SignalR connection to `/hubs/task` using `HubConnectionBuilder` with `withAutomaticReconnect()` (mirrors `lounge.js` pattern)
  - [ ] On connection start, invoke `hub.JoinTask(taskId)`
  - [ ] Register handler for `TaskUpdated` event:
    - [ ] Receive `{ taskId, sections[], summary }`
    - [ ] For each section in `sections[]`, find the target element by ID and trigger `htmx.ajax("GET", url, { target: element, swap: "innerHTML" })`
    - [ ] If `summary` is present, call `showToast(summary)`
  - [ ] Handle reconnection: re-invoke `JoinTask(taskId)` on reconnect (via `onreconnected` callback)
  - [ ] Implement 200ms debounce: if multiple `TaskUpdated` events arrive in rapid succession, collect all section names within the window and fetch each unique section once
- [ ] Write unit tests (or manual test plan) for:
  - [ ] Script exits cleanly when `data-task-id` is absent
  - [ ] SignalR connection is established and `JoinTask` is invoked
  - [ ] `TaskUpdated` triggers htmx fetches for the correct sections
  - [ ] Reconnection re-joins the task group

### 50.2 Details.cshtml Integration

- [ ] Add `data-task-id="@Model.Id"` to the page container element (outermost `<div>` or a dedicated wrapper)
- [ ] Wrap the status badges section in a container with `id="task-status-section"` and `data-partial-url="@Url.Action("StatusPartial", "Task", new { id = Model.Id })"`
  - [ ] Render the extracted `_StatusSection` partial inside this container
- [ ] Ensure the comments section has `id="comments-section"` and add `data-partial-url="@Url.Action("CommentsPartial", "Task", new { id = Model.Id })"`
- [ ] Wrap the activity history section in a container with `id="task-activity-section"` and `data-partial-url="@Url.Action("ActivityPartial", "Task", new { id = Model.Id })"`
  - [ ] Render the extracted `_ActivityHistory` partial inside this container
- [ ] Add the toast container element:
  ```html
  <div id="task-toast-container"
       class="fixed bottom-4 right-4 z-50 flex flex-col gap-2 max-w-sm">
  </div>
  ```
- [ ] Add script references in the `@section Scripts` block:
  ```html
  <script src="~/js/signalr/dist/browser/signalr.min.js"></script>
  <script src="~/js/task-realtime.js"></script>
  ```
- [ ] Verify that the SignalR client library (`signalr.min.js`) is already available in `wwwroot/js/signalr/` (used by the lounge); if not, install via `libman` or npm
- [ ] Write rendering tests:
  - [ ] `data-task-id` attribute is present with the correct task ID
  - [ ] Each section container has the correct `id` and `data-partial-url`
  - [ ] Toast container is present
  - [ ] Script references are included

### 50.3 Toast Notifications

- [ ] Implement `showToast(summary)` function in `task-realtime.js`:
  - [ ] Create a `<div>` element styled to match `_Notification.cshtml` info variant:
    - [ ] Blue background: `bg-blue-50 text-blue-800 dark:bg-blue-900/30 dark:text-blue-300`
    - [ ] Rounded, shadow, padding: `rounded-md px-4 py-3 text-sm shadow-lg`
    - [ ] Transition classes for fade-in/fade-out: `transition ease-in duration-300`
  - [ ] Set inner text to the `summary` string
  - [ ] Add a close button (`×`) with `aria-label="Dismiss"` that removes the element on click
  - [ ] Append to `#task-toast-container`
  - [ ] Auto-dismiss after 5 seconds: fade out and remove the element
  - [ ] Stacking: multiple toasts stack vertically (newest at bottom), each dismisses independently
- [ ] Write tests for toast behavior:
  - [ ] Toast appears with correct text
  - [ ] Toast auto-dismisses after timeout
  - [ ] Close button removes toast immediately
  - [ ] Multiple toasts stack correctly

### 50.4 End-to-End Testing and Polish

- [ ] Write integration tests for the full flow:
  - [ ] Agent calls `update_task_status` via MCP → browser receives `TaskUpdated` → status section refreshes
  - [ ] Agent calls `add_comment` via MCP → browser receives `TaskUpdated` → comments and activity sections refresh
  - [ ] Human changes status via web UI → other connected browsers receive `TaskUpdated`
  - [ ] Human adds comment via web UI → other connected browsers receive `TaskUpdated`
- [ ] Verify no regressions:
  - [ ] Task Details page renders identically when no SignalR connection is active (progressive enhancement)
  - [ ] Existing htmx comment add form continues to work
  - [ ] Page load performance is not degraded
- [ ] Verify authorization edge cases:
  - [ ] Non-member cannot join task group via SignalR
  - [ ] Partial endpoints return 403/redirect for unauthorized users
  - [ ] Revoked PAT does not receive broadcasts
- [ ] Verify dark mode styling:
  - [ ] Toast notifications render correctly in dark mode
  - [ ] Refreshed partials maintain dark mode classes
- [ ] Verify the human-initiated double-update is harmless:
  - [ ] When a human changes status, the POST redirect reloads the page; the SignalR event also arrives and triggers a redundant htmx fetch on the reloaded page — this is invisible and benign
- [ ] Document the feature in `RealtimeAgentActivity.md` (mark as implemented, note any deviations from the design)

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
