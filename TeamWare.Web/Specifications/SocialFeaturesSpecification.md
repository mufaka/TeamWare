# TeamWare - Social Features Specification

## 1. Introduction

### 1.1 Purpose

This document provides the formal specification for the social and user-management features being added to TeamWare. It defines the functional requirements, data model additions, and technology changes needed to support user discovery, system administration, user activity and presence, and improved project invitations. This specification is a companion to the [main TeamWare specification](Specification.md) and follows the same conventions.

### 1.2 Scope

These features address three gaps in the existing application:

1. **User Discovery** - Users cannot find or browse other users outside of a specific project context.
2. **System Administration** - There is no site-wide administrator role or tooling for managing users.
3. **User Visibility** - There is no indication of who is active on the platform or what they are working on.

This work begins at **Phase 10** and is governed by a separate [implementation plan](SocialFeaturesImplementationPlan.md).

### 1.3 Definitions and Acronyms

| Term | Definition |
|------|-----------|
| User Directory | A searchable list of all registered users on the platform |
| Site Admin | A user with the system-wide "Admin" Identity role, granting access to administrative functions |
| Admin Activity Log | A persistent record of administrative actions taken by site admins |
| Presence | A real-time indicator of whether a user is currently online or offline |
| SignalR | A library for ASP.NET Core that enables real-time web functionality via WebSockets |
| Project Invitation | A request sent by a project owner or admin for a user to join a project, requiring acceptance |

### 1.4 Design Principles

- TeamWare targets **small teams and home use**. The entire installation is effectively the team. Features are designed with full openness among authenticated users in mind.
- All registered users are visible to all other authenticated users. There are no privacy opt-outs for the user directory.
- SignalR is introduced for presence but is intended as **foundational infrastructure** for future real-time features.

---

## 2. Technology Additions

| Layer | Technology | Purpose |
|-------|-----------|---------|
| Real-Time Communication | ASP.NET Core SignalR | Online/offline presence indicators; future real-time features |

All other technology choices remain unchanged from the [main specification](Specification.md).

---

## 3. Functional Requirements

### 3.1 User Directory

| ID | Requirement |
|----|------------|
| DIR-01 | The system shall provide a searchable directory of all registered users, accessible to any authenticated user |
| DIR-02 | The directory shall support searching users by display name or email address |
| DIR-03 | The directory list shall be sortable by display name or email address |
| DIR-04 | Each user in the directory shall link to a user profile page |
| DIR-05 | The user profile page shall display the user's display name, avatar, and email address |
| DIR-06 | The user profile page shall display a list of all projects the user belongs to |
| DIR-07 | The user profile page shall display task statistics: tasks assigned, tasks completed, and tasks overdue |
| DIR-08 | The user profile page shall display a recent activity feed showing the last 30 days of activity across all projects |
| DIR-09 | The user profile page shall provide a link to invite the user to a project |
| DIR-10 | All registered users shall appear in the directory. There is no opt-out mechanism |

### 3.2 System Administration

| ID | Requirement |
|----|------------|
| ADMIN-01 | The system shall support a site-wide administrator role implemented via ASP.NET Identity roles |
| ADMIN-02 | The system shall have exactly two site-level roles: Admin and User |
| ADMIN-03 | The first administrator account shall be seeded at application startup |
| ADMIN-04 | Site admins shall have access to an admin dashboard |
| ADMIN-05 | The admin dashboard shall display a list of all users with search and filter capabilities |
| ADMIN-06 | Site admins shall be able to disable or lock a user account |
| ADMIN-07 | Site admins shall be able to reset a user's password (fulfills AUTH-04) |
| ADMIN-08 | Site admins shall be able to promote a user to the Admin role or demote an admin to the User role |
| ADMIN-09 | The admin dashboard shall display system-wide statistics: total users, total projects, and total tasks |
| ADMIN-10 | Site admins shall be able to view and edit any project, regardless of project membership |
| ADMIN-11 | The system shall maintain an admin activity log recording all administrative actions |
| ADMIN-12 | Each admin activity log entry shall record the action performed, the admin who performed it, the target of the action, and a timestamp |

### 3.3 User Activity and Presence

| ID | Requirement |
|----|------------|
| ACTV-01 | The system shall track and display a "last active" timestamp on each user's profile |
| ACTV-02 | The system shall provide a global activity feed on the dashboard showing recent activity across all projects |
| ACTV-03 | For projects the viewer is a member of, the activity feed shall display full detail (task titles, description changes, comments) |
| ACTV-04 | For projects the viewer is not a member of, the activity feed shall display a masked format showing only the activity type and project name |
| ACTV-05 | The system shall provide a real-time online/offline presence indicator for each user, powered by SignalR |
| ACTV-06 | The SignalR infrastructure shall be implemented as a foundation for future real-time features |

### 3.4 Project Invitation Improvements

| ID | Requirement |
|----|------------|
| INVITE-01 | The project member invitation flow shall provide autocomplete search powered by the user directory |
| INVITE-02 | Project invitations shall require acceptance by the invitee before membership is granted |
| INVITE-03 | Invitees shall be able to accept or decline a project invitation |
| INVITE-04 | Project owners and admins shall be able to view a list of pending invitations for their project |
| INVITE-05 | Project owners and admins shall be able to invite multiple users at once (bulk invite) |
| INVITE-06 | The system shall send an in-app notification to the user when they are invited to a project |
| INVITE-07 | The notification type for project invitations shall be "ProjectInvitation" |

### 3.5 Notification Additions

These requirements extend the existing notification system defined in the main specification (NOTIF-01 through NOTIF-05).

| ID | Requirement |
|----|------------|
| NOTIF-06 | The system shall generate an in-app notification when a user is invited to a project |
| NOTIF-07 | The project invitation notification shall include a link to accept or decline the invitation |

---

## 4. Data Model

### 4.1 New Entities

#### AdminActivityLog

| Field | Type | Constraints |
|-------|------|------------|
| Id | int | Primary key, auto-increment |
| AdminUserId | string | Foreign key to User; the admin who performed the action |
| Action | string | Required, max 100 characters (e.g., "LockAccount", "ResetPassword", "PromoteToAdmin", "DemoteToUser", "EditProject") |
| TargetUserId | string | Optional, foreign key to User; the user affected by the action |
| TargetProjectId | int | Optional, foreign key to Project; the project affected by the action |
| Details | string | Optional, max 1000 characters; additional context about the action |
| CreatedAt | datetime | Required, set on creation |

#### ProjectInvitation

| Field | Type | Constraints |
|-------|------|------------|
| Id | int | Primary key, auto-increment |
| ProjectId | int | Foreign key to Project |
| InvitedUserId | string | Foreign key to User; the user being invited |
| InvitedByUserId | string | Foreign key to User; the user who sent the invitation |
| Status | string | "Pending", "Accepted", "Declined"; default "Pending" |
| Role | string | "Admin", "Member"; the role the user will have if they accept |
| CreatedAt | datetime | Required, set on creation |
| RespondedAt | datetime | Optional, set when the user accepts or declines |

### 4.2 Modified Entities

#### Notification

The existing Notification entity's `Type` field adds the following value:

| New Type Value | Description |
|----------------|------------|
| "ProjectInvitation" | Generated when a user is invited to a project |

The existing `ReferenceId` field shall reference the `ProjectInvitation.Id` for invitation notifications.

### 4.3 New Entity Relationships

- A **User** (as admin) has many **AdminActivityLog** entries.
- A **Project** has many **ProjectInvitations**.
- A **User** has many **ProjectInvitations** (as invitee).
- A **User** has many **ProjectInvitations** (as inviter).

### 4.4 Technology Infrastructure

#### SignalR Hub

A SignalR hub shall be introduced to support real-time presence:

- Authenticated users connect to the hub on page load
- The hub tracks connected users and broadcasts online/offline status changes
- The hub infrastructure is designed for reuse by future real-time features

---

## 5. Changes to Existing Requirements

The following existing requirements from the [main specification](Specification.md) are affected by this work:

| Requirement | Change |
|-------------|--------|
| AUTH-04 | Fulfilled by ADMIN-07. Site admins (Identity role) can reset any user's password via the admin dashboard. |
| AUTH-05 | Extended. In addition to per-project roles (Owner, Admin, Member), the system now enforces a site-wide role (Admin, User) via Identity roles. |
| PROJ-04 | Modified by INVITE-02. Inviting a team member now creates a pending invitation rather than adding the member directly. |

---

## 6. Testing Requirements

All testing requirements from the main specification (TEST-01 through TEST-04) apply to this work. Additionally:

| ID | Requirement |
|----|------------|
| TEST-05 | SignalR hub connection and presence tracking shall have integration tests |
| TEST-06 | Admin authorization checks shall be tested to ensure non-admin users cannot access admin endpoints |
| TEST-07 | The project invitation accept/decline workflow shall have end-to-end tests |

---

## 7. Future Considerations

The following features were discussed during planning but are deferred to future iterations:

- **Project Lounge** - A per-project persistent chat room powered by SignalR, with @ mention notifications. Builds on the SignalR infrastructure introduced by this specification.
