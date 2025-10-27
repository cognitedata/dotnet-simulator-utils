# Install PlaceiT Connector as Windows Service
# Must be run as Administrator

param(
    [string]$ServiceName = "PlaceiTConnector",
    [string]$DisplayName = "PlaceiT Simulator Connector",
    [string]$Description = "Cognite PlaceiT Simulator Connector for inhibitor squeeze treatment calculations",
    [string]$ExecutablePath = "$PSScriptRoot\Sample.BasicComConnector.exe"
)

# Check if running as administrator
if (-NOT ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator"))
{
    Write-Error "This script must be run as Administrator. Right-click PowerShell and select 'Run as Administrator'"
    exit 1
}

Write-Host "Installing PlaceiT Connector as Windows Service..." -ForegroundColor Cyan

# Check if executable exists
if (-not (Test-Path $ExecutablePath)) {
    Write-Error "Executable not found at: $ExecutablePath"
    Write-Host "Please build the project first using:" -ForegroundColor Yellow
    Write-Host "  dotnet publish -c Release -r win-x64 --self-contained -o ." -ForegroundColor Yellow
    exit 1
}

# Check if service already exists
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "Service already exists. Stopping and removing..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    & sc.exe delete $ServiceName
    Start-Sleep -Seconds 2
}

# Create the service
Write-Host "Creating service..." -ForegroundColor Green
& sc.exe create $ServiceName binPath= "$ExecutablePath --service" start= auto DisplayName= "$DisplayName"

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to create service"
    exit 1
}

# Set service description
& sc.exe description $ServiceName "$Description"

# Configure service recovery options (restart on failure)
& sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000

Write-Host "`nService installed successfully!" -ForegroundColor Green
Write-Host "`nIMPORTANT: Before starting the service, you MUST:" -ForegroundColor Yellow -BackgroundColor Red
Write-Host "1. Configure DCOM permissions for Excel (see WINDOWS_SERVICE_DEPLOYMENT.md)" -ForegroundColor Yellow
Write-Host "2. Ensure config.yml is in the same directory as the executable" -ForegroundColor Yellow
Write-Host "3. Grant the service account access to Excel and temp directories" -ForegroundColor Yellow

Write-Host "`nTo start the service:" -ForegroundColor Cyan
Write-Host "  Start-Service -Name $ServiceName" -ForegroundColor White
Write-Host "`nTo check service status:" -ForegroundColor Cyan
Write-Host "  Get-Service -Name $ServiceName" -ForegroundColor White
Write-Host "`nTo view service logs:" -ForegroundColor Cyan
Write-Host "  Get-EventLog -LogName Application -Source 'PlaceiT Connector' -Newest 50" -ForegroundColor White

