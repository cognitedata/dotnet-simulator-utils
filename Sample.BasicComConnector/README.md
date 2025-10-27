# PlaceiT Simulator Connector

Production-ready connector for Cognite Data Fusion (CDF) that runs PlaceiT inhibitor squeeze treatment simulations via Excel COM automation.

## Features

✅ **Excel COM Automation** - Calls PlaceiT VBA macros for simulations  
✅ **Dialog Suppression** - 4-layer protection prevents pop-ups from blocking execution  
✅ **Windows Service** - Runs as background service or interactive application  
✅ **Error Handling** - Comprehensive logging with graceful failure  
✅ **Auto-Recovery** - Service auto-restarts on failure  
✅ **Production Ready** - Tested and documented  

## System Requirements

- **OS**: Windows 10/11 or Windows Server 2016+
- **Excel**: Microsoft Excel (installed and licensed)
- **.NET**: 8.0 (included in self-contained build)
- **Access**: Administrator rights for service installation

## Quick Start (Local Development)

### 1. Configure Environment

Copy `config.example.yml` to `config.yml` and fill in your CDF credentials:

```yaml
cognite:
    project: your-project
    host: https://your-cluster.cognitedata.com
    idp-authentication:
        tenant: your-tenant-id
        client-id: your-client-id
        secret: your-secret
        scopes:
          - https://your-cluster.cognitedata.com/.default
```

### 2. Run Locally

```powershell
dotnet run
```

The connector will:
- Connect to CDF
- Report heartbeat every 10 seconds
- Process simulation runs automatically
- Log all activity to console

Press `Ctrl+C` to stop.

## Production Deployment

### Build for Production

```powershell
dotnet publish -c Release -r win-x64 --self-contained -o deploy
```

This creates a `deploy/` folder with:
- `Sample.BasicComConnector.exe` (70-100 MB, includes .NET 8 runtime)
- `config.example.yml` (template)
- All dependencies

### Deploy as Windows Service

See **[WINDOWS_SERVICE_DEPLOYMENT.md](WINDOWS_SERVICE_DEPLOYMENT.md)** for complete deployment guide.

**Quick install:**

```powershell
# On target server (as Administrator)
cd deploy

# Configure config.yml with production credentials

# CRITICAL: Configure Excel DCOM first!
# See WINDOWS_SERVICE_DEPLOYMENT.md section "Excel DCOM Configuration"

# Install service
sc.exe create PlaceiTConnector binPath= "$PWD\Sample.BasicComConnector.exe --service" start= auto DisplayName= "PlaceiT Simulator Connector"

# Start service
Start-Service -Name PlaceiTConnector

# Monitor
Get-EventLog -LogName Application -Source "PlaceiT Connector" -Newest 20
```

## Documentation

| Document | Purpose |
|----------|---------|
| **[WINDOWS_SERVICE_DEPLOYMENT.md](WINDOWS_SERVICE_DEPLOYMENT.md)** | Complete production deployment guide |
| **[SERVICE_DEPLOYMENT_SUMMARY.md](SERVICE_DEPLOYMENT_SUMMARY.md)** | Quick reference for deployment |
| **[TEST_SERVICE_LOCALLY.md](TEST_SERVICE_LOCALLY.md)** | Step-by-step local testing guide |
| **[DIALOG_SUPPRESSION_SOLUTION.md](DIALOG_SUPPRESSION_SOLUTION.md)** | Technical details on pop-up handling |
| **[TROUBLESHOOTING_POPUPS.md](TROUBLESHOOTING_POPUPS.md)** | Diagnostic guide for dialog issues |
| **[PRODUCTION_READINESS_ASSESSMENT.md](PRODUCTION_READINESS_ASSESSMENT.md)** | Full production readiness review |

## Architecture

### Components

```
PlaceiT Connector
├── Program.cs                  - Entry point with dual-mode support
├── ConnectorRuntime.cs         - Connector initialization
├── ConnectorServiceHost.cs     - Windows Service host
├── NewSimClient.cs             - Excel COM automation client
├── NewSimRoutine.cs            - Simulation execution logic
├── DialogSuppressor.cs         - Windows API dialog auto-dismissal
└── SimulatorDefinition.cs      - Connector metadata
```

### Data Flow

```
CDF → Connector → Excel COM → PlaceiT VBA → Results → CDF
          ↓
    Dialog Suppressor (monitors for pop-ups)
          ↓
    Auto-dismiss & log errors
```

### Dialog Suppression (4 Layers)

1. **Session 0 Isolation** - Windows Service runs in non-interactive mode
2. **Excel COM Settings** - `DisplayAlerts = false` at multiple levels
3. **DialogSuppressor** - Windows API monitoring and auto-dismissal
4. **Error Handling** - Captures error codes and logs details

## Configuration

### Key Settings

```yaml
connector:
  name-prefix: "your-connector-name@"
  data-set-id: 1234567890
  simulation-run-tolerance: 7200  # 2 hours (prevents timeout errors)
```

**simulation-run-tolerance**: Maximum age (seconds) for simulation runs before timing out. Increase if simulations queue up.

## Monitoring

### Interactive Mode (Console)

All logs appear in the console with color coding:
- **Debug** (Gray): Detailed operational info
- **Info** (White): Normal operations
- **Warning** (Yellow): Handled issues
- **Error** (Red): Failures

### Service Mode (Event Log)

```powershell
# View recent logs
Get-EventLog -LogName Application -Source "PlaceiT Connector" -Newest 20

# Watch live
while ($true) { 
    Clear-Host
    Get-EventLog -LogName Application -Source "PlaceiT Connector" -Newest 10 | Format-Table TimeGenerated, EntryType, Message -Wrap
    Start-Sleep -Seconds 3 
}

# View errors only
Get-EventLog -LogName Application -Source "PlaceiT Connector" -EntryType Error -Newest 10
```

### CDF Monitoring

- **Heartbeat**: Check connector status page (updates every ~10 seconds)
- **Simulations**: Monitor run status and completion
- **Logs**: Review connector logs in CDF

## Troubleshooting

### Common Issues

**Service won't start**
```
Error: 0x80080005 Server execution failed
Solution: Configure Excel DCOM (see WINDOWS_SERVICE_DEPLOYMENT.md)
```

**Simulations hanging**
```
Issue: Pop-ups blocking execution
Solution: Already handled by DialogSuppressor (should not occur)
Check logs for: "Auto-dismissing dialog: '...'"
```

**Simulation timeout errors**
```
Error: "Simulation has timed out because it is older than 3600 second(s)"
Solution: Increase simulation-run-tolerance in config.yml
```

**COM cleanup warnings**
```
Warning: Failed to cleanup extracted directory after 3 attempts
Impact: Minimal - Windows will clean temp files later
Solution: Already optimized with 5 retries and progressive delays
```

### Getting Help

1. Check relevant documentation (see table above)
2. Review Event Log for errors
3. Test in interactive mode to isolate issues
4. Verify Excel DCOM configuration

## Development

### Prerequisites

- .NET 8.0 SDK
- Visual Studio 2022 or VS Code
- Excel (for testing)

### Build

```powershell
dotnet build
```

### Run Tests

```powershell
# Run connector locally
dotnet run

# Test with specific config
dotnet run -- --config path/to/config.yml

# Test service mode detection
dotnet run -- --service
```

### Project Structure

```
Sample.BasicComConnector/
├── *.cs                        - Source code
├── config.example.yml          - Configuration template
├── *.ps1                       - PowerShell scripts
├── *.md                        - Documentation
└── files/                      - Test PlaceiT packages (optional)
```

## Security

- ✅ No credentials in source code
- ✅ `config.yml` in .gitignore
- ✅ Environment variable support
- ✅ Secrets should be rotated regularly

**Never commit `config.yml` with real credentials!**

## License

See [LICENSE](../LICENSE) file in repository root.

## Support

For issues specific to:
- **PlaceiT functionality**: Contact PlaceiT vendor
- **CDF integration**: Contact Cognite support
- **Connector issues**: Review documentation and logs

---

**Version**: 1.0  
**Status**: Production Ready ✅  
**Last Updated**: October 24, 2025

