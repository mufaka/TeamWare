# Idea: Enhanced Inbox Workflow with Subtask System — REJECTED

**Task:** [#76](https://teamware.example.com/tasks/76)
**Priority:** Medium
**Context:** Improving the inbox-to-task workflow to support complex work items that require multiple stages of refinement before implementation
**Status:** ❌ **Rejected** — The scope of changes required (15 identified gaps spanning data model, status logic, every query surface, MCP tools, SignalR, and the CopilotAgent) far exceeds the benefit for the use case. The current inbox-to-task workflow is sufficient.

---

## Problem Statement

The current TeamWare inbox workflow is optimized for simple, well-defined tasks:

1. **Capture** → An item lands in the inbox (`InboxItem`)
2. **Process** → Convert to a `TaskItem` in a project with additional details
3. **Execute** → Task is assigned and worked on

This works well for straightforward items but breaks down when:

- An inbox item represents a complex idea that needs exploration, specification, planning, and implementation
- Multiple people need to collaborate on different phases (e.g., designer for spec, architect for plan, developer for implementation)
- Earlier phases must complete before later ones can begin
- The same inbox item needs to flow through a formal workflow: **Idea → Spec → Plan → Implement**

### Current Architecture Constraints

**Strengths:**
- Simple data model: `InboxItem` → `TaskItem` → `TaskAssignment`
- Clear ownership via `TaskAssignment` (many-to-many: task ↔ users)
- Project-scoped tasks with status tracking (`ToDo`, `InProgress`, `InReview`, `Done`, `Blocked`, `Error`)
- Activity logging and notifications already in place
- MCP server with task/inbox tools consumed by external agents and the CopilotAgent
- Real-time SignalR `TaskHub` for live task updates

**Limitations:**
- No way to assign users during inbox processing (only after conversion to task)
- No formal workflow stages beyond task status
- No parent-child task relationships
- No enforcement of sequential dependencies between work items
- No way to track "this is an idea that needs a spec before implementation"

---

## Chosen Direction: Subtask System

After evaluating three approaches (see [Also Considered](#also-considered) below), the **subtask system** provides the best balance of capability and complexity for this feature. It introduces a formal parent-child task hierarchy where an inbox item converts to a *parent task* with multiple *subtasks* representing workflow stages, with automatic sequential dependency enforcement.

### Why Subtasks

- Gives us parent-child relationships and automatic dependency enforcement without the weight of a full workflow engine
- Assign different users to different stages upfront during inbox processing
- A single parent task provides clear visibility into the entire workflow
- Subtasks are still full `TaskItem` entities — they get comments, assignments, activity logging, and notifications for free
- Ad-hoc subtasks can be added later without requiring a template
- Fully backward compatible — existing tasks with no parent simply behave as they do today

### Proposed Data Model Changes

Add self-referential parent-child support to `TaskItem`:

```csharp
public class TaskItem
{
    // ... existing fields ...

    public int? ParentTaskId { get; set; }

    public TaskItem? ParentTask { get; set; }

    public ICollection<TaskItem> Subtasks { get; set; } = new List<TaskItem>();

    public int? SequenceOrder { get; set; }  // For ordering subtasks within a parent
}
```

### Proposed Service Layer

New `ISubtaskService` with methods:
- `CreateSubtask(parentTaskId, title, assignees, sequenceOrder)`
- `GetSubtasks(parentTaskId)`
- `GetBlockingSubtask(taskId)` → Returns the previous subtask that must complete first
- `CanStartSubtask(subtaskId)` → Checks if prior subtask is `Done`

Enhanced `InboxService` with new conversion method:
- `ConvertToWorkflow(inboxItemId, projectId, stages[])` → Creates parent + ordered subtasks

### Use Case Example

**Inbox Item:** "Add dark mode support"
**Processing (via new UI):**
1. Click "Convert to Workflow"
2. UI shows stage builder:
   - Stage 1: Assign Sarah → Title: "Design dark mode UI"
   - Stage 2: Assign John → Title: "Architect dark mode system"
   - Stage 3: Assign Alex → Title: "Implement dark mode"
3. Submit → Creates parent + 3 subtasks
4. Sarah sees "Design dark mode UI" in her tasks (status=ToDo)
5. John sees "Architect dark mode system" (status=Blocked, blocked by Sarah's task)
6. Sarah completes her task → John's task automatically becomes `ToDo`, John gets notification
7. John completes → Alex's task unblocks
8. Alex completes → Parent task automatically marked `Done`

---

## Discovered Gaps

A review of the existing codebase against this proposal surfaced the following gaps that need team input before a specification can be written. They are grouped by category.

---

### Data Model & Schema

#### Gap 1: Self-Referential FK Cascade Strategy

SQL Server does not allow `ON DELETE CASCADE` on self-referencing foreign keys. The EF Core migration will fail if we configure `DeleteBehavior.Cascade` on the `ParentTaskId` relationship. Deletion of a parent and its subtasks must be handled in application code.

Additionally, when a parent task is deleted, the `InboxItem.ConvertedToTaskId` that references it would be left dangling (currently configured as `DeleteBehavior.SetNull`, so it would become `null` — but the inbox item loses its link to the work that was done).

> **Question:** Is application-level cascade delete (service loads subtasks, deletes them in a loop, then deletes parent) acceptable? Should we also clear or update `InboxItem.ConvertedToTaskId` as part of that flow, or is `SetNull` sufficient?

#### Gap 2: `InboxItem.ConvertedToTaskId` is Singular

The current `InboxItem` has a single `ConvertedToTaskId` FK. The proposal assumes this points to the parent task, but that's implicit. If the user later wants to trace "what tasks came from this inbox item?", they can only follow one link to the parent and then query its children.

> **Question:** Is pointing `ConvertedToTaskId` at the parent (container) task sufficient for traceability? Or do we need a one-to-many relationship (e.g., a new `InboxItem.ConvertedToTasks` collection)?

#### Gap 3: `WorkflowStage` Enum — Do We Need It?

The original proposal included a `WorkflowStage` enum (Idea, Specification, Planning, Implementation, Validation) on each subtask alongside a `SequenceOrder` integer. But `SequenceOrder` alone is sufficient for dependency ordering. The `WorkflowStage` enum is limiting — teams may want stages like "Legal Review," "Security Audit," or "Localization" that don't fit fixed categories.

> **Question:** Should subtasks simply have a title and `SequenceOrder` (no stage enum)? Or is there value in a categorization field? If so, should it be a free-text label rather than a fixed enum?

#### Gap 4: `IsNextAction` / `IsSomedayMaybe` on Parent Tasks

In GTD methodology, "next action" applies to a specific actionable item. A parent container task isn't itself actionable — its subtasks are. If a parent is marked `IsNextAction`, the semantics are unclear. Similarly, marking a parent `IsSomedayMaybe` could conflict with actively-sequenced subtasks underneath it.

> **Question:** Should `IsNextAction` and `IsSomedayMaybe` be restricted to leaf tasks (tasks with no subtasks)? Or should the flags propagate/be ignored on parents?

#### Gap 5: Nesting Depth — Can a Subtask Have Its Own Subtasks?

The proposal supports single-level parent-child. But what if a subtask like "Implement dark mode" itself needs to be broken into sub-items? Allowing arbitrary nesting adds significant complexity to queries, UI rendering, status propagation, and deletion.

> **Question:** Should we enforce single-level nesting only (a subtask cannot be a parent)? This is simpler and covers the primary use case. Deeper nesting could be a future enhancement if needed.

---

### Status & Dependency Logic

#### Gap 6: `Blocked` Status Semantics Are Overloaded

The existing `TaskItemStatus.Blocked` is a general-purpose status that any user can set manually for any reason (e.g., "waiting on external vendor"). The proposal reuses it for automatic sequential-dependency blocking. This creates ambiguity: if a subtask is `Blocked`, is it because a prior subtask isn't done, or because a human manually blocked it?

If a user manually blocks a subtask that was auto-blocked by a dependency, and then the dependency completes, should the system auto-unblock it (overriding the manual block) or leave it blocked?

> **Question:** How should we distinguish automatic dependency blocking from manual blocking? Options:
> - (A) Add a `BlockReason` field (enum: `None`, `DependencyNotMet`, `Manual`)
> - (B) Use a separate boolean `IsAutoBlocked` alongside the existing status
> - (C) Don't use `Blocked` status for dependencies at all — instead, prevent status transitions on subtasks whose predecessor isn't `Done` (enforcement at the service layer, not via status)

#### Gap 7: Parent Task Status Derivation Rules

The proposal says "parent is `Done` only when all subtasks are `Done`" but doesn't cover the full matrix:

| Subtask States | What Should Parent Status Be? |
|---|---|
| All `ToDo` | `ToDo`? |
| One `InProgress`, rest `ToDo`/`Blocked` | `InProgress`? |
| One `Error` | `Error`? |
| Some `Done`, one `InProgress` | `InProgress`? |
| Some `Done`, one `InReview` | `InReview`? |
| All `Done` | `Done` |
| Mix of `Done` and `Blocked` | ??? |

> **Question:** Should parent status be automatically derived from subtask states, or should it remain independent (manually set)? If derived, what are the rules? A simple approach: parent is `InProgress` if any subtask is not `ToDo` and not all are `Done`; parent is `Done` when all subtasks are `Done`.

#### Gap 8: Where Does Dependency Propagation Logic Live?

The current `TaskService.ChangeStatus` fires an activity log entry and sends notifications to assigned users. It has no extensibility point for side effects like unblocking sibling subtasks. The proposal mentions "background job or service logic" but doesn't specify the architecture.

> **Question:** Where should the "complete subtask N → unblock subtask N+1 → update parent status" logic live?
> - (A) Inline in `TaskService.ChangeStatus` (adds coupling to subtask logic in the general status-change path)
> - (B) A new `ISubtaskService` that wraps or is called after `ChangeStatus`
> - (C) A domain event / observer pattern (new pattern for the codebase)
> - (D) Called explicitly by the controller/MCP tool after `ChangeStatus` returns

#### Gap 9: Activity Log and Notifications for Automatic Transitions

When subtask N completing auto-unblocks subtask N+1, who is the `userId` for the activity log entry? `ActivityLogService.LogChange` requires a `userId` parameter. Automatic system transitions don't have an acting user.

Similarly, `NotificationType` has no value for "your task was automatically unblocked." The `ActivityChangeType` enum also lacks values for subtask-specific events (e.g., subtask created, subtask auto-unblocked).

> **Question:** How should system-initiated actions be attributed?
> - (A) Use the userId of the person who completed the predecessor subtask (they indirectly caused the unblock)
> - (B) Introduce a system/sentinel userId for automated actions
> - (C) Make `userId` nullable on `ActivityLogEntry` for system actions
>
> What new enum values are needed?
> - `ActivityChangeType`: `SubtaskCreated`, `AutoUnblocked`, `ParentStatusDerived`?
> - `NotificationType`: `SubtaskUnblocked`, `SubtaskCompleted`?

---

### Query & Display Surface Impact

#### Gap 10: `ProgressService` / `ProjectStatistics` Will Double-Count

`ProgressService.GetProjectStatistics` counts **all** `TaskItems` in a project grouped by status. If parent tasks exist alongside their subtasks, every metric — dashboard counts, the MCP `ProjectSummaryResource`, completion percentages — will be inflated. A parent with 3 subtasks would count as 4 tasks.

> **Question:** How should aggregate counts handle the hierarchy?
> - (A) Exclude parent tasks from counts (only count leaf tasks)
> - (B) Exclude subtasks from counts (only count top-level tasks, using derived status)
> - (C) Count everything but provide separate "parent" and "subtask" breakdowns
> - (D) Let the user choose via a filter toggle

#### Gap 11: `GetWhatsNext` / "My Assignments" Behavior

`GetWhatsNext` returns all tasks assigned to a user, sorted by priority and due date. If the parent task is a pure container with no assignees, it won't appear. But if someone *is* assigned to the parent, it shows up alongside their subtasks.

> **Question:** Should parent tasks (tasks that have subtasks) appear in the "My Assignments" / "What's Next" view?
> - (A) Never — parent tasks are containers, only subtasks appear in personal views
> - (B) Always — let the user see both
> - (C) Only if the parent has no subtasks (i.e., it's being used as a plain task)
>
> Related: Can users be assigned to parent tasks at all, or only to subtasks?

#### Gap 12: Search (`SearchTasks`) Doesn't Account for Hierarchy

`TaskService.SearchTasks` searches title and description. If a user searches for "dark mode," they could get the parent task *and* all its subtasks. The result list would be confusing without visual hierarchy indicators. If results only show the parent, subtask content is invisible to search.

> **Question:** Should search results include both parent and subtask matches? If so, should they be grouped (parent shown with its matching subtasks indented beneath it)? Or should subtask results link back to their parent?

#### Gap 13: MCP Tool Surfaces

The codebase has MCP tools consumed by external agents and the CopilotAgent: `TaskTools` (`list_tasks`, `get_task`, `create_task`, `update_task_status`), `InboxTools` (`process_inbox_item`), and `ProjectSummaryResource`. None of these are aware of parent-child relationships.

> **Question:** What MCP changes are needed?
> - Should `list_tasks` expose `parentTaskId` and support a `parentTaskId` filter?
> - Should `get_task` return the subtask list in its response?
> - Do we need a new `create_subtask` tool, or is `create_task` with an optional `parentTaskId` sufficient?
> - How does the `CopilotAgent` polling loop (`AgentPollingLoop`) handle parent vs. subtask assignments? Should agents only be assigned to subtasks?
> - Should `process_inbox_item` gain an optional workflow conversion mode, or should that be a separate tool?

#### Gap 14: SignalR `TaskHub` and Real-Time Updates

The `TaskHub` broadcasts real-time updates scoped to individual task groups (`task-{taskId}`). If completing subtask N auto-unblocks subtask N+1, viewers of subtask N+1's detail page need a real-time push. The parent task's viewers also need to see updated status/progress. Currently there's no mechanism to broadcast to sibling or parent task groups from a single status change.

> **Question:** When a subtask status changes, should the broadcast also push updates to:
> - (A) The parent task group (so viewers of the parent see updated subtask status)
> - (B) The next sibling subtask group (so viewers see it unblock in real-time)
> - (C) Both
> - (D) Neither — let the client poll or refresh for sibling/parent updates

---

### Subtask Variants

#### Gap 15: Ad-Hoc Subtasks vs. Sequential Subtasks

The proposal says subtasks can be added ad-hoc later. But the sequential dependency model assumes subtasks have a `SequenceOrder` and that completion of N unblocks N+1. What happens when someone adds an ad-hoc subtask to an existing workflow?

- Does it get inserted into the sequence (requiring reordering)?
- Does it exist outside the sequence (no auto-blocking/unblocking)?
- Can a parent task have a mix of sequential and non-sequential subtasks?

> **Question:** Should we distinguish between "sequenced subtasks" (part of a dependency chain) and "unordered subtasks" (independent work items under the same parent)?
> - (A) All subtasks are sequenced — adding one inserts it into the chain
> - (B) All subtasks are unordered — dependencies are optional and manual
> - (C) Subtasks have a nullable `SequenceOrder` — if set, they participate in the chain; if null, they're independent
> - (D) Two separate concepts: "workflow steps" (sequenced) and "subtasks" (unordered)

---

## Use Case Walkthrough with Gaps Resolved

*This section illustrates how the feature works end-to-end. Open questions are noted inline — once the team answers the gaps above, this walkthrough can be updated and used as the basis for the specification.*

1. **Inbox capture:** User adds "Add dark mode support" to inbox.
2. **Inbox processing:** User clicks "Convert to Workflow." UI presents a stage builder where they define subtasks with titles, optional assignees, and sequence order.
3. **Task creation:** System creates a parent task (title from inbox item) and N subtasks. `InboxItem.ConvertedToTaskId` → parent task. First subtask status = `ToDo`, remaining = *[depends on Gap 6 decision]*.
4. **First subtask worked:** Sarah completes "Design dark mode UI." `ChangeStatus(Done)` is called → *[Gap 8: where does propagation run?]* → next subtask unblocks → *[Gap 9: who is the actor for the log entry?]* → parent status updates → *[Gap 7: derived or manual?]* → SignalR pushes to *[Gap 14: which groups?]*.
5. **User checks dashboard:** Project statistics show task counts → *[Gap 10: are parents counted?]*. Sarah checks "My Assignments" → *[Gap 11: does she see the parent?]*.
6. **Ad-hoc subtask added:** Mid-workflow, someone adds "Write migration guide" → *[Gap 15: sequenced or independent?]*.
7. **Final subtask completes:** Alex finishes implementation → parent auto-completes → *[Gap 7]*.
8. **Agent interaction:** CopilotAgent calls `my_assignments` via MCP → *[Gap 13: does it see the parent or just its subtask?]*.

---

## Also Considered

### Approach 1: Workflow Metadata on Tasks (Rejected — Too Limited)

Add `WorkflowStage` and `BlockedByTaskId` fields directly to `TaskItem` without introducing parent-child relationships.

**Why we passed:** This approach tracks workflow stages as informal metadata but doesn't represent multi-stage workflows as a cohesive unit. Users must manually create separate tasks for each stage and manually set "blocked by" links. There's no automatic dependency enforcement, no single-parent visibility, and no way to see the full workflow at a glance. It solves the "assign during inbox processing" problem but not the core workflow orchestration need.

### Approach 3: Formal Workflow Engine (Rejected — Over-Engineered)

Introduce dedicated `Workflow`, `WorkflowStep`, and `WorkflowInstance` entities decoupled from `TaskItem`, with reusable workflow templates.

**Why we passed:** This approach provides maximum flexibility (reusable templates, branching/parallel steps, workflow versioning, analytics) but at 7-10 days of implementation effort and significant schema/UI weight. It requires a template management UI, a workflow visualization view, and a separate conceptual model that users must learn. For a team that needs multi-stage inbox processing — not a full BPM engine — this is more machinery than the problem warrants. If we later need reusable templates, a lightweight "Approach 2.5" hybrid (JSON template storage that pre-fills subtasks) can be added on top of the subtask system without the full engine.

---

## Next Steps

1. **Team reviews this document** and provides feedback on the open questions (Gaps 1–15)
2. **Answers are documented inline** in this idea document via comments or direct edits
3. **Create a formal specification** based on the resolved decisions
4. **Break down implementation** into phased work items
5. **Implement, test, deploy** incrementally

---

## Appendix: Current Data Model Reference

### InboxItem (Current)
```csharp
public class InboxItem
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string? Description { get; set; }
    public InboxItemStatus Status { get; set; }  // Unprocessed, Processed, Dismissed
    public string UserId { get; set; }
    public ApplicationUser User { get; set; }
    public int? ConvertedToTaskId { get; set; }
    public TaskItem? ConvertedToTask { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### TaskItem (Current)
```csharp
public class TaskItem
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string? Description { get; set; }
    public TaskItemStatus Status { get; set; }  // ToDo, InProgress, InReview, Done, Blocked, Error
    public TaskItemPriority Priority { get; set; }
    public DateTime? DueDate { get; set; }
    public bool IsNextAction { get; set; }
    public bool IsSomedayMaybe { get; set; }
    public int ProjectId { get; set; }
    public Project Project { get; set; }
    public string CreatedByUserId { get; set; }
    public ApplicationUser CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<TaskAssignment> Assignments { get; set; }
    public ICollection<Comment> Comments { get; set; }
}
```

### TaskAssignment (Current)
```csharp
public class TaskAssignment
{
    public int Id { get; set; }
    public int TaskItemId { get; set; }
    public TaskItem TaskItem { get; set; }
    public string UserId { get; set; }
    public ApplicationUser User { get; set; }
    public DateTime AssignedAt { get; set; }
}
```

### Key Services Affected
- `TaskService` — `ChangeStatus`, `GetTasksForProject`, `GetWhatsNext`, `SearchTasks`, `DeleteTask`
- `InboxService` — `ConvertToTask`, `ClarifyItem`
- `ProgressService` — `GetProjectStatistics`
- `ActivityLogService` — `LogChange` (requires `userId`)
- `NotificationService` — `CreateNotification`
- `TaskTools` (MCP) — `list_tasks`, `get_task`, `create_task`, `update_task_status`
- `InboxTools` (MCP) — `process_inbox_item`
- `ProjectSummaryResource` (MCP)
- `TaskHub` (SignalR) — real-time broadcasts
- `AgentPollingLoop` (CopilotAgent) — task assignment filtering

### Key Enums That May Need New Values
- `ActivityChangeType` — subtask-specific events
- `NotificationType` — subtask-specific notifications
- `TaskItemStatus` — possibly no changes, but `Blocked` semantics need clarification

---

**Document Version:** 3.0
**Author:** Agent (Bill Claude)
**Date:** 2026-04-09
**Revised:** 2026-07-17
**Status:** ❌ Rejected — scope exceeds benefit
**Rejection Reason:** The gap analysis (Gaps 1–15) revealed that introducing subtasks touches the data model, self-referential FK cascade handling, every query/counting surface (`ProgressService`, `GetWhatsNext`, `SearchTasks`), all MCP tools, the SignalR `TaskHub`, the CopilotAgent polling loop, activity log attribution for system actions, and the `Blocked` status semantics. The implementation effort and ongoing maintenance cost are not justified by the frequency of multi-stage inbox items.

---

## How to Provide Feedback

This idea has been rejected. The document is retained for historical reference. If the need for multi-stage inbox workflows resurfaces, this analysis and the 15 identified gaps provide a starting point for a future proposal.
