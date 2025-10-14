# Run the Basic COM Connector with environment variables from .env file

Write-Host "=== Cognite Basic COM Connector ===" -ForegroundColor Cyan
Write-Host ""

# Load environment variables from .env file
& "$PSScriptRoot\load-env.ps1"

Write-Host ""
Write-Host "Starting the connector..." -ForegroundColor Green
Write-Host ""

# Run the dotnet application
try {
    dotnet run
} catch {
    Write-Host "Error running the connector: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Connector stopped." -ForegroundColor Yellow
