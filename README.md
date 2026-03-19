# TeamWare

TeamWare is a lightweight, self-hosted project and task management application built with ASP.NET Core MVC. It follows a GTD (Getting Things Done) inspired workflow with features for inbox capture, task management, project organization, and periodic reviews.

## Tech Stack

- **Backend:** ASP.NET Core MVC (.NET 10)
- **Database:** SQLite via Entity Framework Core
- **Authentication:** Microsoft Identity with cookie authentication
- **Frontend:** Tailwind CSS 4, HTMX, Alpine.js

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

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

### Run the application

```bash
dotnet run --project TeamWare.Web
```

The application will be available at `https://localhost:5001` (or the port shown in the console output).

### Run tests

```bash
dotnet test
```

## Project Structure

```
TeamWare/
  TeamWare.Web/           # ASP.NET Core MVC application
    Controllers/           # MVC controllers
    Data/                  # DbContext and EF Core configuration
    Models/                # Domain entities (one class per file)
    Services/              # Business logic interfaces and implementations
    Views/                 # Razor views organized by controller
    ViewModels/            # View-specific models
    wwwroot/               # Static files (CSS, JS, libraries)
  TeamWare.Tests/          # xUnit test project
```

## Development

See the [Implementation Plan](TeamWare.Web/Specifications/ImplementationPlan.md) for the phased development roadmap. Each phase has a corresponding GitHub branch (`phase-X/<name>`) and issues for individual work items.

## License

This project is for personal/team use. See repository for license details.
