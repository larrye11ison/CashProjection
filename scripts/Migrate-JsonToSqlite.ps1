# Run once, manually, when ready to cut over from the legacy JSON file to
# the new SQLite database. Not invoked by the app. Safe to delete after use.
#
# Thin wrapper around the Migrate-JsonToSqlite console project in this same
# folder — see Migrate-JsonToSqlite/Program.cs for what it actually does.
#
# Usage:
#   .\Migrate-JsonToSqlite.ps1
#   .\Migrate-JsonToSqlite.ps1 -JsonPath "C:\path\to\CashProjection.json" -DbPath "C:\path\to\CashProjection.sqlite"

param(
    [string]$JsonPath = (Join-Path $env:OneDrive "CashProjection.json"),
    [string]$DbPath = (Join-Path $env:OneDrive "CashProjection.sqlite")
)

if (Get-Process -Name CashProjection -ErrorAction SilentlyContinue) {
    Write-Error "CashProjection is currently running. Close it first to avoid a concurrent write conflict, then re-run this script."
    exit 1
}

$projectDir = Join-Path $PSScriptRoot "Migrate-JsonToSqlite"
dotnet run --project $projectDir -- $JsonPath $DbPath
