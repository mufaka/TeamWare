# TeamWare - Implementation Plan

This document defines the phased implementation plan for TeamWare based on the [Formal Specification](Specification.md). Each phase builds on the previous one and is broken into work items suitable for GitHub Issues. Check off items as they are completed to track progress.

---

## Progress Summary

| Phase | Description | Status |
|-------|------------|--------|
| 0 | Foundation and Infrastructure | Complete |
| 1 | Project Management | Complete |
| 2 | Task Management | Complete |
| 3 | Inbox and GTD Workflow | Complete |
| 4 | Progress Tracking and Activity Log | Complete |
| 5 | Comments and Communication | Complete |
| 6 | Notifications | Complete |
| 7 | Review Workflow | Complete |
| 8 | User Profile and Dashboard | Complete |
| 9 | Polish and Hardening | In Progress |

---

## Current State

The workspace is an ASP.NET Core MVC project (.NET 10) with Phase 0 complete:

- `TeamWare.Tests` xUnit project with 31 passing tests
- SQLite database via EF Core 10 with `ApplicationDbContext` and initial migration
- Microsoft Identity authentication with cookie auth, admin seed account
- Tailwind CSS 4.2.2 (via `@tailwindcss/cli`), HTMX 2.0.4, Alpine.js 3.14.9
- Responsive sidebar layout with light/dark theme toggle
- `aspnet-client-validation` for jQuery-free form validation
- `ServiceResult<T>` base type, `_Notification.cshtml` partial, global error handling
- HTTPS enforcement, anti-forgery tokens, status code pages

---

## Guiding Principles

1. **Vertical slices** - Each phase delivers end-to-end working functionality (model, data access, service, controller, view, tests).
2. **Tests accompany every feature** - No phase is complete without its test project and test cases (MAINT-02, TEST-01 through TEST-04).
3. **One type per file** - Enforced from the start (MAINT-01).
4. **MVC only** - Controllers and Views, no Razor Pages (project guideline).
5. **Incremental replacement** - Bootstrap/jQuery are removed only after Tailwind CSS 4, HTMX, and Alpine.js are fully in place.

---

## Phase 0: Foundation and Infrastructure

Establish the project structure, tooling, data layer, and frontend stack before any feature work.

### 0.1 Solution and Project Structure

- [x] Create the `TeamWare.Tests` xUnit test project and add it to the solution
- [x] Establish folder conventions in `TeamWare.Web`:
  - [x] `Models/` - Domain entities (one class per file)
  - [x] `Data/` - DbContext and EF Core configuration
  - [x] `Services/` - Business logic interfaces and implementations
  - [x] `Controllers/` - MVC controllers (already exists)
  - [x] `Views/` - Organized by controller name (already exists)
  - [x] `ViewModels/` - View-specific models
- [x] Add a `README.md` at the solution root with build and run instructions

### 0.2 Data Layer (SQLite + EF Core)

- [x] Add NuGet packages: `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design`
- [x] Create `ApplicationDbContext` inheriting from `IdentityDbContext<ApplicationUser>`
- [x] Create the `ApplicationUser` entity extending `IdentityUser` with: `DisplayName`, `AvatarUrl`, `ThemePreference`
- [x] Configure the SQLite connection string in `appsettings.json`
- [x] Register the DbContext in `Program.cs`
- [x] Create and apply the initial EF Core migration
- [x] Write integration tests verifying DbContext creation and migration

### 0.3 Authentication and Identity

- [x] Add NuGet package: `Microsoft.AspNetCore.Identity.EntityFrameworkCore`
- [x] Configure Microsoft Identity in `Program.cs` with default cookie authentication
- [x] Disable email confirmation by default (AUTH-06)
- [x] Create `AccountController` with actions: Register, Login, Logout (AUTH-01, AUTH-02, AUTH-03)
- [x] Create corresponding views for Register and Login
- [x] Create an admin-only password reset action (AUTH-04)
- [x] Seed an initial administrator account on first run
- [x] Write tests for registration, login, logout, and admin password reset

### 0.4 Frontend Stack Replacement

- [x] Remove Bootstrap, jQuery, and jQuery Validation from `wwwroot/lib`
- [x] Install and configure Tailwind CSS 4 (via npm/standalone CLI, integrated into the build)
- [x] Add HTMX (via CDN or local copy in `wwwroot/lib`)
- [x] Add Alpine.js (via CDN or local copy in `wwwroot/lib`)
- [x] Rewrite `_Layout.cshtml` with Tailwind CSS 4 utility classes, HTMX script, and Alpine.js script
- [x] Implement light/dark theme toggle using Alpine.js, defaulting to the user's `ThemePreference` (UI-02, USER-03)
- [x] Implement a responsive navigation shell (sidebar on desktop, hamburger menu on mobile) (UI-01)
- [x] Update the Home/Index and Error views to use the new stack
- [x] Remove `_Layout.cshtml.css` (no longer needed with Tailwind)
- [x] Update `_ValidationScriptsPartial.cshtml` or replace with HTMX-based validation approach
- [x] Verify the build pipeline compiles Tailwind CSS correctly
- [x] Write smoke tests verifying the layout renders and theme toggle works

### 0.5 Shared Infrastructure

- [x] Create a base `ServiceResult<T>` type for consistent service return values
- [x] Create a shared `_Notification.cshtml` partial for toast/alert messages
- [x] Configure global error handling and logging (REL-01, REL-02)
- [x] Configure HTTPS enforcement and anti-forgery tokens (SEC-02, SEC-03)
- [x] Write tests for error handling middleware

---

## Phase 1: Project Management

Deliver the ability to create, view, edit, archive, and manage membership of projects.

### 1.1 Domain Models

- [x] Create `Project` entity (PROJ-01)
- [x] Create `ProjectMember` entity with Role (Owner, Admin, Member)
- [x] Create EF Core configurations (relationships, constraints, indexes)
- [x] Add and apply the EF Core migration
- [x] Write unit tests for entity validation

### 1.2 Services

- [x] Create `IProjectService` and `ProjectService`
  - [x] `CreateProject` (PROJ-01) - auto-assigns the creator as Owner
  - [x] `UpdateProject` (PROJ-02)
  - [x] `ArchiveProject` / `DeleteProject` (PROJ-03)
  - [x] `GetProjectsForUser` (PROJ-07)
  - [x] `GetProjectDashboard` (PROJ-08)
- [x] Write unit tests for all service methods including authorization rules

### 1.3 Membership Services

- [x] Create `IProjectMemberService` and `ProjectMemberService`
  - [x] `InviteMember` (PROJ-04)
  - [x] `RemoveMember` (PROJ-05)
  - [x] `UpdateMemberRole` (PROJ-06)
- [x] Enforce that only Owners/Admins can invite and remove; only Owners can assign roles
- [x] Write unit tests for membership operations and authorization

### 1.4 Controllers and Views

- [x] Create `ProjectController` with actions: Index (list), Create, Edit, Details (dashboard), Archive, Delete
- [x] Create `ProjectMemberController` (or actions within `ProjectController`) for Invite, Remove, UpdateRole
- [x] Build views using Tailwind CSS 4, with HTMX for form submissions and list updates
- [x] Implement the project dashboard showing task count placeholders (to be populated in Phase 2)
- [x] Write integration tests for all controller actions and authorization checks

---

## Phase 2: Task Management

Deliver core task CRUD, assignment, filtering, and the GTD-inspired workflow (Next Action, Someday/Maybe, What's Next).

### 2.1 Domain Models

- [x] Create `TaskItem` entity with GTD fields: `IsNextAction`, `IsSomedayMaybe` (TASK-02, TASK-11, TASK-12)
- [x] Create `TaskAssignment` entity
- [x] Create EF Core configurations
- [x] Add and apply the EF Core migration
- [x] Write unit tests for entity validation and status/priority constraints

### 2.2 Services

- [x] Create `ITaskService` and `TaskService`
  - [x] `CreateTask` (TASK-01)
  - [x] `UpdateTask` (TASK-06)
  - [x] `DeleteTask` (TASK-08) - enforce Owner/Admin only
  - [x] `ChangeStatus` (TASK-07)
  - [x] `AssignMembers` / `UnassignMembers` (TASK-05)
  - [x] `MarkAsNextAction` / `ClearNextAction` (TASK-11)
  - [x] `MarkAsSomedayMaybe` / `ClearSomedayMaybe` (TASK-12)
  - [x] `GetTasksForProject` with filtering and sorting (TASK-09)
  - [x] `SearchTasks` (TASK-10)
  - [x] `GetWhatsNext` - returns the user's Next Action tasks across all projects, ordered by priority and due date, capped to a configurable limit (TASK-13, TASK-14)
- [x] Write unit tests for all service methods

### 2.3 Controllers and Views

- [x] Create `TaskController` with actions: Index (list within project), Create, Edit, Details, Delete, ChangeStatus
- [x] Create a `WhatsNextController` (or action on `HomeController`) for the cross-project What's Next view
- [x] Build task list view with HTMX for inline status changes, filtering, and sorting
- [x] Build the What's Next view as a focused, minimal list
- [x] Use Alpine.js for filter dropdowns and priority selectors
- [x] Wire up the project dashboard (from Phase 1) to show actual task counts by status
- [x] Write integration tests for all controller actions and authorization checks

---

## Phase 3: Inbox and GTD Workflow

Deliver the personal inbox capture/clarify workflow and the Someday/Maybe list.

### 3.1 Domain Models

- [x] Create `InboxItem` entity (INBOX-01)
- [x] Create EF Core configuration
- [x] Add and apply the EF Core migration
- [x] Write unit tests for entity validation

### 3.2 Services

- [x] Create `IInboxService` and `InboxService`
  - [x] `AddItem` (INBOX-02)
  - [x] `ClarifyItem` - assign to project, set priority, optionally flag as Next Action or Someday/Maybe (INBOX-03)
  - [x] `ConvertToTask` - creates a `TaskItem` from an inbox item and marks the inbox item as processed (INBOX-04)
  - [x] `DismissItem` (INBOX-05)
  - [x] `MoveToSomedayMaybe` (INBOX-07)
  - [x] `GetUnprocessedItems` / `GetUnprocessedCount` (INBOX-06)
- [x] Write unit tests for all service methods

### 3.3 Controllers and Views

- [x] Create `InboxController` with actions: Index (list), Add, Clarify, Dismiss
- [x] Build the inbox view with HTMX for quick-add and inline clarification
- [x] Display the unprocessed item count in the navigation bar
- [x] Build a Someday/Maybe list view (filtered from tasks + inbox items marked as such)
- [x] Write integration tests for all controller actions

---

## Phase 4: Progress Tracking and Activity Log

Deliver task completion statistics, activity timeline, and deadline visibility.

### 4.1 Domain Models

- [x] Create `ActivityLogEntry` entity to record task state changes (PROG-02)
- [x] Create EF Core configuration
- [x] Add and apply the EF Core migration

### 4.2 Services

- [x] Create `IActivityLogService` and `ActivityLogService`
  - [x] `LogChange` - called by `TaskService` when status, assignment, or priority changes
  - [x] `GetActivityForProject` - returns the timeline for a project
  - [x] `GetActivityForTask` - returns the timeline for a single task
- [x] Create `IProgressService` and `ProgressService`
  - [x] `GetProjectStatistics` - task counts by status, completion percentage (PROG-01)
  - [x] `GetOverdueTasks` (PROG-03)
  - [x] `GetUpcomingDeadlines` (PROG-04)
- [x] Write unit tests for all service methods

### 4.3 Controllers and Views

- [x] Add a project activity timeline view/partial (HTMX lazy-loaded on the project dashboard)
- [x] Update the project dashboard to display completion statistics and upcoming deadlines
- [x] Highlight overdue tasks in task lists with visual styling
- [x] Add a task detail section showing its activity history
- [x] Write integration tests

---

## Phase 5: Comments and Communication

Deliver task commenting with in-app notification on new comments.

### 5.1 Domain Models

- [x] Create `Comment` entity (COMM-01, COMM-02)
- [x] Create EF Core configuration
- [x] Add and apply the EF Core migration

### 5.2 Services

- [x] Create `ICommentService` and `CommentService`
  - [x] `AddComment` (COMM-01) - triggers notification to assigned users (COMM-04)
  - [x] `EditComment` (COMM-03) - only the author
  - [x] `DeleteComment` (COMM-03) - only the author
  - [x] `GetCommentsForTask`
- [x] Write unit tests for all service methods and authorization rules

### 5.3 Controllers and Views

- [x] Create `CommentController` (or actions nested under `TaskController`) with actions: Add, Edit, Delete
- [x] Build a comments section on the task detail view, using HTMX for adding/editing comments without a full page reload
- [x] Write integration tests

---

## Phase 6: Notifications

Deliver the in-app notification system for task assignments, deadlines, status changes, comments, and inbox/review prompts.

### 6.1 Domain Models

- [x] Create `Notification` entity (NOTIF-01 through NOTIF-05)
- [x] Create EF Core configuration
- [x] Add and apply the EF Core migration

### 6.2 Services

- [x] Create `INotificationService` and `NotificationService`
  - [x] `CreateNotification` - generic method used by other services
  - [x] `GetUnreadForUser`
  - [x] `MarkAsRead` / `DismissNotification` (NOTIF-04)
  - [x] `GetInboxThresholdAlert` (NOTIF-05)
- [x] Integrate notification triggers into:
  - [x] `TaskService` (assignment, status change)
  - [x] `CommentService` (new comment on assigned task)
  - [x] `InboxService` (unprocessed count threshold)
- [x] Write unit tests for all service methods and integration with triggering services

### 6.3 Controllers and Views

- [x] Create `NotificationController` with actions: Index, MarkAsRead, Dismiss
- [x] Build a notification dropdown in the navigation bar (Alpine.js for toggle, HTMX for loading and dismissing)
- [x] Display unread notification count badge in the nav bar
- [x] Write integration tests

---

## Phase 7: Review Workflow

Deliver the GTD periodic review feature.

### 7.1 Domain Models

- [x] Create `UserReview` entity (REV-01 through REV-04)
- [x] Create EF Core configuration
- [x] Add and apply the EF Core migration

### 7.2 Services

- [x] Create `IReviewService` and `ReviewService`
  - [x] `StartReview` - gathers inbox items, active tasks, Next Actions, and Someday/Maybe items for the user
  - [x] `CompleteReview` (REV-04)
  - [x] `GetLastReviewDate` (REV-04)
  - [x] `IsReviewDue` - checks against configurable schedule (REV-03)
- [x] Integrate with `NotificationService` to prompt reviews when due (REV-03)
- [x] Write unit tests for all service methods

### 7.3 Controllers and Views

- [x] Create `ReviewController` with actions: Index (guided review page), Complete
- [x] Build a multi-step review view where the user walks through:
  - [x] Step 1: Unprocessed inbox items (process or dismiss)
  - [x] Step 2: Active tasks (re-prioritize, update status, flag as Next Action or Someday/Maybe)
  - [x] Step 3: Someday/Maybe items (promote to active, keep, or dismiss)
- [x] Use HTMX for step transitions and inline edits
- [x] Display last review date and review-due indicator on the user dashboard
- [x] Write integration tests

---

## Phase 8: User Profile and Dashboard

Deliver user profile management and a personal dashboard that ties all features together.

### 8.1 Services

- [x] Create `IUserProfileService` and `UserProfileService`
  - [x] `GetProfile` / `UpdateProfile` (USER-01)
  - [x] `ChangePassword` (USER-02)
  - [x] `UpdateThemePreference` (USER-03)
- [x] Write unit tests

### 8.2 Controllers and Views

- [x] Create `ProfileController` with actions: Index, Edit, ChangePassword
- [x] Build a personal dashboard on `Home/Index` (post-login) showing:
  - [x] Inbox unprocessed count with link
  - [x] What's Next task list (top items)
  - [x] Projects summary with task counts
  - [x] Upcoming deadlines
  - [x] Last review date and review-due prompt
  - [x] Recent notifications
- [x] Use HTMX to lazy-load each dashboard section
- [x] Write integration tests

---

## Phase 9: Polish and Hardening

Final pass on cross-cutting concerns, accessibility, and production readiness.

### 9.1 Security Hardening

- [x] Audit all endpoints for authorization enforcement (SEC-05)
- [x] Review input validation and sanitization across all forms (SEC-04)
- [x] Verify CSRF tokens on all POST/PUT/DELETE actions (SEC-03)
- [x] Verify HTTPS enforcement (SEC-02)
- [x] Write security-focused integration tests

### 9.2 Performance

- [ ] Profile page load times and optimize slow queries (PERF-01)
- [ ] Verify HTMX partial update response times (PERF-02)
- [ ] Add database indexes where query analysis indicates a need
- [ ] Test with simulated concurrent users (PERF-03)

### 9.3 UI/UX Polish

- [x] Verify responsive behavior across breakpoints (UI-01)
- [x] Verify light/dark theme consistency across all views (UI-02)
- [x] Verify no emoticons or emojis in the UI (UI-07)
- [x] Review all views for clarity and consistency (UI-03)
- [ ] Perform cross-browser testing (Chrome, Firefox, Edge, Safari)

### 9.4 Documentation

- [x] Update `README.md` with setup, configuration, and deployment instructions
- [x] Document admin workflows (user management, password reset)
- [x] Document the GTD workflow (Inbox, What's Next, Review) for end users

---

## Phase Dependency Summary

```
Phase 0: Foundation
  |
  +---> Phase 1: Project Management
  |       |
  |       +---> Phase 2: Task Management
  |               |
  |               +---> Phase 3: Inbox and GTD
  |               |
  |               +---> Phase 4: Progress Tracking
  |               |
  |               +---> Phase 5: Comments
  |                       |
  |                       +---> Phase 6: Notifications
  |                               |
  |                               +---> Phase 7: Review Workflow
  |
  +---> Phase 8: User Profile and Dashboard (after Phases 1-7)
  |
  +---> Phase 9: Polish and Hardening (after Phase 8)
```

Phases 3, 4, and 5 can be worked in parallel after Phase 2 is complete. Phase 6 depends on Phase 5 (comment notifications). Phase 7 depends on Phase 6 (review reminders). Phase 8 ties all features into the dashboard. Phase 9 is a final pass.

---

## Requirement Traceability

| Spec Requirement | Covered In |
|-----------------|-----------|
| AUTH-01 through AUTH-06 | Phase 0.3 |
| USER-01 through USER-03 | Phase 8.1, 8.2 |
| INBOX-01 through INBOX-07 | Phase 3 |
| PROJ-01 through PROJ-08 | Phase 1 |
| TASK-01 through TASK-14 | Phase 2 |
| PROG-01 through PROG-04 | Phase 4 |
| COMM-01 through COMM-04 | Phase 5 |
| NOTIF-01 through NOTIF-05 | Phase 6 |
| REV-01 through REV-04 | Phase 7 |
| UI-01 through UI-07 | Phase 0.4, Phase 9.3 |
| PERF-01 through PERF-03 | Phase 9.2 |
| SEC-01 through SEC-05 | Phase 0.3, Phase 9.1 |
| REL-01, REL-02 | Phase 0.5 |
| MAINT-01 through MAINT-03 | All Phases (enforced throughout) |
| TEST-01 through TEST-04 | All Phases (enforced throughout) |
