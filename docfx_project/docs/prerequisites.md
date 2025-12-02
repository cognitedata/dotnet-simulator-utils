# Prerequisites & Setup

This guide walks you through setting up your development environment for building simulator connectors with the Cognite Simulator Integration SDK.

## System Requirements

### Operating System

For this **tutorial**, you need:
- **Windows 10 or later**
- **Microsoft Excel**

**Note:** The SDK itself supports cross-platform deployment for non-COM integration types. See [Understanding Simulator Integration](understanding-integration.md#platform-considerations) for platform and architecture details.

## Development Tools

### .NET SDK

**Required:** .NET 8.0 (LTS) or later

**Check if you have .NET installed:**

```bash
dotnet --version
```

**Install .NET:**

1. Download from [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download)
2. Choose ".NET SDK" (not just Runtime)
3. Select the latest LTS version (Long Term Support)
4. Run the installer

## Cognite Data Fusion (CDF) Setup

You need access to a CDF project to deploy and test your connector.

### CDF Project Access

**If you already have a CDF project:**
- You're ready to proceed
- You'll need admin or contributor access to create simulator resources

**If you don't have a CDF project:**
- Request access from your organization's CDF administrator
- Or sign up for a trial at [cognite.com](https://www.cognite.com/)

### Service Account / Application Credentials

Your connector needs credentials to authenticate with CDF.

**You'll need:**
1. **Client ID** - Application/Service Principal ID
2. **Client Secret** - Application secret/key
3. **Tenant ID** - Azure AD tenant ID (for Microsoft Entra ID)
4. **CDF Project Name** - Your CDF project identifier
5. **CDF Host** - Typically `https://api.cognitedata.com` (or your cluster URL)
6. **Scopes** - Typically `https://[cluster].cognitedata.com/.default`
7. **Data Set ID** - CDF Data Set to store simulator resources

**Required CDF Capabilities:**

Your service account needs appropriate CDF [capabilities](https://docs.cognite.com/cdf/access/guides/capabilities/#simulator-connectors).

**Create a Data Set:**

1. Go to CDF → Data Management → Data sets
2. Click "Create data set"
3. Give it a name like "Simulator Connector Data"
4. Note the Data Set ID (you'll need this for configuration)

---

**Next:** Continue to [Create Your First Connector](create-connector.md) to build your first Excel-based simulator connector.