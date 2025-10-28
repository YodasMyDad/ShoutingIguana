# Shouting Iguana

<img src="Assets/logo.png" alt="Shouting Iguana" width="100">

**Enterprise-grade web crawler and SEO analysis platform built on .NET 9**

Shouting Iguana is a powerful desktop application for comprehensive website auditing and SEO analysis. Whether you're analyzing a small blog or crawling enterprise sites with millions of pages, Shouting Iguana delivers professional-grade insights through an intuitive interface.

## Why Shouting Iguana?

- **Enterprise-Scale Crawling** - Handle sites of any size with efficient SQLite-backed persistence and configurable crawl rules
- **Extensible Plugin System** - Install SEO analysis plugins directly from NuGet or build your own with minimal code
- **Comprehensive Analysis** - Detect broken links, duplicate content, missing meta tags, redirect chains, structured data issues, and more
- **Professional Exports** - Export findings to CSV and Excel for client reports and team collaboration
- **Modern Technology** - Built with .NET 9, WPF, Entity Framework Core, and Playwright for JavaScript rendering

## Getting Started

### Prerequisites

- .NET 9.0 SDK
- Visual Studio 2022 or JetBrains Rider

### Quick Start

1. Build and run the solution

2. Create a new project, configure your crawl settings, and start analyzing!

## Project Structure

The solution is organized into focused projects:

- **ShoutingIguana** - WPF desktop application with modern MaterialDesign UI
- **ShoutingIguana.Core** - Domain models, business logic, and service interfaces
- **ShoutingIguana.Data** - Entity Framework Core data access with SQLite
- **ShoutingIguana.PluginSdk** - SDK for building custom analysis plugins
- **ShoutingIguana.Plugins** - Built-in SEO analysis plugins

## Building Plugins

Shouting Iguana's plugin system lets you extend its capabilities with custom analysis logic. Plugins are distributed via NuGet, making them instantly accessible to all users.

See the [Plugin SDK README](src/ShoutingIguana.PluginSdk/README.md) for a complete guide to building and publishing your own plugins.

## License

This project is MIT [LICENSE](LICENSE) .
