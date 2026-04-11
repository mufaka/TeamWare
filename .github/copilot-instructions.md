# Copilot Instructions

## Project Guidelines
- TeamWare is an ASP.NET Core MVC application. It should not use Razor Pages. Use Controllers and Views instead.
- One type per file (MAINT-01).
- Tests accompany every feature. No phase is complete without its test cases.
- Follow the active implementation plan in `TeamWare.Web/Specifications/WhiteBoardImplementationPlan.md`. All prior implementation plans (Phases 0–51) are complete and merged to `master`.
- In `_DashboardProjects.cshtml`, ensure the link to project details is correctly implemented using `asp-controller="Project" asp-action="Details" asp-route-id="@project.Id"`.

## Idea Documents
- Idea documents (e.g., files named *Idea.md in TeamWare.Web/Specifications/) are collaborative, human-in-the-loop documents. They present multiple approaches neutrally, pose open questions for the human to answer, and do not make decisions or declare a preferred direction. Decisions belong in specification documents, not idea documents.

---

## Developer Workflow

### Branch Strategy

The whiteboard feature (Phases 52–61) is developed on a single feature branch: `feature/whiteboard`. All whiteboard work is committed to this branch until the full implementation is complete and validated. The branch will be merged to `master` only when all phases are finished.

> **Prior phases (0–51):** Each phase previously used its own branch (e.g., `phase-0/foundation`, `phase-51/edit-repo-mcp-inline`). Those branches have all been merged to `master` and are no longer relevant.

### Work Tracking

All whiteboard work items are tracked directly in `TeamWare.Web/Specifications/WhiteBoardImplementationPlan.md`. Check items off as they are completed. No separate GitHub issues are used for whiteboard phases.

### Commit Messages

Commits should have a short, descriptive message referencing the phase and work item:

```
<short description of change> (Phase X.Y)
```

Examples:
```
Create Whiteboard, WhiteboardInvitation, and WhiteboardChatMessage entities (Phase 52.1)
```

```
Add WhiteboardService with CRUD and access control (Phase 53.2)
```