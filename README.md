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

## Tech Stack

- **Backend:** ASP.NET Core MVC (.NET 10)
- **Database:** SQLite via Entity Framework Core 10
- **Authentication:** Microsoft Identity with cookie authentication
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

The test suite includes 480+ tests covering unit tests, integration tests, and security tests.

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
    Infrastructure/           # Error handling and anti-forgery tests
    Models/                   # Entity validation tests
    Security/                 # Security hardening tests
    Services/                 # Unit tests for service layer
    Views/                    # Layout smoke tests and UI consistency tests
```

## Admin Workflows

### Initial Setup

1. Start the application. The admin account (`admin@teamware.local` / `Admin123!`) is created automatically.
2. Log in with the admin credentials.
3. Navigate to **My Profile > Change Password** to set a secure password.

### Password Reset

Administrators can reset any user's password:

1. Log in with an admin account.
2. Navigate to **Reset Password** (available in the sidebar for admin users, or go to `/Account/ResetPassword`).
3. Enter the user's email address and the new password.
4. Click **Reset Password**.

> Only users with the `Admin` role can access this feature. Regular users can change their own password via **My Profile > Change Password**.

### User Management

Members are managed at the project level:

1. Navigate to a project's **Details** page.
2. Use the **Invite Member** form to add a user by email address (the user must have a registered account).
3. Assign roles: **Owner** (full control), **Admin** (can manage members and tasks), or **Member** (can create and manage own tasks).
4. Use **Remove Member** to revoke access.

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

See the [Implementation Plan](TeamWare.Web/Specifications/ImplementationPlan.md) for the phased development roadmap and the [Specification](TeamWare.Web/Specifications/Specification.md) for formal requirements. Each phase has a corresponding GitHub branch (`phase-X/<name>`) and issues for individual work items.

## License

This project is for personal/team use. See repository for license details.
