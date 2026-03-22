# TeamWare

TeamWare is a lightweight, self-hosted project and task management application built with ASP.NET Core MVC. It follows a GTD (Getting Things Done) inspired workflow with features for inbox capture, task management, project organization, periodic reviews, and in-app notifications.

## Features

- **Project Management** — Create, edit, archive, and delete projects; invite members with Owner/Admin/Member roles
- **Task Management** — Full CRUD with status tracking (To Do, In Progress, In Review, Done), priority levels, due dates, and member assignment
- **Inbox Capture** — Quick-add items to a personal inbox, then clarify and convert them to project tasks
- **What's Next** — A focused view of your Next Action tasks across all projects, ordered by priority and due date
- **Someday/Maybe** — Park ideas that are not actionable right now
- **Progress Tracking** — Per-project completion statistics, activity timeline, overdue task highlighting, and upcoming deadline visibility
- **Comments** — Threaded comments on tasks with real-time HTMX updates
- **Notifications** — In-app alerts for task assignments, status changes, new comments, and review reminders
- **Weekly Review** — A guided multi-step review workflow to process your inbox, re-prioritize tasks, and review Someday/Maybe items
- **User Profile** — Manage display name, avatar URL, password, and light/dark theme preference
- **Dashboard** — A personal dashboard aggregating inbox count, What's Next items, project summaries, upcoming deadlines, review status, and recent notifications
- **System Administration** — Site-wide admin dashboard with user management (lock, unlock, reset password, promote/demote), system statistics, and admin activity log
- **User Directory** — Searchable directory of all registered users with sortable listing, user profiles showing project memberships, task statistics, and recent activity
- **Real-Time Presence** — Online/offline status indicators via SignalR, "last active" timestamps on user profiles, and a global activity feed across all projects
- **Project Invitations** — Accept/decline invitation workflow with bulk invite support, pending invitation management, and notification integration
- **Project Lounge** — Real-time chat rooms for each project and a site-wide #general room, with @mentions, emoji reactions, message pinning, message-to-task conversion, and unread tracking

## Tech Stack

- **Backend:** ASP.NET Core MVC (.NET 10)
- **Database:** SQLite via Entity Framework Core 10
- **Authentication:** Microsoft Identity with cookie authentication
- **Real-Time:** SignalR for user presence tracking and lounge messaging
- **Background Jobs:** Hangfire for scheduled tasks (message retention cleanup)
- **Frontend:** Tailwind CSS 4.2, HTMX 2.0, Alpine.js 3.14
- **Client Validation:** aspnet-client-validation (jQuery-free)
- **Testing:** xUnit with `Microsoft.AspNetCore.Mvc.Testing`

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js](https://nodejs.org/) (for Tailwind CSS CLI build, only needed for CSS development)

## Getting Started

### Clone the repository

```bash
git clone https://github.com/mufaka/TeamWare.git
cd TeamWare
```

### Build

```bash
dotnet build
```

The build automatically compiles Tailwind CSS via the `@tailwindcss/cli` npm package.

### Run the application

```bash
dotnet run --project TeamWare.Web
```

The application will be available at `https://localhost:5001` (or the port shown in the console output).

On first run, the database is automatically created and migrated, and a default administrator account is seeded:

| Field    | Value                  |
|----------|------------------------|
| Email    | `admin@teamware.local` |
| Password | `Admin123!`            |

> **Important:** Change the admin password immediately after first login via **My Profile > Change Password**.

### Run tests

```bash
dotnet test
```

The test suite includes 1050+ tests covering unit tests, integration tests, and security tests.

## Configuration

### Database

TeamWare uses SQLite by default. The connection string is configured in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=TeamWare.db"
  }
}
```

The database file (`TeamWare.db`) is created in the application's working directory. EF Core migrations are applied automatically on startup.

### HTTPS

HTTPS redirection and HSTS are enforced in production. For local development, the .NET development certificate is used. To trust it:

```bash
dotnet dev-certs https --trust
```

### Environment

Set the environment variable to control behavior:

- `Development` — Detailed error pages, no HSTS
- `Production` — Generic error pages, HSTS enabled, HTTPS enforced

```bash
# Linux/macOS
export ASPNETCORE_ENVIRONMENT=Production

# Windows PowerShell
$env:ASPNETCORE_ENVIRONMENT = "Production"
```

## Project Structure

```
TeamWare/
  TeamWare.Web/              # ASP.NET Core MVC application
    Controllers/              # MVC controllers
    Data/                     # DbContext, migrations, and seed data
    Helpers/                  # HTML helper extensions
    Hubs/                     # SignalR hubs (PresenceHub, LoungeHub)
    Models/                   # Domain entities (one class per file)
    Services/                 # Business logic interfaces and implementations
    Views/                    # Razor views organized by controller
    ViewComponents/           # View components (InboxCount, NotificationCount, ReviewStatus)
    ViewModels/               # View-specific models with validation attributes
    Specifications/           # Formal specification and implementation plan
    wwwroot/                  # Static files (CSS, JS, libraries)
  TeamWare.Tests/             # xUnit test project
    Controllers/              # Integration tests for controllers
    Data/                     # Database context tests
    Infrastructure/           # Error handling, anti-forgery, and SignalR hub tests
    Models/                   # Entity validation tests
    Performance/              # Performance and index verification tests
    Security/                 # Security hardening and authorization tests
    Services/                 # Unit tests for service layer
    Views/                    # Layout smoke tests and UI consistency tests
```

## Admin Workflows

### Initial Setup

1. Start the application. The admin account (`admin@teamware.local` / `Admin123!`) is created automatically.
2. Log in with the admin credentials.
3. Navigate to **My Profile > Change Password** to set a secure password.

### Admin Dashboard

The admin dashboard (`/Admin/Dashboard`) provides system-wide statistics (total users, projects, and tasks) and quick access to user management and the admin activity log. Only users with the `Admin` role can access these features.

### User Management

Administrators can manage all user accounts from the admin dashboard:

1. Navigate to **Admin Dashboard > User Management** (or go to `/Admin/Users`).
2. **Search** — Filter users by display name or email.
3. **Lock/Unlock** — Lock a user account to prevent login, or unlock a previously locked account.
4. **Reset Password** — Set a new password for any user account.
5. **Promote/Demote** — Change a user's role between Admin and User.

All administrative actions are recorded in the admin activity log for audit purposes.

### Admin Activity Log

The activity log (`/Admin/ActivityLog`) shows a paginated, chronological record of all administrative actions including who performed the action, who was affected, what action was taken, and when it occurred.

### Password Reset

Administrators can reset any user's password:

1. Log in with an admin account.
2. Navigate to **Admin Dashboard > User Management**.
3. Click **Reset Password** next to the target user.
4. Enter and confirm the new password.
5. Click **Reset Password**.

> Only users with the `Admin` role can access this feature. Regular users can change their own password via **My Profile > Change Password**.

### Project-Level Member Management

Members are managed at the project level through the invitation workflow:

1. Navigate to a project's **Details** page.
2. Use the **Invite Member** autocomplete to search and select a user from the directory.
3. Choose a role: **Owner** (full control), **Admin** (manage members and tasks), or **Member** (create and manage own tasks).
4. Click **Send Invitation**. The invited user receives an in-app notification.
5. The invited user can **Accept** or **Decline** the invitation from their notifications or the pending invitations page.

## User Directory

The user directory (`/Directory`) lists all registered users and supports:

- **Search** — Find users by display name or email.
- **Sort** — Sort the list by name or email, ascending or descending.
- **User Profiles** — Click on a user to view their profile page showing:
  - Display name, avatar, and email
  - Project membership list
  - Task statistics (assigned, completed, overdue)
  - Recent activity (last 30 days)
  - Online/offline presence indicator and last active timestamp
  - Quick link to invite the user to a project

All registered users appear in the directory. There is no opt-out mechanism.

## Project Invitations

TeamWare uses an accept/decline invitation workflow for adding members to projects:

1. **Sending Invitations** — Project owners and admins can invite users from the project details page. Autocomplete search pulls from the user directory.
2. **Bulk Invitations** — Multiple users can be invited to a project at once.
3. **Pending Invitations** — Project owners/admins can view pending invitations for their projects at `/Invitation/PendingForProject`.
4. **Accepting/Declining** — Invited users see pending invitations at `/Invitation/PendingForUser` and via notification links. Accepting creates the project membership; declining removes the invitation.
5. **Notifications** — Invitation notifications include direct accept/decline action links.

## Real-Time Presence

TeamWare uses SignalR to provide real-time user presence:

- **Online/Offline Indicators** — Green dot indicators appear next to online users in the directory and on profile pages.
- **Last Active Timestamp** — User profile pages show when the user was last active.
- **Global Activity Feed** — The dashboard includes a global activity feed showing recent actions across all projects. Activity from projects the viewer is not a member of is shown in a masked/generic format to preserve privacy.
- **Automatic Connection** — SignalR connects automatically when an authenticated user loads any page. No manual setup is required.

## GTD Workflow Guide

TeamWare implements a GTD (Getting Things Done) inspired workflow. Here's how to use it effectively:

### 1. Capture — Inbox

Use the **Inbox** to capture anything that comes to mind:

- Click **Inbox** in the sidebar.
- Use the **Quick Add** field to rapidly capture items.
- Or click **Add Item** for a title and description.
- Items stay in the inbox until you process them.

### 2. Clarify — Process Your Inbox

For each inbox item, decide what to do:

- Click **Clarify** to assign it to a project, set priority, due date, and optionally flag it as a Next Action or Someday/Maybe.
- Click **Dismiss** to discard items that are no longer relevant.
- The inbox badge in the sidebar shows your unprocessed item count.

### 3. Organize — Projects and Tasks

- Create **Projects** to group related work.
- Within a project, create **Tasks** with status, priority, due dates, and assignees.
- Use task statuses: **To Do**, **In Progress**, **In Review**, **Done**.
- Use the filter and search features on the task list to find what you need.

### 4. Reflect — Weekly Review

The **Weekly Review** is a guided process to keep your system current:

1. Click **Weekly Review** in the sidebar (or respond to the review-due prompt on your dashboard).
2. **Step 1:** Process any remaining inbox items.
3. **Step 2:** Review active tasks — update status, re-prioritize, or flag as Next Action / Someday/Maybe.
4. **Step 3:** Review Someday/Maybe items — promote to active, keep, or dismiss.
5. Click **Complete Review** when finished.

The dashboard shows your last review date and prompts you when a new review is due.

### 5. Engage — What's Next

The **What's Next** view shows your Next Action tasks across all projects, sorted by priority and due date. This is your focused work list — start here when deciding what to do next.

### Someday/Maybe

Items flagged as Someday/Maybe are parked for future consideration. Access them via **Someday/Maybe** in the sidebar. During your weekly review, decide whether to activate, keep, or dismiss them.

## Development

See the [Implementation Plan](TeamWare.Web/Specifications/ImplementationPlan.md) and the [Social Features Implementation Plan](TeamWare.Web/Specifications/SocialFeaturesImplementationPlan.md) for the phased development roadmap. See the [Specification](TeamWare.Web/Specifications/Specification.md) and [Social Features Specification](TeamWare.Web/Specifications/SocialFeaturesSpecification.md) for formal requirements. Each phase has a corresponding GitHub branch (`phase-X/<name>`) and issues for individual work items.

### SignalR Configuration

SignalR is configured automatically in `Program.cs`. Two hubs are registered:

- `PresenceHub` at `/hubs/presence` — Real-time user online/offline presence tracking.
- `LoungeHub` at `/hubs/lounge` — Real-time lounge messaging, reactions, and read position updates.

No additional configuration is needed for development. For production deployments behind a load balancer, consider configuring a SignalR backplane (e.g., Redis) if scaling to multiple server instances.

### Hangfire Configuration

Hangfire is used for background job scheduling. It is configured automatically in `Program.cs` with in-memory storage. The Hangfire dashboard is available at `/hangfire` for users with the `Admin` role.

The following recurring jobs are registered:

- **lounge-retention-cleanup** — Runs daily to delete non-pinned lounge messages older than 30 days.

## Project Lounge

The Project Lounge provides real-time chat rooms for team communication:

### Rooms

- **#general** — A site-wide room accessible to all authenticated users.
- **Project rooms** — Each project has its own lounge room, accessible only to project members.

### Features

- **Real-time messaging** — Messages are delivered instantly via SignalR. No page refresh needed.
- **@mentions** — Type `@` followed by a username to mention a team member. Mentioned users receive an in-app notification. Autocomplete suggests matching members as you type.
- **Emoji reactions** — React to messages with +1, heart, laugh, rocket, or eyes reactions. Click a reaction to toggle it on/off.
- **Message pinning** — Project owners/admins (or site admins in #general) can pin important messages. Pinned messages appear in a collapsible banner at the top of the room.
- **Message editing** — Edit your own messages after sending. Edited messages display an "(edited)" indicator.
- **Message deletion** — Delete your own messages, or (for admins) any message in the room.
- **Message-to-task conversion** — In project rooms, convert a lounge message into a project task with one click. The task is pre-populated from the message content.
- **Unread tracking** — Unread message counts appear as badges in the sidebar. A "new messages" divider marks where you left off.
- **Message history** — Scroll up to load older messages. Messages are paginated for performance.

### Message Retention

Lounge messages are retained for 30 days. After 30 days, non-pinned messages are automatically deleted by the daily retention job. Pinned messages are exempt from cleanup and are retained indefinitely.

## License

This project is for personal/team use. See repository for license details.
