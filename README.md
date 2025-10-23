# Shouting Iguana

A professional web crawler and SEO analysis tool built with WPF and .NET 9.

## Stage 1: Core Foundation & Basic Crawling

Currently implementing the foundational features including project management, basic HTTP crawling, SQLite database persistence, and CSV export.

## Prerequisites

- .NET 9.0 SDK
- Visual Studio 2022 or JetBrains Rider
- dotnet-ef tool (installed automatically by migration script)

## Getting Started

1. Clone the repository
2. Open `ShoutingIguana.sln` in your IDE
3. Build the solution
4. Run the WPF application

## Database Migrations

### Creating a New Migration

Run the migration script from the solution root:

```powershell
.\migrations.ps1 -MigrationName "YourMigrationName"
```

Or let it prompt you:

```powershell
.\migrations.ps1
```

### Migration Details

- **Database:** SQLite
- **Context:** `SqliteShoutingIguanaDbContext`
- **Location:** Database stored in `%APPDATA%/ShoutingIguana/projects/`
- **Migrations:** Located in `src/ShoutingIguana.Data/Migrations/`

### Manual Migration Commands

If you prefer manual EF Core commands:

```bash
# Add a migration
dotnet ef migrations add YourMigrationName --context SqliteShoutingIguanaDbContext --project src/ShoutingIguana.Data --startup-project src/ShoutingIguana

# Remove last migration
dotnet ef migrations remove --context SqliteShoutingIguanaDbContext --project src/ShoutingIguana.Data --startup-project src/ShoutingIguana

# Update database
dotnet ef database update --context SqliteShoutingIguanaDbContext --project src/ShoutingIguana.Data --startup-project src/ShoutingIguana
```

Note: Migrations are applied automatically when the application starts.

## Project Structure

```
ShoutingIguana/
├── src/
│   ├── ShoutingIguana.Core/          # Core models, interfaces, services
│   │   ├── Models/                    # Domain entities
│   │   ├── Configuration/             # Settings classes
│   │   ├── Services/                  # Core business logic
│   │   └── Repositories/              # Repository interfaces
│   ├── ShoutingIguana.Data/          # EF Core, SQLite, repositories
│   │   ├── Configurations/            # Entity type configurations
│   │   ├── Migrations/                # EF Core migrations
│   │   └── Repositories/              # Repository implementations
│   └── ShoutingIguana/               # WPF application
│       ├── ViewModels/                # MVVM view models
│       ├── Views/                     # XAML views
│       └── Services/                  # Application services
├── Assets/                            # Application icons
├── migrations.ps1                     # Migration helper script
└── ShoutingIguana.sln                # Visual Studio solution
```

## Features (Stage 1)

### ✅ Implemented
- Modern WPF UI with MaterialDesign theme
- Dependency injection with Microsoft.Extensions.DependencyInjection
- EF Core with SQLite database
- Serilog file logging
- Project management (create, open, save)
- Basic crawl settings configuration
- Navigation between views
- Database migrations infrastructure

### 🚧 In Progress
- HTTP crawl engine
- Robots.txt parsing and respect
- Link extraction with HtmlAgilityPack
- URL inventory and queue management
- Real-time crawl progress dashboard
- CSV export functionality

### 📋 Planned (Stage 2+)
- Playwright integration for JavaScript rendering
- Plugin system with SDK
- Built-in analyzer plugins
- Excel export
- Advanced crawl features (pause/resume, proxy support)
- Settings dialog

## Technology Stack

- **Framework:** .NET 9.0
- **UI:** WPF with MaterialDesign
- **MVVM:** CommunityToolkit.Mvvm
- **Database:** SQLite with EF Core 9.0
- **Logging:** Serilog
- **HTTP:** HttpClient with Polly retry policies
- **Parsing:** HtmlAgilityPack
- **Export:** CsvHelper

## Configuration

Application settings are stored in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=shouting_iguana.db"
  }
}
```

The connection string is overridden at runtime to use the AppData folder.

## License

See [LICENSE](LICENSE) file for details.

## Development Guidelines

- Follow the coding standards in `.cursor/developer.mdc`
- Use the EF Core patterns from `.cursor/efcore.mdc`
- Follow WPF best practices from `.cursor/wpf.mdc`
- Keep the PDR stages in mind when implementing features

## Support

For issues and feature requests, please use the GitHub issue tracker.

