# PlaceiT Connector - Deployment Instructions

## Prerequisites

✅ **Windows Server** with Excel installed and licensed  
✅ **Administrator access** to install Windows Service  
✅ **CDF credentials** (Azure AD application)  
✅ **Network access** to CDF cluster

---

## Step 1: Create `config.yml` from Template

1. Copy `config.example.yml` to `config.yml` in this directory
2. Replace **ALL** placeholder values with actual credentials

### ⚠️ CRITICAL: Scope Configuration Fix

**The most common deployment error** is the connector using default scopes (`https://api.cognitedata.com/.default`) instead of reading from `config.yml`.

**Root Cause**: When using `tenant` field with Azure AD, the framework may initialize the Cognite client before fully loading the config, causing it to fall back to default scopes.

**Solution**: Use `implementation: Basic` with explicit `token-url`. This ensures scopes are read correctly.

**Correct Configuration for West Europe cluster:**

```yaml
cognite:
    project: my-project
    host: https://westeurope-1.cognitedata.com
    idp-authentication:
        implementation: Basic  # ← CRITICAL: This line prevents scope issues
        client-id: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
        secret: your-secret-here
        token-url: https://login.microsoftonline.com/{TENANT-ID}/oauth2/v2.0/token
        scopes:
          - 'https://westeurope-1.cognitedata.com/.default'
```

**Key Points:**
- ✅ **DO** include `implementation: Basic`
- ✅ **DO** ensure `host` and `scopes` use the same cluster URL
- ✅ **DO** include full `token-url` with your tenant ID
- ❌ **DON'T** use `tenant` field (causes scope resolution issues)
- ❌ **DON'T** use `https://api.cognitedata.com/.default` as scope

### 📋 Configuration Checklist

| Field | Example Value | Your Value |
|-------|---------------|------------|
| `project` | `ipso-sandbox` | ✏️ |
| `host` | `https://westeurope-1.cognitedata.com` | ✏️ |
| `implementation` | `Basic` | ✏️ (must be "Basic") |
| `client-id` | `bec7c43d-5816-4467-af1c-fa9c17a22570` | ✏️ |
| `secret` | `L4W8Q~i~CaH9D~gW2h...` | ✏️ |
| `token-url` | `https://login.microsoftonline.com/4ecb897a-.../oauth2/v2.0/token` | ✏️ |
| `scopes` | `https://westeurope-1.cognitedata.com/.default` | ✏️ (must match `host`) |
| `data-set-id` | `3752613513124040` | ✏️ |

### 🚫 Common Mistakes

❌ **WRONG**: Missing `implementation: Basic` (causes default scope to be used)  
✅ **CORRECT**: 
```yaml
idp-authentication:
    implementation: Basic
```

❌ **WRONG**: Using `tenant` field instead of `token-url`  
✅ **CORRECT**: Use `token-url: https://login.microsoftonline.com/{tenant-id}/oauth2/v2.0/token`

❌ **WRONG**: `scopes: - https://api.cognitedata.com/.default`  
✅ **CORRECT**: `scopes: - https://westeurope-1.cognitedata.com/.default`

❌ **WRONG**: `host` field missing  
✅ **CORRECT**: `host: https://westeurope-1.cognitedata.com`

---

## Step 2: Test Interactively First

Before installing as a service, test the connector interactively:

```powershell
# Run in this directory
.\Sample.BasicComConnector.exe
```

**Expected output:**
```
[INFO] Connected to CDF project: your-project
[DBG] Updating connector heartbeat
[DBG] CDF (simulators:updateSimulatorIntegrations): POST ...
```

**If you see errors about scopes or authentication:**
1. Double-check your `config.yml` values
2. Ensure `host` and `scopes` match your CDF cluster
3. Verify `token-url` includes your tenant ID

Press `Ctrl+C` to stop.

---

## Step 3: Install as Windows Service

### A. Configure Excel DCOM (REQUIRED)

⚠️ **Must be done BEFORE installing service**

1. Press `Win+R`, type `dcomcnfg`, press Enter
2. Navigate: **Component Services → Computers → My Computer → DCOM Config**
3. Find **Microsoft Excel Application**, right-click → **Properties**

**Identity Tab:**
- Select: **The interactive user**

**Security Tab - Launch and Activation Permissions:**
- Click **Edit**
- Add **SYSTEM** and **NETWORK SERVICE**
- Grant: **Local Launch** and **Local Activation**

**Security Tab - Access Permissions:**
- Click **Edit**
- Add **SYSTEM** and **NETWORK SERVICE**
- Grant: **Local Access**

Click **OK** to save.

### B. Install Service

Open PowerShell **as Administrator**:

```powershell
# Navigate to deployment directory
cd C:\path\to\deploy

# Create service
sc.exe create PlaceiTConnector `
    binPath= "$PWD\Sample.BasicComConnector.exe --service" `
    start= auto `
    DisplayName= "PlaceiT Simulator Connector"

# Configure auto-restart on failure
sc.exe failure PlaceiTConnector reset= 86400 actions= restart/60000/restart/60000/restart/60000

# Start service
Start-Service -Name PlaceiTConnector
```

### C. Verify Service

```powershell
# Check service status
Get-Service PlaceiTConnector

# View logs in Event Viewer
Get-EventLog -LogName Application -Source "PlaceiT Connector" -Newest 20
```

---

## Step 4: Monitor in Production

### Check Service Status

```powershell
Get-Service PlaceiTConnector
```

### View Logs

```powershell
# Last 50 entries
Get-EventLog -LogName Application -Source "PlaceiT Connector" -Newest 50

# Filter for errors only
Get-EventLog -LogName Application -Source "PlaceiT Connector" -EntryType Error -Newest 20
```

### Verify in CDF

1. Log into CDF
2. Navigate to **Simulators** section
3. Look for connector with name-prefix `scaled-solutions-placeit@`
4. Heartbeat should update every ~10 seconds

---

## Troubleshooting

### Error: "https://api.cognitedata.com/default" or Token:inspect errors

**Cause**: The connector is using default scopes instead of reading from `config.yml`. This happens when the framework initializes the Cognite client before fully loading the configuration.

**Fix**:
1. Open `config.yml`
2. **Add** `implementation: Basic` under `idp-authentication` (this is CRITICAL)
3. **Remove** `tenant` field if present (use `token-url` instead)
4. Ensure `host` and `scopes` match your cluster

```yaml
# CORRECT configuration
cognite:
    project: your-project
    host: https://westeurope-1.cognitedata.com
    idp-authentication:
        implementation: Basic  # ← ADD THIS LINE
        client-id: your-client-id
        secret: your-secret
        token-url: https://login.microsoftonline.com/{tenant-id}/oauth2/v2.0/token
        scopes:
          - 'https://westeurope-1.cognitedata.com/.default'
```

**Why this works**: Using `implementation: Basic` forces the framework to read all config values explicitly, preventing it from falling back to default scopes.

### Error: "Value cannot be null (Parameter 'clientSecret')"

**Cause**: Missing or empty `secret` field

**Fix**: Ensure `secret` is properly set in `config.yml`

### Error: Pop-up dialogs block execution

**Cause**: Excel DCOM not configured OR running interactively instead of as service

**Fix**:
1. Configure Excel DCOM (see Step 3A)
2. Run as Windows Service, not interactively

### Service won't start

**Causes**:
- `config.yml` not in same directory as .exe
- Invalid configuration values
- Excel not installed
- DCOM not configured

**Fix**:
1. Verify `config.yml` exists in deployment directory
2. Test interactively first to validate config
3. Check Event Log for specific errors

---

## Uninstall Service

```powershell
# Stop service
Stop-Service -Name PlaceiTConnector

# Remove service
sc.exe delete PlaceiTConnector
```

---

## Support

For additional help, see:
- `README.md` - Project overview
- `WINDOWS_SERVICE_DEPLOYMENT.md` - Detailed service deployment
- `TROUBLESHOOTING_POPUPS.md` - Dialog suppression issues


