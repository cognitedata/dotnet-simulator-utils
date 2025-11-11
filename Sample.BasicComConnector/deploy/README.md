# PlaceiT Connector - Production Deployment Package

**Version**: 1.0 (Production Ready)  
**Date**: November 11, 2025

---

## 🚀 Quick Start

1. **Read** `DEPLOYMENT_INSTRUCTIONS.md` for complete deployment guide
2. **Copy** `config.example.yml` to `config.yml`
3. **Edit** `config.yml` with your CDF credentials
4. **Test** interactively: `.\Sample.BasicComConnector.exe`
5. **Deploy** as Windows Service (see instructions)

---

## 📦 Package Contents

| File | Purpose |
|------|---------|
| `Sample.BasicComConnector.exe` | Production executable (self-contained, includes .NET 8 runtime) |
| `config.example.yml` | Configuration template with placeholders |
| `DEPLOYMENT_INSTRUCTIONS.md` | Complete deployment guide |
| `README.md` | This file |

---

## ⚠️ CRITICAL: Configuration Fix

**The #1 deployment issue** is the connector using wrong scopes. This has been fixed in the config template.

**MUST INCLUDE** in your `config.yml`:

```yaml
cognite:
    host: https://your-cluster.cognitedata.com
    idp-authentication:
        implementation: Basic  # ← CRITICAL: Prevents scope resolution issues
        token-url: https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token
        scopes:
          - https://your-cluster.cognitedata.com/.default  # ← Must match host
```

**Why?** Using `implementation: Basic` ensures the framework reads scopes from config instead of using defaults.

---

## 📋 System Requirements

- **OS**: Windows 10/11 or Windows Server 2016+
- **Excel**: Microsoft Excel (M365 or standalone) - installed and licensed
- **RAM**: 8-16 GB recommended
- **CPU**: 4-8 vCPUs recommended
- **.NET**: Included in executable (self-contained)

---

## 🔧 Configuration Steps

### 1. Create config.yml

```powershell
cp config.example.yml config.yml
notepad config.yml
```

### 2. Replace ALL placeholders

- `${CDF_PROJECT}` → Your CDF project name
- `${CDF_HOST}` → Your cluster URL (e.g., `https://westeurope-1.cognitedata.com`)
- `${IDP_CLIENT_ID}` → Azure AD App Client ID
- `${IDP_CLIENT_SECRET}` → Azure AD App Client Secret  
- `${IDP_TOKEN_URL}` → Full OAuth2 token URL with your tenant ID
- `${IDP_SCOPE}` → Cluster-specific scope (must match `${CDF_HOST}/.default`)
- `${SIMULATOR_DATASET_ID}` → Dataset ID in CDF

### 3. Test Interactively

```powershell
.\Sample.BasicComConnector.exe
```

**Expected output**:
```
[INFO] Connected to CDF project: your-project
[DBG] Updating connector heartbeat
```

Press `Ctrl+C` to stop.

### 4. Deploy as Service

See `DEPLOYMENT_INSTRUCTIONS.md` for:
- Excel DCOM configuration (required!)
- Service installation commands
- Monitoring and troubleshooting

---

## 🐛 Troubleshooting Quick Reference

### "Token:inspect - GET:https://api.cognitedata.com/default"

**Problem**: Using default scopes instead of your cluster  
**Fix**: Add `implementation: Basic` to config (see DEPLOYMENT_INSTRUCTIONS.md)

### "Value cannot be null (Parameter 'clientSecret')"

**Problem**: Missing or empty secret  
**Fix**: Check `config.yml` has correct `secret` value

### Pop-up dialogs block execution

**Problem**: Excel DCOM not configured OR running interactively  
**Fix**: Configure DCOM and run as Windows Service

---

## 📚 Additional Documentation

In this deployment package:
- **Full Deployment Guide**: `DEPLOYMENT_INSTRUCTIONS.md` (complete step-by-step instructions)

In parent directory (optional reference):
- **Project Overview**: `../README.md`
- **Windows Service Details**: `../WINDOWS_SERVICE_DEPLOYMENT.md`
- **Dialog Suppression**: `../TROUBLESHOOTING_POPUPS.md`

---

## ✅ Production Readiness

This connector has been:
- ✅ Tested with dialog suppression (4-layer protection)
- ✅ Configured for Windows Service deployment
- ✅ Optimized for production use (self-contained, no debug symbols)
- ✅ Documented with complete deployment guides
- ✅ Fixed for scope configuration issues

**Ready for production deployment!** 🚀


