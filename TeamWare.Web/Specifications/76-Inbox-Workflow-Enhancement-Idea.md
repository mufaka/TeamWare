# Idea: Enhanced Inbox Workflow with Progressive Refinement

**Task:** [#76](https://teamware.example.com/tasks/76)
**Priority:** Medium
**Context:** Improving the inbox-to-task workflow to support complex work items that require multiple stages of refinement before implementation

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

**Limitations:**
- No way to assign users during inbox processing (only after conversion to task)
- No formal workflow stages beyond task status
- No parent-child task relationships
- No enforcement of sequential dependencies between work items
- No way to track "this is an idea that needs a spec before implementation"

---

## Proposed Solutions

This document explores **three approaches** with increasing complexity and capability. Team feedback will determine the best direction.

---

## Approach 1: Workflow Metadata on Tasks (Minimal Change)

### Concept
Add workflow stage metadata directly to `TaskItem` without changing the core data model significantly.

### Changes Required

#### 1.1 Add Workflow Fields to `TaskItem`

```csharp
public class TaskItem
{
    // ... existing fields ...

    public WorkflowStage WorkflowStage { get; set; } = WorkflowStage.Implementation;

    public int? BlockedByTaskId { get; set; }

    public TaskItem? BlockedByTask { get; set; }
}

public enum WorkflowStage
{
    Idea = 0,           // Initial concept/proposal
    Specification = 1,  // Detailed requirements/design
    Planning = 2,       // Technical planning/architecture
    Implementation = 3, // Actual development work
    Validation = 4      // Testing/verification (optional)
}
```

#### 1.2 Enhanced Inbox Processing

Add a new `ConvertToTaskWithWorkflow` method to `InboxService`:

```csharp
public async Task<ServiceResult<TaskItem>> ConvertToTaskWithWorkflow(
    int inboxItemId,
    string userId,
    int projectId,
    WorkflowStage startingStage,
    TaskItemPriority priority,
    string[]? assigneeUserIds = null,  // NEW: assign immediately
    DateTime? dueDate = null,
    bool isNextAction = false)
{
    // Create task with workflow stage
    // Optionally assign users right away
    // Set up blockers if needed
}
```

#### 1.3 UI Changes

- **Inbox processing screen:** Add dropdown to select starting workflow stage
- **Inbox processing screen:** Add multi-select for initial assignees
- **Task detail view:** Show workflow stage badge
- **Task detail view:** Allow setting "blocked by" another task
- **Task list filters:** Filter by workflow stage

### Pros
✅ Minimal database schema changes
✅ Leverages existing task status system
✅ Can assign users during inbox processing
✅ Simple to implement (~1-2 days)
✅ Backward compatible (existing tasks default to `Implementation` stage)

### Cons
❌ Workflow stages are informal metadata, not enforced
❌ Blocked-by relationship is manual (no automatic dependency checking)
❌ Doesn't naturally represent multi-stage workflows (one task = one stage)
❌ Assignees for *future* stages must be manually re-assigned
❌ No visibility into "this idea will eventually need 3 more tasks"

### Use Case Example

**Inbox Item:** "Add dark mode support"
**Processing:**
1. Convert to task with stage = `Specification`, assign to designer Sarah
2. Sarah creates spec document, marks task `Done`
3. Create *new* task manually for `Planning` stage, assign to architect John, set "blocked by" previous task
4. John creates technical plan, marks `Done`
5. Create *new* task for `Implementation`, assign to developer Alex
6. Alex implements, marks `Done`

**Note:** This still requires manually creating 3 separate tasks. The workflow is tracked but not automated.

---

## Approach 2: Subtask System (Moderate Complexity)

### Concept
Introduce a formal parent-child task hierarchy where an inbox item converts to a *parent task* with multiple *subtasks* representing workflow stages.

### Changes Required

#### 2.1 Add Subtask Support to `TaskItem`

```csharp
public class TaskItem
{
    // ... existing fields ...

    public int? ParentTaskId { get; set; }

    public TaskItem? ParentTask { get; set; }

    public ICollection<TaskItem> Subtasks { get; set; } = new List<TaskItem>();

    public WorkflowStage WorkflowStage { get; set; } = WorkflowStage.Implementation;

    public int? SequenceOrder { get; set; }  // For ordering subtasks
}
```

#### 2.2 Subtask Service Layer

New `ISubtaskService` with methods:
- `CreateSubtask(parentTaskId, title, stage, assignees, sequenceOrder)`
- `GetSubtasks(parentTaskId)`
- `GetBlockingSubtask(taskId)` → Returns the previous subtask that must complete first
- `CanStartSubtask(subtaskId)` → Checks if prior subtask is `Done`

#### 2.3 Enhanced Inbox Workflow Conversion

New method: `ConvertToWorkflow(inboxItemId, projectId, stages[])`:

```csharp
// Example call:
await _inboxService.ConvertToWorkflow(
    inboxItemId: 42,
    projectId: 14,
    stages: new[] {
        new WorkflowStageDefinition {
            Stage = WorkflowStage.Specification,
            AssigneeUserIds = new[] { "designer-user-id" },
            Title = "Design dark mode UI/UX"
        },
        new WorkflowStageDefinition {
            Stage = WorkflowStage.Planning,
            AssigneeUserIds = new[] { "architect-user-id" },
            Title = "Plan dark mode architecture"
        },
        new WorkflowStageDefinition {
            Stage = WorkflowStage.Implementation,
            AssigneeUserIds = new[] { "dev-user-id" },
            Title = "Implement dark mode"
        }
    }
);
```

This creates:
1. **Parent task:** "Add dark mode support" (no assignees, serves as container)
2. **Subtask 1:** "Design dark mode UI/UX" (stage=Spec, assigned to Sarah, order=1, status=ToDo)
3. **Subtask 2:** "Plan dark mode architecture" (stage=Planning, assigned to John, order=2, status=Blocked)
4. **Subtask 3:** "Implement dark mode" (stage=Implementation, assigned to Alex, order=3, status=Blocked)

#### 2.4 Automatic Dependency Enforcement

Add background job or service logic:
- When subtask N is marked `Done`, check if subtask N+1 exists and update its status from `Blocked` → `ToDo`
- Send notification to assignees of subtask N+1
- Update parent task status based on subtask completion (e.g., parent is `Done` only when all subtasks are `Done`)

#### 2.5 UI Changes

- **Inbox processing:** New "Convert to Workflow" button with stage builder UI
- **Task detail view:** Show subtask list with stage badges, completion indicators, assignees
- **Task list:** Option to show/hide subtasks (default: collapsed under parent)
- **My Tasks view:** Show user's assigned subtasks with "Blocked by X" indicator
- **Activity log:** Track subtask creation, completion, unblocking

### Pros
✅ Formal parent-child relationships in database
✅ Automatic dependency enforcement (prior task must complete first)
✅ Assign different users to different stages upfront
✅ Clear visibility: one parent task shows the entire workflow
✅ Subtasks can still have comments, attachments, their own assignees
✅ Flexible: can add ad-hoc subtasks later

### Cons
❌ More complex database schema (self-referential foreign key)
❌ UI complexity for managing subtask trees
❌ What if a subtask itself needs subtasks? (Do we allow nesting?)
❌ Queries become more complex (need to traverse tree)
❌ ~3-5 days implementation effort

### Use Case Example

**Inbox Item:** "Add dark mode support"
**Processing (via new UI):**
1. Click "Convert to Workflow"
2. UI shows stage builder:
   - Stage 1: Specification → Assign Sarah → Title: "Design dark mode UI"
   - Stage 2: Planning → Assign John → Title: "Architect dark mode system"
   - Stage 3: Implementation → Assign Alex → Title: "Implement dark mode"
3. Submit → Creates parent + 3 subtasks
4. Sarah sees "Design dark mode UI" in her tasks (status=ToDo)
5. John sees "Architect dark mode system" (status=Blocked, blocked by Sarah's task)
6. Sarah completes her task → John's task automatically becomes `ToDo`, John gets notification
7. John completes → Alex's task unblocks
8. Alex completes → Parent task automatically marked `Done`

---

## Approach 3: Formal Workflow Engine (High Complexity)

### Concept
Introduce a dedicated `Workflow` and `WorkflowStep` entity system, decoupled from `TaskItem`, where workflows are reusable templates.

### Changes Required

#### 3.1 New Entities

```csharp
public class Workflow
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;  // e.g., "Feature Development Workflow"

    public int ProjectId { get; set; }

    public Project Project { get; set; } = null!;

    public bool IsTemplate { get; set; }  // If true, can be reused for multiple items

    public ICollection<WorkflowStep> Steps { get; set; } = new List<WorkflowStep>();
}

public class WorkflowStep
{
    public int Id { get; set; }

    public int WorkflowId { get; set; }

    public Workflow Workflow { get; set; } = null!;

    public string Name { get; set; } = string.Empty;  // e.g., "Specification"

    public WorkflowStage Stage { get; set; }

    public int SequenceOrder { get; set; }

    public int? TaskItemId { get; set; }  // Created when workflow is instantiated

    public TaskItem? TaskItem { get; set; }

    public WorkflowStepStatus Status { get; set; } = WorkflowStepStatus.Pending;
}

public enum WorkflowStepStatus
{
    Pending = 0,
    Active = 1,
    Completed = 2,
    Skipped = 3
}

public class WorkflowInstance
{
    public int Id { get; set; }

    public int WorkflowId { get; set; }

    public Workflow Workflow { get; set; } = null!;

    public int? InboxItemId { get; set; }

    public InboxItem? InboxItem { get; set; }

    public DateTime CreatedAt { get; set; }
}
```

#### 3.2 Workflow Service Layer

New `IWorkflowService`:
- `CreateWorkflowTemplate(projectId, name, steps[])`
- `InstantiateWorkflow(workflowId, inboxItemId)` → Creates tasks for each step
- `AdvanceWorkflow(workflowInstanceId)` → Moves to next step when current completes
- `GetWorkflowProgress(workflowInstanceId)` → Returns completion percentage, current step, etc.

#### 3.3 Inbox Integration

New conversion option: "Apply Workflow Template"
- User selects a predefined workflow template
- System creates tasks for each step, links them to workflow instance
- Steps activate sequentially as prior ones complete

#### 3.4 UI Changes

- **Project settings:** Workflow template builder (define reusable workflows)
- **Inbox processing:** Dropdown to select workflow template + assign users per step
- **Workflow progress view:** Visual timeline showing current step, completed steps, upcoming steps
- **Dashboard widget:** "Active Workflows" showing all in-flight workflow instances

### Pros
✅ Maximum flexibility: workflows are first-class citizens
✅ Reusable templates across projects
✅ Can support complex workflows (branching, parallel steps, conditional steps)
✅ Clear separation of concerns (workflow logic vs task execution)
✅ Analytics-friendly (track workflow completion rates, bottlenecks)
✅ Could support workflow versioning (template evolves over time)

### Cons
❌ Significant complexity (~7-10 days implementation)
❌ Steeper learning curve for users
❌ May be overkill for simple use cases
❌ Requires UI for template management
❌ Database schema becomes much heavier
❌ Risk of over-engineering if workflows aren't used frequently

### Use Case Example

**Project Admin Creates Template:**
1. Navigate to Project Settings → Workflows
2. Create template "Feature Development Workflow"
3. Add steps:
   - Step 1: Idea Review (stage=Idea)
   - Step 2: Specification (stage=Specification)
   - Step 3: Technical Planning (stage=Planning)
   - Step 4: Implementation (stage=Implementation)
   - Step 5: QA (stage=Validation)
4. Save template

**User Processes Inbox Item:**
1. Select inbox item "Add dark mode support"
2. Click "Apply Workflow"
3. Select "Feature Development Workflow"
4. Assign roles:
   - Idea Review → Product Manager (Mark)
   - Specification → Designer (Sarah)
   - Technical Planning → Architect (John)
   - Implementation → Developer (Alex)
   - QA → Tester (Jane)
5. Submit → Creates 5 tasks linked to workflow instance
6. Mark completes idea review → Sarah's spec task activates
7. And so on...

---

## Comparison Matrix

| Feature | Approach 1: Metadata | Approach 2: Subtasks | Approach 3: Workflow Engine |
|---------|----------------------|----------------------|-----------------------------|
| **Complexity** | Low | Medium | High |
| **Implementation Time** | 1-2 days | 3-5 days | 7-10 days |
| **Assign Users in Inbox** | ✅ Yes | ✅ Yes | ✅ Yes |
| **Sequential Dependencies** | ⚠️ Manual | ✅ Automatic | ✅ Automatic |
| **Multi-Stage Visibility** | ❌ No (separate tasks) | ✅ Yes (parent task) | ✅ Yes (workflow view) |
| **Reusable Templates** | ❌ No | ❌ No | ✅ Yes |
| **Backward Compatibility** | ✅ Full | ✅ Full | ✅ Full (optional feature) |
| **UI Changes** | Minimal | Moderate | Significant |
| **Supports Ad-Hoc Workflows** | ✅ Yes | ✅ Yes | ⚠️ Requires template |
| **Analytics/Reporting** | ⚠️ Basic | ✅ Good | ✅ Excellent |
| **Risk of Over-Engineering** | ✅ Low | ⚠️ Medium | ❌ High |

---

## Recommendation for Team Discussion

### For Teams with Occasional Complex Items
**→ Approach 1 (Workflow Metadata)** is likely sufficient. Simple, fast to implement, doesn't over-complicate the system. Workflow stages are tracked but enforcement is manual.

### For Teams with Regular Multi-Phase Work
**→ Approach 2 (Subtasks)** provides the best balance of capability and complexity. Automatic dependency management, clear parent-child relationships, intuitive UI. This is the **recommended default** for most teams.

### For Teams Running Formal, Repeatable Processes
**→ Approach 3 (Workflow Engine)** is worth the investment if you have standardized workflows (e.g., "all features go through these 5 stages") and want analytics, templates, and maximum control.

---

## Alternative/Hybrid Ideas

### Hybrid: Subtasks + Workflow Templates (Approach 2.5)
- Implement Approach 2 (subtask system)
- Add a simple template storage mechanism (JSON in database or config files)
- When processing inbox, offer "Create from template" which pre-fills subtasks
- Avoids the full complexity of a workflow engine while enabling reusability

### Defer Assignment Until Each Stage
- Don't assign users during inbox processing
- Each subtask starts as `ToDo` with no assignees
- When a subtask becomes active, notify project members to self-assign or project lead assigns
- Reduces upfront planning burden, increases flexibility

### Inbox Item as Perpetual Parent
- Instead of converting `InboxItem` → `TaskItem`, keep `InboxItem` as the parent entity
- Add `InboxItem.Status` with values like `Unprocessed`, `InSpecification`, `InPlanning`, `InImplementation`, `Done`
- Tasks are created as children of `InboxItem` rather than replacing it
- Preserves the original capture context

---

## Questions for Team Feedback

1. **How often do we encounter inbox items that need multiple stages?**
   - If < 10% of inbox items: Approach 1 is sufficient
   - If 10-40%: Approach 2 is ideal
   - If > 40%: Consider Approach 3

2. **Do we have standardized workflows we want to reuse?**
   - If yes → Approach 3 or Hybrid 2.5
   - If no → Approach 1 or 2

3. **Who should decide the workflow stages?**
   - Person processing inbox? (supports ad-hoc workflows)
   - Project admin? (supports templates/standards)
   - Both? (hybrid)

4. **Should we support assigning users during inbox processing, or defer to later?**
   - Assign upfront: Requires knowing who will handle each stage
   - Defer: More flexible but adds coordination overhead

5. **Do we need workflow analytics (e.g., "avg time in specification stage")?**
   - If yes → Lean toward Approach 2 or 3
   - If no → Approach 1 is fine

6. **Can a subtask have its own subtasks?**
   - If yes → Need to design for tree depth (Approach 2 becomes more complex)
   - If no → Single-level parent-child is simpler

7. **How do we handle blocked tasks that are NOT part of a workflow?**
   - `BlockedByTaskId` in Approach 1/2 can work for any task
   - Should blocking be a general feature or workflow-specific?

---

## Next Steps

1. **Team reviews this document** and provides feedback via comments/task discussion
2. **Vote or discuss** which approach aligns with our needs
3. **Create detailed technical spec** for chosen approach
4. **Prototype UI mockups** to validate user experience
5. **Break down implementation** into subtasks (ironic!)
6. **Implement, test, deploy** incrementally

---

## Open Questions / Risks

- **Performance:** How do subtask queries scale with hundreds of parent tasks?
  *Mitigation:* Add index on `ParentTaskId`, use pagination

- **Permissions:** Should subtasks inherit parent task's project membership? Or have independent access control?
  *Recommendation:* Inherit for simplicity

- **Deletion:** If a parent task is deleted, cascade delete subtasks or orphan them?
  *Recommendation:* Cascade delete (with confirmation prompt)

- **Status Propagation:** Should parent task status auto-update based on subtask statuses?
  *Example:* Parent is `InProgress` if any subtask is `InProgress`

- **Assignment Conflicts:** What if a subtask is assigned to a user not in the project?
  *Validation:* Enforce project membership check during assignment

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

---

**Document Version:** 1.0
**Author:** Agent (Bill Claude)
**Date:** 2026-04-09
**Status:** Draft for Team Review
**Feedback Deadline:** TBD

---

## How to Provide Feedback

Please add comments to this task (#76) with:
- Your preferred approach (1, 2, or 3)
- Any concerns or suggestions
- Answers to the "Questions for Team Feedback" section
- Additional use cases or edge cases to consider

We'll schedule a team discussion once all feedback is collected.
