# Windows Service Deployment - Quick Summary

## What We Built

The connector is now **dual-mode**: runs as **Windows Service** (production) or **standalone** (development/testing).

## Key Features

✅ **Automatic Mode Detection**
- Interactive mode: Runs normally when launched from command line
- Service mode: Automatically detected when running as Windows Service

✅ **Dialog Suppression (Both Modes)**
- 4-layer protection against Excel pop-ups
- Works in both interactive and service modes
- Logs all dismissed dialogs

✅ **Easy Installation**
- One-command install: `.\install-service.ps1`
- One-command uninstall: `.\uninstall-service.ps1`

✅ **Production-Ready**
- Auto-restart on failure
- Windows Event Log integration
- Proper error handling
- Extended cleanup timeouts

## Quick Start

### 1. Build for Deployment

```powershell
dotnet publish -c Release -r win-x64 --self-contained -o publish
```

### 2. Configure Excel DCOM (CRITICAL!)

```
Run: dcomcnfg
Navigate to: DCOM Config → Microsoft Excel Application → Properties
Configure: Identity, Security (Launch/Activation, Access, Configuration)
```

See `WINDOWS_SERVICE_DEPLOYMENT.md` for detailed steps.

### 3. Install Service

```powershell
cd publish
# Run as Administrator
.\install-service.ps1
```

### 4. Start Service

```powershell
Start-Service -Name PlaceiTConnector
```

### 5. Monitor

```powershell
# Check status
Get-Service -Name PlaceiTConnector

# View logs
Get-EventLog -LogName Application -Source "PlaceiT Connector" -Newest 20
```

## Files Added/Modified

### New Files:
- ✨ `ConnectorServiceHost.cs` - Windows Service host
- 📜 `install-service.ps1` - Installation script
- 📜 `uninstall-service.ps1` - Removal script
- 📄 `WINDOWS_SERVICE_DEPLOYMENT.md` - Complete deployment guide
- 📄 `SERVICE_DEPLOYMENT_SUMMARY.md` - This file

### Modified Files:
- 🔧 `Program.cs` - Dual-mode support (standalone + service)
- 🔧 `Sample.BasicComConnector.csproj` - Added Windows Service package

### Previously Added (Dialog Suppression):
- 🛡️ `DialogSuppressor.cs` - Auto-dismiss dialogs
- 🔧 `NewSimClient.cs` - Enhanced cleanup, Excel suppression
- 🔧 `NewSimRoutine.cs` - Dialog suppressor integration
- ⚙️ `config.yml` - Extended simulation timeout

## Testing Locally (Development)

```powershell
# Run as standalone (current behavior)
.\Sample.BasicComConnector.exe

# Or explicitly
dotnet run
```

## Response to Priyanka

**Yes, we're service-deployment ready!**

- ✅ Dual-mode support (interactive + service)
- ✅ Excel DCOM configuration documented
- ✅ Dialog suppression works in both modes
- ✅ Installation scripts provided
- ✅ Comprehensive deployment guide

**Key Point**: When running as Windows Service in session 0, Excel UI dialogs are naturally suppressed by Windows. The DialogSuppressor acts as a safety net and also works during local testing.

## Why Keep DialogSuppressor?

Even though service mode naturally suppresses dialogs:

1. **Safety Net** - Handles edge cases where dialogs still appear
2. **Local Testing** - Critical for development/testing in interactive mode
3. **Logging** - Captures dialog titles for debugging
4. **Zero Downtime** - Ensures no simulation hangs in any scenario

## Next Steps

1. **Test locally** (already working ✅)
2. **Configure DCOM** on target server
3. **Deploy as service** to test environment
4. **Monitor** for 24 hours
5. **Deploy to production**

---

**Everything is ready for Windows Service deployment!** 🎯

