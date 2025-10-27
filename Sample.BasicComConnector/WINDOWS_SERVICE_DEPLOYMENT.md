# Windows Service Deployment Guide

## Overview

The PlaceiT Simulator Connector can run as a Windows Service for production deployment. When running as a service:
- ✅ **Runs automatically on system startup**
- ✅ **Runs in unattended/session 0 mode** (naturally suppresses UI dialogs)
- ✅ **Managed by Windows Service Control Manager**
- ✅ **Logs to Windows Event Log**
- ✅ **Can run without user logged in**

## Prerequisites

### 1. Excel Installation
- Microsoft Excel **must be installed** on the server
- Excel must be properly licensed
- **Important**: Excel must be configured for COM automation in service mode (see DCOM Configuration below)

### 2. Build Requirements
- .NET 8.0 Runtime (included in self-contained build)
- Windows Server 2016+ or Windows 10+

### 3. Administrator Access
- Service installation requires Administrator privileges
- DCOM configuration requires Administrator privileges

## Build for Service Deployment

```powershell
cd Sample.BasicComConnector
dotnet publish -c Release -r win-x64 --self-contained -o publish
```

This creates a `publish/` folder with:
- `Sample.BasicComConnector.exe`
- `config.yml`
- `config.example.yml`

## Critical: Excel DCOM Configuration

**THIS IS MANDATORY for Excel COM automation in Windows Service mode!**

Without this configuration, Excel will fail to launch from a service, and you'll see errors like:
- `COMException: 0x80080005 Server execution failed`
- `Cannot create ActiveX component`

### Step 1: Open Component Services

1. Press `Win + R`
2. Type `dcomcnfg` and press Enter
3. Navigate to: **Component Services → Computers → My Computer → DCOM Config**

### Step 2: Configure Microsoft Excel Application

1. Find **Microsoft Excel Application** in the list
2. Right-click → **Properties**

#### Identity Tab
- Select **"The interactive user"** or **"This user"** (use a dedicated service account)
- If using "This user", enter credentials of an account with:
  - Excel installed and licensed
  - Access to temp directories
  - Access to CDF (network permissions)

#### Security Tab - Launch and Activation Permissions
1. Click **"Edit"** under "Launch and Activation Permissions"
2. Add the service account (e.g., `LOCAL SYSTEM`, `NETWORK SERVICE`, or your custom account)
3. Grant these permissions:
   - ☑ Local Launch
   - ☑ Local Activation

#### Security Tab - Access Permissions
1. Click **"Edit"** under "Access Permissions"
2. Add the same service account
3. Grant:
   - ☑ Local Access

#### Security Tab - Configuration Permissions
1. Click **"Edit"** under "Configuration Permissions"
2. Add the same service account
3. Grant:
   - ☑ Full Control

### Step 3: Configure Default DCOM Permissions (Optional but Recommended)

1. In `dcomcnfg`, right-click **"My Computer"** → **Properties**
2. Go to **"Default Properties"** tab
3. Set **"Default Authentication Level"** to **"Connect"**
4. Set **"Default Impersonation Level"** to **"Identify"**

## Installation

### Automated Installation (Recommended)

```powershell
# Run as Administrator
cd publish
.\install-service.ps1
```

This script:
- Creates the Windows Service
- Configures auto-start
- Sets recovery options (restart on failure)
- Displays next steps

### Manual Installation

```powershell
# Run as Administrator
sc.exe create PlaceiTConnector binPath= "C:\Path\To\Sample.BasicComConnector.exe --service" start= auto DisplayName= "PlaceiT Simulator Connector"

sc.exe description PlaceiTConnector "Cognite PlaceiT Simulator Connector"

# Configure recovery (restart on failure)
sc.exe failure PlaceiTConnector reset= 86400 actions= restart/60000/restart/60000/restart/60000
```

## Configuration

### config.yml Location

The `config.yml` **must be in the same directory** as the executable.

```
C:\Path\To\Service\
├── Sample.BasicComConnector.exe
├── config.yml  ← Must be here!
└── config.example.yml
```

### Service Account Permissions

The service account needs:
- ✅ **Read** access to executable directory
- ✅ **Read/Write** access to `%TEMP%` directory (for PlaceiT extraction)
- ✅ **Execute** permission for Excel
- ✅ **Network** access to CDF API endpoints

## Service Management

### Start the Service

```powershell
Start-Service -Name PlaceiTConnector
```

### Stop the Service

```powershell
Stop-Service -Name PlaceiTConnector
```

### Check Status

```powershell
Get-Service -Name PlaceiTConnector
```

### View Logs

```powershell
# Recent logs (last 50 entries)
Get-EventLog -LogName Application -Source "PlaceiT Connector" -Newest 50

# Filter by error
Get-EventLog -LogName Application -Source "PlaceiT Connector" -EntryType Error -Newest 20

# Filter by date
Get-EventLog -LogName Application -Source "PlaceiT Connector" -After (Get-Date).AddDays(-1)
```

## Uninstallation

### Automated

```powershell
# Run as Administrator
.\uninstall-service.ps1
```

### Manual

```powershell
# Stop and remove service
Stop-Service -Name PlaceiTConnector -Force
sc.exe delete PlaceiTConnector
```

## Testing

### 1. Test Interactive Mode First

Before deploying as a service, test in interactive mode:

```powershell
.\Sample.BasicComConnector.exe
```

This should run successfully and connect to CDF.

### 2. Test Service Mode

After installation:

```powershell
Start-Service -Name PlaceiTConnector
Start-Sleep -Seconds 10
Get-EventLog -LogName Application -Source "PlaceiT Connector" -Newest 10
```

Look for:
- ✅ "PlaceiT Connector Service starting"
- ✅ "Starting the connector..."
- ✅ "Connector can reach CDF!"
- ✅ Successful simulation runs

### 3. Test Dialog Suppression

Trigger a simulation that causes an error (e.g., invalid timestep). Check logs for:
- ⚠️ "Auto-dismissing dialog: 'PlaceiT v7.3: Gridding error'"
- ✅ "Simulation run XXXXX failed with error"
- ✅ Next simulation proceeds normally

## Dialog Suppression in Service Mode

### How It Works

The connector has **4 layers of dialog suppression**:

1. **Session 0 Isolation** (Windows Service natural behavior)
   - Services run in Session 0 (non-interactive)
   - UI dialogs cannot be displayed to users
   - Excel attempts to show dialogs are suppressed by Windows

2. **Excel COM DisplayAlerts = false**
   - Suppresses Excel's built-in alerts
   - Set at server, workbook, and pre-call levels

3. **DialogSuppressor (Safety Net)**
   - Windows API monitoring for stray dialogs
   - Auto-dismisses any dialogs that appear
   - Logs dialog titles for debugging

4. **Error Handling**
   - Captures error codes from PlaceiT
   - Logs full context and parameters
   - Fails runs gracefully

### Expected Behavior

**Service mode** (Session 0):
- Pop-ups are automatically suppressed by Windows
- DialogSuppressor acts as backup
- Runs fail immediately with error logs

**Interactive mode** (for testing):
- DialogSuppressor actively dismisses pop-ups
- Same error logging behavior

## Troubleshooting

### Service Won't Start

**Error**: Service fails to start immediately

**Solutions**:
1. Check DCOM configuration (most common issue)
2. Verify Excel is installed and licensed
3. Check service account has proper permissions
4. Review Event Log for specific errors

```powershell
Get-EventLog -LogName Application -Source "PlaceiT Connector" -EntryType Error -Newest 5
```

### Excel COM Errors

**Error**: `0x80080005 Server execution failed` or `Cannot create ActiveX component`

**Solution**: Configure DCOM permissions (see above)

### Permission Errors

**Error**: Access denied to temp directories or config files

**Solutions**:
1. Grant service account access to `C:\Windows\Temp`
2. Grant read access to config.yml directory
3. Run service as a user account instead of SYSTEM

### Simulation Hanging

**Error**: Simulations don't complete, service becomes unresponsive

**Solutions**:
1. Check if pop-ups are appearing (shouldn't in service mode)
2. Review logs for "Auto-dismissing dialog" entries
3. Verify simulation timeout configuration:
   ```yaml
   connector:
     simulation-run-tolerance: 7200  # 2 hours
   ```

### Log Files Not Appearing

**Error**: No logs in Event Viewer

**Solutions**:
1. Check "Application" log, not "System"
2. Filter by Source = "PlaceiT Connector"
3. Verify service actually started: `Get-Service PlaceiTConnector`

## Production Checklist

Before deploying to production:

- [ ] Excel installed and licensed
- [ ] DCOM configured for Excel Application
- [ ] Service account permissions configured
- [ ] config.yml updated with production credentials
- [ ] Tested in interactive mode successfully
- [ ] Tested as service on test server
- [ ] Verified dialog suppression works
- [ ] Verified logs appear in Event Viewer
- [ ] Verified service auto-restarts on failure
- [ ] Documented recovery procedures
- [ ] Set up monitoring/alerts for service status

## Monitoring

### Key Metrics to Monitor

1. **Service Status**
   ```powershell
   Get-Service -Name PlaceiTConnector | Select-Object Status, StartType
   ```

2. **Recent Errors**
   ```powershell
   Get-EventLog -LogName Application -Source "PlaceiT Connector" -EntryType Error -Newest 10
   ```

3. **Connector Heartbeat** (in CDF)
   - Check last heartbeat timestamp
   - Should update every ~10 seconds

4. **Simulation Success Rate**
   - Monitor failed runs in CDF
   - Look for patterns in error logs

## Support

For issues:
1. Check Event Log first
2. Review DCOM configuration
3. Test in interactive mode to isolate service-specific issues
4. Collect logs from last failure
5. Check CDF connector status page

## Advanced Configuration

### Custom Service Account

```powershell
# Create service with custom account
sc.exe create PlaceiTConnector binPath= "C:\Path\To\Sample.BasicComConnector.exe --service" start= auto obj= "DOMAIN\ServiceAccount" password= "password"
```

### Multiple Instances

To run multiple connector instances:
1. Copy to different directories
2. Use different service names
3. Use different config.yml files
4. Ensure different `connector.name-prefix` in each config

---

**Ready for Production Deployment!** 🚀

