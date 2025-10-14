# Load environment variables from .env file
function Load-EnvFile {
    param(
        [string]$EnvFilePath = ".env"
    )
    
    if (Test-Path $EnvFilePath) {
        Write-Host "Loading environment variables from $EnvFilePath" -ForegroundColor Green
        
        Get-Content $EnvFilePath | ForEach-Object {
            if ($_ -match "^\s*([^#][^=]*)\s*=\s*(.*)\s*$") {
                $name = $matches[1].Trim()
                $value = $matches[2].Trim()
                
                # Remove quotes if present
                if ($value.StartsWith('"') -and $value.EndsWith('"')) {
                    $value = $value.Substring(1, $value.Length - 2)
                }
                
                Set-Item -Path "env:$name" -Value $value
                Write-Host "  Set $name" -ForegroundColor Gray
            }
        }
        Write-Host "Environment variables loaded successfully!" -ForegroundColor Green
    } else {
        Write-Host "Warning: .env file not found at $EnvFilePath" -ForegroundColor Yellow
        Write-Host "Please create a .env file with your configuration variables." -ForegroundColor Yellow
    }
}

# Load the .env file
Load-EnvFile

# Optional: Display loaded variables (without showing secrets)
Write-Host "`nLoaded Configuration:" -ForegroundColor Cyan
Write-Host "  CDF_PROJECT: $env:CDF_PROJECT" -ForegroundColor Gray
Write-Host "  IDP_TENANT_ID: $env:IDP_TENANT_ID" -ForegroundColor Gray
Write-Host "  IDP_CLIENT_ID: $env:IDP_CLIENT_ID" -ForegroundColor Gray
Write-Host "  IDP_SCOPE: $env:IDP_SCOPE" -ForegroundColor Gray
Write-Host "  SIMULATOR_DATA_SET_ID: $env:SIMULATOR_DATA_SET_ID" -ForegroundColor Gray
Write-Host "  IDP_CLIENT_SECRET: [HIDDEN]" -ForegroundColor Gray
