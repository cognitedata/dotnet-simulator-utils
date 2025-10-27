# Testing Windows Service Locally - Step by Step

## Prerequisites

- ✅ You're running locally (already working)
- ✅ Project builds successfully
- ✅ Administrator access to install services

## Step 1: Test Interactive Mode (Already Working)

First, verify everything still works in normal mode:

```powershell
cd Sample.BasicComConnector
dotnet run
```

**Expected**: Connector starts, connects to CDF, processes simulations
**Status**: ✅ Already confirmed working

---

## Step 2: Build for Testing

Build a Release version to test:

```powershell
dotnet build -c Release
cd bin\Release\net8.0
```

Test the Release build in interactive mode:

```powershell
.\Sample.BasicComConnector.exe
```

**Expected**: Same behavior as `dotnet run`
**Verify**: 
- Connects to CDF
- Runs simulations
- Dialog suppression works
- Press `Ctrl+C` to stop

---

## Step 3: Test Service Mode Detection (No Installation)

Test the service mode flag without actually installing as a service:

```powershell
# This will try to run in service mode but will likely fail quickly
# That's OK - we're just testing the detection logic
.\Sample.BasicComConnector.exe --service
```

**Expected**: 
- Different startup behavior (uses HostBuilder)
- May exit quickly (that's fine for this test)
- Proves the `--service` flag detection works

**If it crashes immediately**: That's actually good! It means service mode is detected correctly.

---

## Step 4: Publish for Service Installation

Create a proper service-ready build:

```powershell
cd C:\Users\ChadHutchison\Documents\GitHub\dotnet-simulator-utils\Sample.BasicComConnector

dotnet publish -c Release -r win-x64 --self-contained -o .\test-service
```

This creates a `test-service\` folder with everything needed.

---

## Step 5: Test Published Build in Interactive Mode

Before installing as a service, test the published executable:

```powershell
cd test-service

# Verify files are there
ls

# Test running interactively
.\Sample.BasicComConnector.exe
```

**Expected**: Works exactly like before
**Press `Ctrl+C`** to stop when you see it's working

---

## Step 6: Install as Windows Service (Locally)

**⚠️ IMPORTANT: Excel DCOM Configuration**

If you haven't configured Excel DCOM yet, you may encounter errors. For local testing with your user account:

```powershell
# Run as Administrator in PowerShell
dcomcnfg
```

Navigate to: **DCOM Config → Microsoft Excel Application → Properties**
- **Identity Tab**: Select "The interactive user"
- Click **OK**

This uses your current user account, so Excel should work.

**Now install the service:**

```powershell
# Make sure you're in the test-service directory
# Open PowerShell AS ADMINISTRATOR

cd C:\Users\ChadHutchison\Documents\GitHub\dotnet-simulator-utils\Sample.BasicComConnector\test-service

# Run installation script
.\install-service.ps1
```

**Expected Output**:
```
Installing PlaceiT Connector as Windows Service...
Creating service...
Service installed successfully!
```

---

## Step 7: Verify Service is Installed

```powershell
# Check service exists
Get-Service -Name PlaceiTConnector

# Should show:
# Status: Stopped
# StartType: Automatic
```

---

## Step 8: Start the Service

```powershell
# Start the service
Start-Service -Name PlaceiTConnector

# Wait a few seconds
Start-Sleep -Seconds 5

# Check status
Get-Service -Name PlaceiTConnector
```

**Expected**: Status = "Running"

---

## Step 9: Monitor Service Logs

**In Windows Event Viewer:**

```powershell
# View last 20 log entries
Get-EventLog -LogName Application -Source "PlaceiT Connector" -Newest 20 | Format-Table -AutoSize

# Or just see the messages
Get-EventLog -LogName Application -Source "PlaceiT Connector" -Newest 20 | Select-Object TimeGenerated, EntryType, Message
```

**Look for:**
- ✅ "PlaceiT Connector Service starting"
- ✅ "Starting the connector..."
- ✅ "Connector can reach CDF!"
- ✅ "Started executing simulation run"

**Watch for errors:**
- ❌ COM exceptions (means DCOM not configured)
- ❌ File access errors (means permissions issue)

---

## Step 10: Test Simulations

Trigger a simulation from CDF and watch it process:

```powershell
# Watch logs in real-time
Get-EventLog -LogName Application -Source "PlaceiT Connector" -Newest 5

# Refresh every 2 seconds (Ctrl+C to stop)
while ($true) { 
    Clear-Host
    Get-EventLog -LogName Application -Source "PlaceiT Connector" -Newest 10 | Format-Table TimeGenerated, EntryType, Message -Wrap
    Start-Sleep -Seconds 2 
}
```

**Look for:**
- ✅ Simulation runs being picked up
- ✅ Dialog auto-dismissal logs (if errors occur)
- ✅ Successful completion or proper error handling
- ✅ Next simulation proceeds

---

## Step 11: Test Dialog Suppression

Trigger a simulation with parameters that cause an error (e.g., small timestep causing gridding error):

**In Service Mode, you should see:**
```
WARNING: Auto-dismissing dialog: 'PlaceiT v7.3: Gridding error'
ERROR: Error calling PlaceiTCOMEntryPoint function
INFO: Simulation run XXXXX failed with error
INFO: Started executing simulation run XXXXX (next run proceeds)
```

**✅ Success criteria:**
- No service hanging
- Error logged properly
- Next simulation runs immediately

---

## Step 12: Stop the Service

```powershell
# Stop service
Stop-Service -Name PlaceiTConnector -Force

# Verify stopped
Get-Service -Name PlaceiTConnector
```

---

## Step 13: Uninstall Test Service

When done testing:

```powershell
# Run as Administrator
cd C:\Users\ChadHutchison\Documents\GitHub\dotnet-simulator-utils\Sample.BasicComConnector\test-service

.\uninstall-service.ps1

# Verify removed
Get-Service -Name PlaceiTConnector
# Should error: "Cannot find any service with service name 'PlaceiTConnector'"
```

---

## Step 14: Cleanup Test Files (Optional)

```powershell
cd ..
Remove-Item -Recurse -Force .\test-service
```

---

## Common Issues & Solutions

### Issue: Service Won't Start

**Error**: Service starts then stops immediately

**Diagnose**:
```powershell
Get-EventLog -LogName Application -Source "PlaceiT Connector" -Newest 5
```

**Common causes**:
1. **DCOM not configured**
   - Error: `0x80080005 Server execution failed`
   - Fix: Configure Excel DCOM (see WINDOWS_SERVICE_DEPLOYMENT.md)

2. **config.yml not found**
   - Error: `Failed to load configuration file`
   - Fix: Ensure config.yml is in same directory as .exe

3. **Permission denied**
   - Error: Access to path is denied
   - Fix: Run service as your user account or grant SYSTEM access

### Issue: No Logs Appearing

**Check Event Viewer manually**:
1. Press `Win + X` → Event Viewer
2. Windows Logs → Application
3. Filter by Source = "PlaceiT Connector"

If still no logs, service may not be starting at all.

### Issue: Simulations Not Running

**Verify heartbeat in CDF**:
- Check connector status page in CDF
- Should see heartbeat updating every ~10 seconds

**Check service is actually running**:
```powershell
Get-Service -Name PlaceiTConnector | Select-Object Status, StartType
```

### Issue: Dialog Still Appears

**In service mode, dialogs should NOT appear** (Session 0 isolation)

If you see a dialog:
- You might be running in interactive mode, not service mode
- Verify: `Get-Service -Name PlaceiTConnector` shows "Running"
- The dialog suppressor should log: "Auto-dismissing dialog: '...'"

---

## Quick Test Script

Save this as `test-service-quick.ps1`:

```powershell
# Quick service test script
# Run as Administrator

Write-Host "=== PlaceiT Service Quick Test ===" -ForegroundColor Cyan
Write-Host ""

# Check service status
Write-Host "1. Service Status:" -ForegroundColor Yellow
Get-Service -Name PlaceiTConnector -ErrorAction SilentlyContinue | Select-Object Status, StartType

# Recent logs
Write-Host "`n2. Recent Logs (last 10):" -ForegroundColor Yellow
Get-EventLog -LogName Application -Source "PlaceiT Connector" -Newest 10 -ErrorAction SilentlyContinue | 
    Select-Object TimeGenerated, EntryType, @{Name="Message";Expression={$_.Message.Substring(0,[Math]::Min(100,$_.Message.Length))}}

# Recent errors
Write-Host "`n3. Recent Errors:" -ForegroundColor Yellow
Get-EventLog -LogName Application -Source "PlaceiT Connector" -EntryType Error -Newest 5 -ErrorAction SilentlyContinue | 
    Select-Object TimeGenerated, Message

Write-Host "`n=== End Test ===" -ForegroundColor Cyan
```

Run it anytime:
```powershell
.\test-service-quick.ps1
```

---

## Success Checklist

After testing, verify:

- [ ] Service installs successfully
- [ ] Service starts and runs
- [ ] Logs appear in Event Viewer
- [ ] Connector connects to CDF
- [ ] Simulations run successfully
- [ ] Errors are handled gracefully
- [ ] Dialog suppression works (no hanging)
- [ ] Service stops cleanly
- [ ] Service can be uninstalled

---

## Next Steps

**If local testing succeeds**:
1. ✅ Document any DCOM configuration needed
2. ✅ Prepare deployment package
3. ✅ Test on a dedicated server (not your dev machine)
4. ✅ Set up monitoring/alerts
5. ✅ Deploy to production

**If issues found**:
1. Check Event Viewer for specific errors
2. Review DCOM configuration
3. Test in interactive mode first
4. Check permissions and file locations

---

## Rollback Plan

If service causes issues:

```powershell
# Stop and remove service
Stop-Service -Name PlaceiTConnector -Force
.\uninstall-service.ps1

# Go back to interactive mode
cd ..\bin\Release\net8.0
.\Sample.BasicComConnector.exe
```

Your interactive mode still works - nothing is broken!

---

**Ready to test! Start with Step 1 and work through each step.** 🚀

