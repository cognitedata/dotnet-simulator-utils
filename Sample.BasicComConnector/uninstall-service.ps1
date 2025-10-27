# Uninstall PlaceiT Connector Windows Service
# Must be run as Administrator

param(
    [string]$ServiceName = "PlaceiTConnector"
)

# Check if running as administrator
if (-NOT ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator"))
{
    Write-Error "This script must be run as Administrator. Right-click PowerShell and select 'Run as Administrator'"
    exit 1
}

Write-Host "Uninstalling PlaceiT Connector Windows Service..." -ForegroundColor Cyan

# Check if service exists
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $existingService) {
    Write-Warning "Service '$ServiceName' does not exist"
    exit 0
}

# Stop the service
Write-Host "Stopping service..." -ForegroundColor Yellow
Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# Delete the service
Write-Host "Removing service..." -ForegroundColor Yellow
& sc.exe delete $ServiceName

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nService uninstalled successfully!" -ForegroundColor Green
} else {
    Write-Error "Failed to uninstall service"
    exit 1
}

