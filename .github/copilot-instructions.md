# Copilot Instructions

## Project Guidelines
- TeamWare is an ASP.NET Core MVC application. It should not use Razor Pages. Use Controllers and Views instead.
- One type per file (MAINT-01).
- Tests accompany every feature. No phase is complete without its test cases.
- Follow the implementation plan in `TeamWare.Web/Specifications/ImplementationPlan.md`, `TeamWare.Web/Specifications/SocialFeaturesImplementationPlan.md`, `TeamWare.Web/Specifications/ProjectLoungeImplementationPlan.md`, and `TeamWare.Web/Specifications/OllamaIntegrationImplementationPlan.md`.

---

## Developer Workflow

### Branch Strategy

Each phase in the implementation plan gets its own branch created from `master`. Branch naming convention:

| Phase | Branch Name |
|-------|------------|
| Phase 0 | `phase-0/foundation` |
| Phase 1 | `phase-1/project-management` |
| Phase 2 | `phase-2/task-management` |
| Phase 3 | `phase-3/inbox-gtd` |
| Phase 4 | `phase-4/progress-tracking` |
| Phase 5 | `phase-5/comments` |
| Phase 6 | `phase-6/notifications` |
| Phase 7 | `phase-7/review-workflow` |
| Phase 8 | `phase-8/user-profile-dashboard` |
| Phase 9 | `phase-9/polish-hardening` |
| Phase 10 | `phase-10/system-administration` |
| Phase 11 | `phase-11/user-directory` |
| Phase 12 | `phase-12/activity-presence` |
| Phase 13 | `phase-13/invitation-improvements` |
| Phase 14 | `phase-14/social-polish` |
| Phase 15 | `phase-15/lounge-data-layer` |
| Phase 16 | `phase-16/lounge-service-layer` |
| Phase 17 | `phase-17/lounge-signalr-hub` |
| Phase 18 | `phase-18/lounge-controllers-views` |
| Phase 19 | `phase-19/lounge-notifications-mentions` |
| Phase 20 | `phase-20/lounge-background-jobs` |
| Phase 21 | `phase-21/lounge-polish-hardening` |
| Phase 22 | `phase-22/ai-foundation` |
| Phase 23 | `phase-23/ai-content-rewriting` |
| Phase 24 | `phase-24/ai-summary-generation` |
| Phase 25 | `phase-25/ai-polish-hardening` |

When starting a phase:
1. Create the phase branch from `master` in GitHub.
2. All work items within that phase are committed to the phase branch.
3. When the phase is complete, create a pull request to merge the phase branch into `master`.

### Issue Tracking

Each work item in the implementation plan has a corresponding GitHub issue. Before starting any work item:
1. **Verify** that a matching GitHub issue exists and its description matches the current plan.
2. **Create** a new issue if one does not exist, using the format: `Phase X.Y: <Work Item Title>` with the appropriate phase label.
3. Close duplicate issues if they exist (see Duplicate Issues section below).

### Commit Messages

All commits must reference the GitHub issue being worked on. Format:

```
<short description of change>

Refs #<issue-number>
```

When a commit completes all work items in an issue, use `Closes` instead:

```
<short description of change>

Closes #<issue-number>
```

Examples:
```
Create ApplicationDbContext and ApplicationUser entity

Refs #2
```

```
Add integration tests for DbContext creation and migration

Closes #2
```

---

## GitHub Issue Map

The following maps implementation plan work items to their canonical GitHub issues. Use the **Canonical Issue** when committing code.

> **Phases 0-21 (Complete):** All issue mappings for Phases 0-21 have been archived. These phases are fully merged to `master`. For historical reference, see the closed issues in GitHub or the individual implementation plans:
> - Phases 0-9: `TeamWare.Web/Specifications/ImplementationPlan.md`
> - Phases 10-14: `TeamWare.Web/Specifications/SocialFeaturesImplementationPlan.md`
> - Phases 15-21: `TeamWare.Web/Specifications/ProjectLoungeImplementationPlan.md`

### Phase 22: AI Foundation and Configuration (label: `Phase 22: AI Foundation`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 22.1 GlobalConfiguration Seeding | TBD | — |
| 22.2 OllamaService | TBD | — |
| 22.3 AiAssistantService | TBD | — |
| 22.4 AiController Skeleton | TBD | — |

### Phase 23: Content Rewriting (label: `Phase 23: Content Rewriting`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 23.1 Rewrite Controller Actions | TBD | — |
| 23.2 Rewrite UI - Project Edit Form | TBD | — |
| 23.3 Rewrite UI - Task Edit Form | TBD | — |
| 23.4 Rewrite UI - Comment Form | TBD | — |
| 23.5 Rewrite UI - Inbox Clarify Form | TBD | — |
| 23.6 Shared AI JavaScript | TBD | — |

### Phase 24: Summary Generation (label: `Phase 24: Summary Generation`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 24.1 Summary Data Gathering | TBD | — |
| 24.2 Summary Controller Actions | TBD | — |
| 24.3 Summary UI - Project Dashboard | TBD | — |
| 24.4 Summary UI - Personal Dashboard | TBD | — |
| 24.5 Summary UI - GTD Review Page | TBD | — |
| 24.6 Shared AI Summary JavaScript | TBD | — |

### Phase 25: AI Polish and Hardening (label: `Phase 25: AI Polish`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|------------------|
| 25.1 Error Handling and Resilience | TBD | — |
| 25.2 UI/UX Consistency | TBD | — |
| 25.3 Security Review | TBD | — |
| 25.4 Documentation | TBD | — |