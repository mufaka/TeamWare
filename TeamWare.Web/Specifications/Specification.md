# TeamWare - Formal Specification

## 1. Introduction

### 1.1 Purpose

This document provides the formal specification for TeamWare, a web-based project and task management application designed for small teams. It defines the functional and non-functional requirements, system architecture, data model, and user interface guidelines that govern the development of the application.

### 1.2 Scope

TeamWare enables small teams to organize work through project creation, task management, assignment tracking, deadline enforcement, and team communication. The application targets teams that need a lightweight, self-hosted solution without the overhead of enterprise project management tools. The application is designed for small team or home use where external services such as SMTP may not be available.

### 1.3 Definitions and Acronyms

| Term | Definition |
|------|-----------|
| Project | A container for related tasks, owned by a team |
| Task | A unit of work within a project, assignable to one or more team members |
| Team Member | A registered user who participates in one or more projects |
| Sprint | An optional time-boxed iteration for organizing tasks |
| Inbox | A per-user capture list for unprocessed tasks and ideas that have not yet been clarified, prioritized, or assigned to a project |
| Next Action | A task that has been clarified and identified as the immediate next step a user should work on |
| Review | A periodic workflow where users examine their inbox, active tasks, and upcoming deadlines to maintain an organized and current task list |
| Someday/Maybe | A holding list for tasks or ideas that are not actionable now but may become relevant in the future |
| GTD | Getting Things Done, a productivity methodology focused on capturing, clarifying, organizing, reviewing, and engaging with work |
| HTMX | A library for accessing modern browser features directly from HTML |
| Alpine.js | A lightweight JavaScript framework for composing behavior directly in markup |
| Tailwind CSS | A utility-first CSS framework |

---

## 2. System Overview

### 2.1 System Context

TeamWare is a server-rendered web application. The backend is built with ASP.NET Core, serving HTML enhanced with HTMX for dynamic interactions, Alpine.js for client-side behavior, and Tailwind CSS 4 for styling. Data is persisted in a SQLite database. Authentication and authorization are handled by Microsoft Identity.

### 2.2 Technology Stack

| Layer | Technology |
|-------|-----------|
| Backend Framework | ASP.NET Core (.NET 10) |
| Frontend Interactivity | HTMX |
| Frontend Behavior | Alpine.js |
| Frontend Styling | Tailwind CSS 4 |
| Database | SQLite |
| Authentication/Authorization | Microsoft Identity |

---

## 3. Functional Requirements

### 3.1 Authentication and Authorization

| ID | Requirement |
|----|------------|
| AUTH-01 | The system shall allow users to register with an email address and password |
| AUTH-02 | The system shall allow users to log in with their registered credentials |
| AUTH-03 | The system shall allow users to log out of their session |
| AUTH-04 | The system shall allow administrators to reset a user's password directly, as SMTP services may not be available |
| AUTH-05 | The system shall enforce role-based access control with the following roles: Owner, Admin, Member |
| AUTH-06 | Email confirmation for new registrations shall be disabled by default, as SMTP services may not be available in small team or home deployments. If SMTP is configured, email confirmation may be optionally enabled |

### 3.2 User Management

| ID | Requirement |
|----|------------|
| USER-01 | Users shall be able to view and edit their profile information (display name, email, avatar) |
| USER-02 | Users shall be able to change their password |
| USER-03 | Users shall be able to set their preferred theme (light or dark mode) |

### 3.3 Inbox

| ID | Requirement |
|----|------------|
| INBOX-01 | Each user shall have a personal inbox for quickly capturing tasks, ideas, and notes without requiring immediate categorization |
| INBOX-02 | Users shall be able to add items to their inbox with a title and optional description |
| INBOX-03 | Users shall be able to clarify an inbox item by assigning it to a project, setting its priority, and optionally designating it as a Next Action or Someday/Maybe item |
| INBOX-04 | Users shall be able to convert an inbox item directly into a project task during clarification |
| INBOX-05 | Users shall be able to delete or dismiss inbox items that are no longer relevant |
| INBOX-06 | The inbox shall display a count of unprocessed items to encourage regular processing |
| INBOX-07 | Users shall be able to move an inbox item to their Someday/Maybe list if it is not currently actionable |

### 3.4 Project Management

| ID | Requirement |
|----|------------|
| PROJ-01 | Authenticated users shall be able to create a new project with a name and optional description |
| PROJ-02 | Project owners and admins shall be able to edit project details (name, description, status) |
| PROJ-03 | Project owners shall be able to archive or delete a project |
| PROJ-04 | Project owners and admins shall be able to invite team members to a project |
| PROJ-05 | Project owners and admins shall be able to remove team members from a project |
| PROJ-06 | Project owners shall be able to assign roles (Admin, Member) to team members within the project |
| PROJ-07 | Users shall be able to view a list of all projects they belong to |
| PROJ-08 | Each project shall have a dashboard displaying summary information (task counts by status, upcoming deadlines, recent activity) |

### 3.5 Task Management

| ID | Requirement |
|----|------------|
| TASK-01 | Project members shall be able to create tasks within a project |
| TASK-02 | Each task shall have a title, optional description, status, priority, and optional due date |
| TASK-03 | Tasks shall support the following statuses: To Do, In Progress, In Review, Done |
| TASK-04 | Tasks shall support the following priority levels: Low, Medium, High, Critical |
| TASK-05 | Project members shall be able to assign one or more team members to a task |
| TASK-06 | Project members shall be able to edit task details |
| TASK-07 | Project members shall be able to change the status of a task |
| TASK-08 | Project owners and admins shall be able to delete tasks |
| TASK-09 | Users shall be able to filter and sort tasks by status, priority, assignee, and due date |
| TASK-10 | Users shall be able to search tasks by title or description within a project |
| TASK-11 | Users shall be able to mark a task as a Next Action, indicating it is the immediate next step they should take |
| TASK-12 | Users shall be able to mark a task as Someday/Maybe, removing it from active views while retaining it for future consideration |
| TASK-13 | The system shall provide a "What's Next" view that displays only the user's Next Action tasks across all projects, ordered by priority and due date |
| TASK-14 | The "What's Next" view shall limit visible tasks to a focused, manageable list to prevent the user from being overwhelmed |

### 3.6 Progress Tracking

| ID | Requirement |
|----|------------|
| PROG-01 | The system shall display task completion statistics per project (percentage complete, tasks by status) |
| PROG-02 | The system shall provide a timeline or activity log showing task state changes |
| PROG-03 | The system shall highlight overdue tasks |
| PROG-04 | The system shall display upcoming deadlines on the project dashboard |

### 3.7 Communication

| ID | Requirement |
|----|------------|
| COMM-01 | Project members shall be able to add comments to a task |
| COMM-02 | Comments shall display the author and timestamp |
| COMM-03 | Project members shall be able to edit or delete their own comments |
| COMM-04 | The system shall notify assigned users when a comment is added to their task |

### 3.8 Notifications

| ID | Requirement |
|----|------------|
| NOTIF-01 | The system shall generate in-app notifications for task assignments |
| NOTIF-02 | The system shall generate in-app notifications for approaching deadlines |
| NOTIF-03 | The system shall generate in-app notifications for task status changes on tasks assigned to the user |
| NOTIF-04 | Users shall be able to view and dismiss their notifications |
| NOTIF-05 | The system shall prompt users to process their inbox when unprocessed items exceed a configurable threshold |

### 3.9 Review

| ID | Requirement |
|----|------------|
| REV-01 | The system shall provide a Review page where users can examine their inbox items, active tasks, Next Actions, and Someday/Maybe items in a guided workflow |
| REV-02 | During review, users shall be able to re-prioritize tasks, update statuses, move items between lists (Next Action, Someday/Maybe, active), or dismiss items |
| REV-03 | The system shall prompt users to perform a review on a configurable schedule (default: weekly) via an in-app notification |
| REV-04 | The system shall track when the user last completed a review and display this on their dashboard |

---

## 4. Non-Functional Requirements

### 4.1 User Interface

| ID | Requirement |
|----|------------|
| UI-01 | The UI shall be responsive and functional on desktop (1024px+) and mobile (320px+) viewports |
| UI-02 | The UI shall support light and dark themes, with the user's preference persisted |
| UI-03 | The UI shall follow a modern, clean design that prioritizes clarity and simplicity |
| UI-04 | Page interactions shall use HTMX for partial page updates to minimize full page reloads |
| UI-05 | Client-side behavior (dropdowns, modals, toggles) shall be implemented with Alpine.js |
| UI-06 | All styling shall use Tailwind CSS 4 utility classes |
| UI-07 | The UI shall not contain emoticons or emojis |

### 4.2 Performance

| ID | Requirement |
|----|------------|
| PERF-01 | Page load times shall not exceed 2 seconds under normal operating conditions |
| PERF-02 | HTMX partial updates shall complete within 500 milliseconds under normal operating conditions |
| PERF-03 | The application shall support at least 50 concurrent users without degradation |

### 4.3 Security

| ID | Requirement |
|----|------------|
| SEC-01 | All passwords shall be hashed using the default Microsoft Identity hashing algorithm |
| SEC-02 | The application shall enforce HTTPS for all connections |
| SEC-03 | The application shall implement CSRF protection on all form submissions |
| SEC-04 | The application shall validate and sanitize all user input |
| SEC-05 | Authorization checks shall be enforced on all endpoints |

### 4.4 Reliability

| ID | Requirement |
|----|------------|
| REL-01 | The application shall handle database errors gracefully and display user-friendly error messages |
| REL-02 | The application shall log errors for diagnostic purposes |

### 4.5 Maintainability

| ID | Requirement |
|----|------------|
| MAINT-01 | Each type shall reside in its own file |
| MAINT-02 | All new features shall have corresponding automated test cases |
| MAINT-03 | The codebase shall follow standard ASP.NET Core project structure conventions |

---

## 5. Data Model

### 5.1 Entities

#### User

| Field | Type | Constraints |
|-------|------|------------|
| Id | string | Primary key (Identity) |
| UserName | string | Required, unique |
| Email | string | Required, unique |
| DisplayName | string | Required, max 100 characters |
| AvatarUrl | string | Optional |
| ThemePreference | string | "Light" or "Dark", default "Light" |

#### Project

| Field | Type | Constraints |
|-------|------|------------|
| Id | int | Primary key, auto-increment |
| Name | string | Required, max 200 characters |
| Description | string | Optional, max 2000 characters |
| Status | string | "Active", "Archived"; default "Active" |
| CreatedAt | datetime | Required, set on creation |
| UpdatedAt | datetime | Required, updated on modification |

#### ProjectMember

| Field | Type | Constraints |
|-------|------|------------|
| Id | int | Primary key, auto-increment |
| ProjectId | int | Foreign key to Project |
| UserId | string | Foreign key to User |
| Role | string | "Owner", "Admin", "Member" |
| JoinedAt | datetime | Required, set on creation |

#### InboxItem

| Field | Type | Constraints |
|-------|------|------------|
| Id | int | Primary key, auto-increment |
| UserId | string | Foreign key to User |
| Title | string | Required, max 300 characters |
| Description | string | Optional, max 5000 characters |
| CreatedAt | datetime | Required, set on creation |
| ProcessedAt | datetime | Optional, set when the item is clarified or dismissed |
| IsProcessed | bool | Default false |

#### TaskItem

| Field | Type | Constraints |
|-------|------|------------|
| Id | int | Primary key, auto-increment |
| ProjectId | int | Foreign key to Project |
| Title | string | Required, max 300 characters |
| Description | string | Optional, max 5000 characters |
| Status | string | "ToDo", "InProgress", "InReview", "Done"; default "ToDo" |
| Priority | string | "Low", "Medium", "High", "Critical"; default "Medium" |
| IsNextAction | bool | Default false; true if the task is flagged as a Next Action for the assigned user |
| IsSomedayMaybe | bool | Default false; true if the task is deferred to the Someday/Maybe list |
| DueDate | datetime | Optional |
| CreatedAt | datetime | Required, set on creation |
| UpdatedAt | datetime | Required, updated on modification |
| CreatedById | string | Foreign key to User |

#### TaskAssignment

| Field | Type | Constraints |
|-------|------|------------|
| Id | int | Primary key, auto-increment |
| TaskItemId | int | Foreign key to TaskItem |
| UserId | string | Foreign key to User |
| AssignedAt | datetime | Required, set on creation |

#### Comment

| Field | Type | Constraints |
|-------|------|------------|
| Id | int | Primary key, auto-increment |
| TaskItemId | int | Foreign key to TaskItem |
| AuthorId | string | Foreign key to User |
| Content | string | Required, max 5000 characters |
| CreatedAt | datetime | Required, set on creation |
| UpdatedAt | datetime | Required, updated on modification |

#### Notification

| Field | Type | Constraints |
|-------|------|------------|
| Id | int | Primary key, auto-increment |
| UserId | string | Foreign key to User |
| Message | string | Required, max 500 characters |
| Type | string | "TaskAssigned", "DeadlineApproaching", "StatusChanged", "CommentAdded" |
| IsRead | bool | Default false |
| CreatedAt | datetime | Required, set on creation |
| ReferenceId | int | Optional, foreign key to related entity |

#### UserReview

| Field | Type | Constraints |
|-------|------|------------|
| Id | int | Primary key, auto-increment |
| UserId | string | Foreign key to User |
| CompletedAt | datetime | Required, set when the review is completed |
| Notes | string | Optional, max 2000 characters |

### 5.2 Entity Relationships

- A **User** can be a member of many **Projects** (via ProjectMember).
- A **User** has many **InboxItems**.
- A **User** has many **UserReviews**.
- A **Project** has many **TaskItems**.
- A **TaskItem** can be assigned to many **Users** (via TaskAssignment).
- A **TaskItem** has many **Comments**.
- A **User** has many **Notifications**.

---

## 6. Application Structure

### 6.1 Backend

The ASP.NET Core backend shall follow a layered architecture:

| Layer | Responsibility |
|-------|---------------|
| Pages / Endpoints | Razor Pages or Minimal API endpoints handling HTTP requests and rendering views |
| Services | Business logic, validation, and orchestration |
| Data Access | Entity Framework Core with SQLite provider |
| Identity | Microsoft Identity for user management, authentication, and authorization |

### 6.2 Frontend

| Concern | Technology | Usage |
|---------|-----------|-------|
| Server Rendering | Razor Pages / Partial Views | Primary HTML generation |
| Dynamic Updates | HTMX | Partial page updates, form submissions, lazy loading |
| Client Behavior | Alpine.js | Dropdowns, modals, theme toggling, local UI state |
| Styling | Tailwind CSS 4 | All visual styling via utility classes |

---

## 7. Testing Requirements

| ID | Requirement |
|----|------------|
| TEST-01 | All service-layer logic shall have unit tests |
| TEST-02 | All endpoints shall have integration tests verifying correct HTTP responses and authorization |
| TEST-03 | New features shall not be considered complete without passing test cases |
| TEST-04 | Tests shall be organized in a dedicated test project within the solution |

---

## 8. Future Considerations

The following features are out of scope for the initial release but may be considered for future iterations:

- File attachments on tasks
- Recurring tasks
- Time tracking
- Kanban board view
- Calendar view integration
- External integrations (email notifications, webhooks)
- API for third-party clients
