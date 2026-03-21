# TeamWare - Social Features Implementation Plan

This document defines the phased implementation plan for TeamWare social features based on the [Social Features Specification](SocialFeaturesSpecification.md). Each phase builds on the previous one and is broken into work items suitable for GitHub Issues. Check off items as they are completed to track progress.

---

## Progress Summary

| Phase | Description | Status |
|-------|------------|--------|
| 10 | System Administration | Complete |
| 11 | User Directory | Complete |
| 12 | User Activity and Presence | Complete |
| 13 | Project Invitation Improvements | Complete |
| 14 | Social Features Polish | Complete |

---

## Current State

All original phases (0-9) are complete. The workspace is an ASP.NET Core MVC project (.NET 10) with:

- Full project and task management (CRUD, assignment, filtering, GTD workflow)
- Inbox capture/clarify workflow with Someday/Maybe list
- Progress tracking with activity log and deadline visibility
- Task commenting with notifications
- In-app notification system (task assignments, deadlines, status changes, comments, inbox/review prompts)
- GTD review workflow
- User profile management and personal dashboard
- Security hardening, performance optimization, and UI polish

The social features build on top of this foundation. Phase 10 introduces the site-wide admin role, which is a prerequisite for several downstream features.

---

## Guiding Principles

All guiding principles from the [original implementation plan](ImplementationPlan.md) continue to apply:

1. **Vertical slices** - Each phase delivers end-to-end working functionality (model, data access, service, controller, view, tests).
2. **Tests accompany every feature** - No phase is complete without its test cases (MAINT-02, TEST-01 through TEST-07).
3. **One type per file** - Enforced throughout (MAINT-01).
4. **MVC only** - Controllers and Views, no Razor Pages (project guideline).

Additionally:

5. **SignalR as foundation** - The SignalR infrastructure introduced in Phase 12 is designed for reuse by future features (e.g., Project Lounge).
6. **Backward compatibility** - Existing invitation behavior (direct-add) is replaced by the accept/decline workflow in Phase 13. Migration must handle any existing project members gracefully.

---

## Phase 10: System Administration

Introduce the site-wide admin role (via ASP.NET Identity roles), admin dashboard, user management, and admin activity logging.

### 10.1 Identity Roles and Seeding

- [x] Create ASP.NET Identity roles: "Admin" and "User" (ADMIN-01, ADMIN-02)
- [x] Configure role services in `Program.cs`
- [x] Update the existing admin seed account to be assigned the "Admin" Identity role (ADMIN-03)
- [x] Assign the "User" role to all new registrations by default
- [x] Add and apply the EF Core migration for Identity roles
- [x] Write tests verifying role seeding and default role assignment on registration

### 10.2 Admin Activity Log Domain Model

- [x] Create `AdminActivityLog` entity (ADMIN-11, ADMIN-12)
- [x] Create EF Core configuration (relationships, constraints, indexes)
- [x] Add and apply the EF Core migration
- [x] Write unit tests for entity validation

### 10.3 Admin Services

- [x] Create `IAdminService` and `AdminService`
  - [x] `GetAllUsers` with search and filter (ADMIN-05)
  - [x] `LockUser` / `UnlockUser` (ADMIN-06)
  - [x] `ResetPassword` (ADMIN-07, fulfills AUTH-04)
  - [x] `PromoteToAdmin` / `DemoteToUser` (ADMIN-08)
  - [x] `GetSystemStatistics` - total users, projects, tasks (ADMIN-09)
- [x] Create `IAdminActivityLogService` and `AdminActivityLogService`
  - [x] `LogAction` - records admin actions with actor, target, action type, details, and timestamp
  - [x] `GetActivityLog` - returns paginated admin activity log entries
- [x] Integrate `AdminActivityLogService` into `AdminService` to log all administrative actions
- [x] Write unit tests for all service methods and authorization rules (TEST-06)

### 10.4 Admin Controllers and Views

- [x] Create `AdminController` with actions: Dashboard, Users (list), LockUser, UnlockUser, ResetPassword, PromoteToAdmin, DemoteToUser, ActivityLog
- [x] Protect all admin actions with `[Authorize(Roles = "Admin")]`
- [x] Build the admin dashboard view showing system-wide statistics (ADMIN-04, ADMIN-09)
- [x] Build the user management list view with search/filter (ADMIN-05)
- [x] Build user action views/modals for lock, unlock, reset password, and role changes
- [x] Build the admin activity log view with pagination
- [x] Update existing project authorization to allow site admins to view and edit any project (ADMIN-10)
- [x] Write integration tests for all admin controller actions and authorization checks (TEST-06)

---

## Phase 11: User Directory

Deliver the searchable user directory and user profile pages.

### 11.1 Directory Services

- [x] Create `IUserDirectoryService` and `UserDirectoryService`
  - [x] `SearchUsers` - search by display name or email (DIR-01, DIR-02)
  - [x] `GetUsersSorted` - return users sorted by display name or email (DIR-03)
  - [x] `GetUserProfile` - returns display name, avatar, email, project memberships, task stats, and recent activity (DIR-05 through DIR-08)
  - [x] `GetUserTaskStatistics` - tasks assigned, completed, and overdue (DIR-07)
  - [x] `GetUserRecentActivity` - last 30 days across all projects (DIR-08)
- [x] Write unit tests for all service methods

### 11.2 Controllers and Views

- [x] Create `DirectoryController` with actions: Index (list with search and sort), Profile (user detail)
- [x] Build the directory list view with search input and sort controls (Tailwind CSS, HTMX for search) (DIR-01, DIR-02, DIR-03)
- [x] Build the user profile page showing:
  - [x] Display name, avatar, and email (DIR-05)
  - [x] Project membership list (DIR-06)
  - [x] Task statistics (DIR-07)
  - [x] Recent activity feed, last 30 days (DIR-08)
  - [x] "Invite to project" link (DIR-09)
- [x] Ensure all registered users appear in the directory with no opt-out (DIR-10)
- [x] Write integration tests for directory and profile controller actions

---

## Phase 12: User Activity and Presence

Deliver the global activity feed, "last active" tracking, and real-time online/offline presence via SignalR.

### 12.1 SignalR Infrastructure

- [x] Add NuGet package: `Microsoft.AspNetCore.SignalR` (if not already included)
- [x] Create `PresenceHub` SignalR hub for tracking connected users (ACTV-05, ACTV-06)
- [x] Implement hub methods for tracking user connections and disconnections
- [x] Configure SignalR in `Program.cs` and map the hub endpoint
- [x] Add SignalR client JavaScript to the layout (connected on page load for authenticated users)
- [x] Write integration tests for hub connection and presence tracking (TEST-05)

### 12.2 Activity and Presence Services

- [x] Create `IPresenceService` and `PresenceService`
  - [x] `TrackUserConnection` / `TrackUserDisconnection`
  - [x] `GetOnlineUsers` - returns currently connected user IDs
  - [x] `UpdateLastActive` - updates the user's last active timestamp (ACTV-01)
- [x] Create `IGlobalActivityService` and `GlobalActivityService`
  - [x] `GetGlobalActivityFeed` - returns recent activity across all projects for a given viewer (ACTV-02)
  - [x] Apply masking rules: full detail for member projects, generic format for non-member projects (ACTV-03, ACTV-04)
- [x] Add a `LastActiveAt` field to `ApplicationUser` (or a separate tracking table)
- [x] Add and apply the EF Core migration
- [x] Write unit tests for all service methods, including masking logic

### 12.3 Controllers and Views

- [x] Create `ActivityController` (or extend `HomeController`) with an action for the global activity feed
- [x] Build the global activity feed partial view, loaded via HTMX on the dashboard (ACTV-02)
- [x] Display masked activity entries for non-member projects (ACTV-04)
- [x] Add online/offline presence indicator to user directory and profile pages (ACTV-05)
- [x] Display "last active" timestamp on user profile pages (ACTV-01)
- [x] Write integration tests for activity feed rendering and presence indicators

---

## Phase 13: Project Invitation Improvements

Replace the current direct-add member flow with an invitation accept/decline workflow.

### 13.1 Invitation Domain Model

- [x] Create `ProjectInvitation` entity (INVITE-02)
- [x] Create EF Core configuration (relationships, constraints, indexes)
- [x] Add and apply the EF Core migration
- [x] Write unit tests for entity validation

### 13.2 Invitation Services

- [x] Create `IProjectInvitationService` and `ProjectInvitationService`
  - [x] `SendInvitation` - creates a pending invitation and triggers a notification (INVITE-02, INVITE-06, NOTIF-06)
  - [x] `SendBulkInvitations` - invites multiple users at once (INVITE-05)
  - [x] `AcceptInvitation` - accepts the invitation, creates the `ProjectMember`, and updates invitation status (INVITE-03)
  - [x] `DeclineInvitation` - declines the invitation and updates invitation status (INVITE-03)
  - [x] `GetPendingInvitationsForProject` - returns pending invitations for project owners/admins (INVITE-04)
  - [x] `GetPendingInvitationsForUser` - returns pending invitations the user has not yet responded to
- [x] Integrate with `NotificationService` to create "ProjectInvitation" notifications with accept/decline link (INVITE-07, NOTIF-07)
- [x] Write unit tests for all service methods and authorization rules (TEST-07)

### 13.3 Controllers and Views

- [x] Create `InvitationController` with actions: Send, SendBulk, Accept, Decline, PendingForProject, PendingForUser
- [x] Update the existing project member invitation UI to use autocomplete search from the user directory (INVITE-01)
- [x] Build the pending invitation list view for project owners/admins (INVITE-04)
- [x] Build the user's pending invitations view (accessible from notifications or a dedicated page)
- [x] Build accept/decline UI (INVITE-03), accessible from the notification link (NOTIF-07)
- [x] Update or replace the existing direct-add member flow in `ProjectController` / `ProjectMemberController`
- [x] Write integration tests for the full invitation workflow (TEST-07)

---

## Phase 14: Social Features Polish

Final pass on cross-cutting concerns for the social features.

### 14.1 Security and Authorization

- [x] Audit all new endpoints for authorization enforcement (SEC-05)
- [x] Verify admin-only endpoints reject non-admin users (TEST-06)
- [x] Review input validation and sanitization on all new forms (SEC-04)
- [x] Verify CSRF tokens on all new POST/PUT/DELETE actions (SEC-03)
- [x] Write security-focused integration tests

### 14.2 Performance

- [x] Profile user directory search and optimize queries if needed
- [x] Profile global activity feed query and optimize for large datasets
- [x] Verify SignalR connection handling under concurrent user load
- [x] Add database indexes where query analysis indicates a need

### 14.3 UI/UX Consistency

- [x] Verify all new views are responsive across breakpoints (UI-01)
- [x] Verify light/dark theme consistency on all new views (UI-02)
- [x] Verify no emoticons or emojis in new UI elements (UI-07)
- [x] Review all new views for clarity and consistency with existing design (UI-03)

### 14.4 Documentation

- [x] Update `README.md` with social features setup and configuration (SignalR, admin role)
- [x] Document admin dashboard workflows (user management, activity log)
- [x] Document the invitation workflow for end users
- [x] Document the user directory and profile features

---

## Phase Dependency Summary

```
Phase 10: System Administration
  |
  +---> Phase 11: User Directory (depends on admin role for admin override access)
  |       |
  |       +---> Phase 12: User Activity and Presence (depends on directory for profile pages)
  |       |
  |       +---> Phase 13: Project Invitation Improvements (depends on directory for autocomplete)
  |
  +---> Phase 14: Social Features Polish (after Phases 10-13)
```

Phases 12 and 13 can be worked in parallel after Phase 11 is complete. Phase 14 is a final pass after all feature phases are done.

---

## Requirement Traceability

| Spec Requirement | Covered In |
|-----------------|-----------|
| ADMIN-01 through ADMIN-03 | Phase 10.1 |
| ADMIN-04, ADMIN-05, ADMIN-09 | Phase 10.4 |
| ADMIN-06 through ADMIN-08, ADMIN-10 | Phase 10.3, 10.4 |
| ADMIN-11, ADMIN-12 | Phase 10.2, 10.3 |
| DIR-01 through DIR-03 | Phase 11.1, 11.2 |
| DIR-04 through DIR-10 | Phase 11.2 |
| ACTV-01 | Phase 12.2, 12.3 |
| ACTV-02 through ACTV-04 | Phase 12.2, 12.3 |
| ACTV-05, ACTV-06 | Phase 12.1, 12.2 |
| INVITE-01 | Phase 13.3 |
| INVITE-02, INVITE-03 | Phase 13.1, 13.2 |
| INVITE-04, INVITE-05 | Phase 13.2, 13.3 |
| INVITE-06, INVITE-07 | Phase 13.2 |
| NOTIF-06, NOTIF-07 | Phase 13.2 |
| TEST-05 | Phase 12.1 |
| TEST-06 | Phase 10.3, 10.4, 14.1 |
| TEST-07 | Phase 13.2, 13.3 |
| AUTH-04 (fulfilled) | Phase 10.3 |
| AUTH-05 (extended) | Phase 10.1 |
| PROJ-04 (modified) | Phase 13 |
| UI-01, UI-02, UI-03, UI-07 | Phase 14.3 |
| SEC-03 through SEC-05 | Phase 14.1 |
| MAINT-01 through MAINT-03 | All Phases (enforced throughout) |
