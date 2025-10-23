# ShoutingIguana Migrations Script
# Creates EF Core migrations for SQLite database

param(
    [string]$ProjectPath = "src/ShoutingIguana.Data",
    [string]$StartupProject = "src/ShoutingIguana",
    [string]$MigrationName
)

Write-Host "Checking dotnet-ef tool..."
dotnet tool update --global dotnet-ef | Out-Null

# Prompt for migration name if not provided
if (-not $MigrationName) {
    $MigrationName = Read-Host "Enter migration name (e.g., AddUsersTable)"
}

if ([string]::IsNullOrWhiteSpace($MigrationName)) {
    Write-Error "Migration name cannot be empty."
    exit 1
}

try {
    Write-Host "Creating SQLite migration '$MigrationName'..."
    dotnet ef migrations add $MigrationName `
        --context "SqliteShoutingIguanaDbContext" `
        -o "Migrations" `
        --project $ProjectPath `
        --startup-project $StartupProject

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create migration."
    }

    Write-Host "`nMigration '$MigrationName' created successfully!" -ForegroundColor Green
    Write-Host "Migration files created in: $ProjectPath/Migrations" -ForegroundColor Cyan
}
catch {
    Write-Error $_
    exit 1
}

