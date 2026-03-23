# TeamWare Project Overview

## Purpose
TeamWare is an ASP.NET Core MVC team collaboration application with project management, task tracking, inbox/GTD, comments, notifications, reviews, admin, user directory, presence, invitations, and a real-time lounge (chat) feature.

## Tech Stack
- .NET 10 / ASP.NET Core MVC (Controllers + Views, NOT Razor Pages)
- C# 14
- EF Core with SQLite
- ASP.NET Identity for auth
- SignalR for real-time (lounge, presence)
- Hangfire for background jobs
- TailwindCSS (npm build step)
- Markdig for Markdown rendering
- xUnit for tests (no mocking library; uses in-memory SQLite)

## Project Structure
```
TeamWare.slnx
TeamWare.Web/           - Main web application
  Controllers/          - MVC controllers
  Data/                 - ApplicationDbContext, SeedData, Migrations/
  Helpers/
  Hubs/                 - SignalR hubs
  Jobs/                 - Hangfire jobs
  Models/               - Domain entities (one type per file)
  Services/             - Service interfaces (IXxxService) and implementations (XxxService)
  ViewComponents/
  ViewModels/
  Views/
  wwwroot/
TeamWare.Tests/         - Test project
  Controllers/
  Data/
  Helpers/
  Infrastructure/
  Jobs/
  Models/
  Services/             - Service unit tests
  Security/
  Views/
```

## Key Conventions
- One type per file (MAINT-01)
- Services use `ServiceResult` / `ServiceResult<T>` pattern
- DbSets use expression-bodied: `public DbSet<X> Xs => Set<X>();`
- Entity configuration in `OnModelCreating` with Fluent API
- Test classes implement `IDisposable`, use in-memory SQLite
- Seed data in `SeedData.InitializeAsync`
- Namespace pattern: `TeamWare.Web.Models`, `TeamWare.Web.Services`, `TeamWare.Web.Data`
- Test namespace: `TeamWare.Tests.Services`, etc.
