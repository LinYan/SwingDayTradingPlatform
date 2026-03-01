param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot ".dotnet-home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"

$projects = @(
    "SwingDayTradingPlatform.Shared/SwingDayTradingPlatform.Shared.csproj",
    "SwingDayTradingPlatform.Storage/SwingDayTradingPlatform.Storage.csproj",
    "SwingDayTradingPlatform.Risk/SwingDayTradingPlatform.Risk.csproj",
    "SwingDayTradingPlatform.Strategy/SwingDayTradingPlatform.Strategy.csproj",
    "SwingDayTradingPlatform.Execution.Ibkr/SwingDayTradingPlatform.Execution.Ibkr.csproj",
    "SwingDayTradingPlatform.UI.Wpf/SwingDayTradingPlatform.UI.Wpf.csproj"
)

foreach ($project in $projects) {
    dotnet build $project -c $Configuration --no-restore -v minimal /p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed for $project"
    }
}

Write-Host "Swing Day Trading Platform build completed."
