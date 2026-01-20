# Prerequisites & Setup

This guide covers setting up your development environment to build simulator connectors with the Cognite Simulator Integration SDK.

## System Requirements

For this tutorial, you need **Windows 10 or later** and **Microsoft Excel**. The SDK supports cross-platform deployment for non-COM integrations.

## Development Tools

You need **.NET 8.0 (LTS) or later**. Check your version with `dotnet --version` and download it from [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download) if needed.

## Cognite Data Fusion (CDF) Setup

You need access to a CDF project. If you don't have one, request access from your administrator or sign up for a trial at [cognite.com](https://www.cognite.com/).

### Service Account / Application Credentials

Your connector needs credentials to authenticate with CDF:
1. **Client ID** - Application/Service Principal ID
2. **Client Secret** - Application secret/key
3. **Tenant ID** - Azure AD tenant ID (for Microsoft Entra ID)
4. **CDF Project Name** - Your CDF project identifier
5. **CDF Host** - Typically `https://api.cognitedata.com` (or your cluster URL)
6. **Scopes** - Typically `https://[cluster].cognitedata.com/.default`
7. **Data Set ID** - CDF Data Set to store simulator resources

Your service account requires appropriate CDF [capabilities](https://docs.cognite.com/cdf/access/guides/capabilities/#simulator-connectors).

### Create a Data Set

In CDF, go to **CDF > Data Management > Data sets** and create a new data set (e.g., "Simulator Connector Data"). Note the Data Set ID for your configuration.

---

**Next:** Continue to [Create Your First Connector](create-connector.md) to build your first Excel-based simulator connector.