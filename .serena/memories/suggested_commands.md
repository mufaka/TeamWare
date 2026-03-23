# Suggested Commands

## System
- Shell: `pwsh.exe` (PowerShell)
- Git: `git` (standard)
- File listing: `Get-ChildItem` or `ls`
- Find files: `Get-ChildItem -Recurse -Filter "*.cs"`

## Build
- `dotnet build` (from solution root)
- TailwindCSS builds automatically as a pre-build step via npm

## Test
- `dotnet test` (from solution root)
- `dotnet test --filter "FullyQualifiedName~ClassName"` (filter tests)

## Database / Migrations
- `dotnet ef migrations add MigrationName --project TeamWare.Web`
- `dotnet ef database update --project TeamWare.Web`

## Run
- `dotnet run --project TeamWare.Web`

## Git Workflow
- Branch naming: `phase-{N}/{description}`
- Commit format: `<description>\n\nRefs #<issue>` or `Closes #<issue>`
