# Copilot Instructions

## Project Guidelines
- TeamWare is an ASP.NET Core MVC application. It should not use Razor Pages. Use Controllers and Views instead.
- One type per file (MAINT-01).
- Tests accompany every feature. No phase is complete without its test cases.
- Follow the implementation plan in `TeamWare.Web/Specifications/ImplementationPlan.md` and `TeamWare.Web/Specifications/SocialFeaturesImplementationPlan.md`.

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

The following maps implementation plan work items to their canonical GitHub issues. Use the **Canonical Issue** when committing code. Duplicates should be closed.

### Phase 0: Foundation (label: `Phase 0: Foundation`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|-----------------|
| 0.1 Solution and Project Structure | #1 | — |
| 0.2 Data Layer (SQLite + EF Core) | #2 | — |
| 0.3 Authentication and Identity | #3 | — |
| 0.4 Frontend Stack Replacement | #4 | — |
| 0.5 Shared Infrastructure | #6 | #5, #10 |

### Phase 1: Project Management (label: `Phase 1: Project Management`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|-----------------|
| 1.1 Project Domain Models | #12 | #8 |
| 1.2 Project Services | #11 | #7 |
| 1.3 Membership Services | #13 | #9 |
| 1.4 Project Controllers and Views | #14 | #21 |

### Phase 2: Task Management (label: `Phase 2: Task Management`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|-----------------|
| 2.1 Task Domain Models | #20 | #17 |
| 2.2 Task Services | #18 | #15 |
| 2.3 Task Controllers and Views | #19 | #16 |

### Phase 3: Inbox and GTD (label: `Phase 3: Inbox and GTD`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|-----------------|
| 3.1 Inbox Domain Models | #24 | — |
| 3.2 Inbox Services | #22 | — |
| 3.3 Inbox Controllers and Views | #23 | — |

### Phase 4: Progress Tracking (label: `Phase 4: Progress Tracking`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|-----------------|
| 4.1 Activity Log Domain Models | #26 | — |
| 4.2 Activity Log and Progress Services | #27 | — |
| 4.3 Progress Tracking Controllers and Views | #29 | — |

### Phase 5: Comments (label: `Phase 5: Comments`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|-----------------|
| 5.1 Comment Domain Models | #30 | — |
| 5.2 Comment Services | #28 | — |
| 5.3 Comment Controllers and Views | #25 | — |

### Phase 6: Notifications (label: `Phase 6: Notifications`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|-----------------|
| 6.1 Notification Domain Models | #32 | — |
| 6.2 Notification Services | #31 | — |
| 6.3 Notification Controllers and Views | #33 | — |

### Phase 7: Review Workflow (label: `Phase 7: Review Workflow`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|-----------------|
| 7.1 Review Domain Models | #35 | — |
| 7.2 Review Services | #34 | — |
| 7.3 Review Controllers and Views | #36 | — |

### Phase 8: User Profile (label: `Phase 8: User Profile`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|-----------------|
| 8.1 User Profile Services | #37 | — |
| 8.2 User Profile and Dashboard Controllers and Views | #39 | — |

### Phase 9: Polish (label: `Phase 9: Polish`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|-----------------|
| 9.1 Security Hardening | #38 | — |
| 9.2 Performance | #41 | — |
| 9.3 UI/UX Polish | #40 | — |
| 9.4 Documentation | #42 | — |

### Duplicate Issues to Close

The following issues are duplicates and should be closed in favor of their canonical counterparts:
- #5 (duplicate of #6 — Phase 0.5)
- #10 (duplicate of #6 — Phase 0.5)
- #8 (duplicate of #12 — Phase 1.1)
- #7 (duplicate of #11 — Phase 1.2)
- #9 (duplicate of #13 — Phase 1.3)
- #21 (duplicate of #14 — Phase 1.4)
- #17 (duplicate of #20 — Phase 2.1)
- #15 (duplicate of #18 — Phase 2.2)
- #16 (duplicate of #19 — Phase 2.3)

### Phase 10: System Administration (label: `Phase 10: System Administration`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|-----------------|
| 10.1 Identity Roles and Seeding | #66 | — |
| 10.2 Admin Activity Log Domain Model | #64 | — |
| 10.3 Admin Services | #63 | — |
| 10.4 Admin Controllers and Views | #65 | — |

### Phase 11: User Directory (label: `Phase 11: User Directory`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|-----------------|
| 11.1 Directory Services | #68 | — |
| 11.2 Directory Controllers and Views | #67 | — |

### Phase 12: User Activity and Presence (label: `Phase 12: User Activity and Presence`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|-----------------|
| 12.1 SignalR Infrastructure | #70 | — |
| 12.2 Activity and Presence Services | #69 | — |
| 12.3 Activity and Presence Controllers and Views | #71 | — |

### Phase 13: Project Invitation Improvements (label: `Phase 13: Project Invitation Improvements`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|-----------------|
| 13.1 Invitation Domain Model | #72 | — |
| 13.2 Invitation Services | #74 | — |
| 13.3 Invitation Controllers and Views | #73 | — |

### Phase 14: Social Features Polish (label: `Phase 14: Social Features Polish`)

| Work Item | Canonical Issue | Duplicate Issues |
|-----------|----------------|-----------------|
| 14.1 Security and Authorization | #76 | — |
| 14.2 Performance | #75 | — |
| 14.3 UI/UX Consistency | #78 | — |
| 14.4 Documentation | #77 | — |
- #17 (duplicate of #20 — Phase 2.1)
- #15 (duplicate of #18 — Phase 2.2)
- #16 (duplicate of #19 — Phase 2.3)