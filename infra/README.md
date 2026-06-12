# Infrastructure as Code — Data Chronicles (Bicep)

Provisions the Azure stack and deploys the app as a **single App Service hosting both
the React UI and the .NET API**, fronted by API Management.

## Files
| File | Purpose |
|---|---|
| `main.bicep` | Azure resources (App Service, APIM, Azure SQL, Key Vault, App Insights) |
| `main.parameters.json` | Default parameter values |
| `deploy.ps1` | Provision infra **+** build UI into the API's `wwwroot` **+** deploy |

## What gets created
- **App Service Plan (B1, Linux)** + **Web App (.NET 9)** — serves the React SPA (`wwwroot`) and the API
- **API Management (Developer)** — gateway in front of the API
- **Azure SQL** — logical server + S0 database (+ "allow Azure services" firewall rule)
- **Key Vault** — holds the SQL connection string; the Web App reads it via a managed identity
- **Application Insights + Log Analytics** — telemetry
- **System-assigned Managed Identity** on the Web App with the `Key Vault Secrets User` role

> Dropped vs. the full design: Static Web App (UI now on App Service), Azure OpenAI, Storage.
> Categorization + chat run on the built-in offline path; set `HuggingFace__Token` later to use BART.

## Prerequisites
- Azure CLI + Bicep: `az bicep install`
- `az login` and `az account set --subscription <id>`
- **Entra ID app registration is optional** — leave `apiClientId` blank to deploy with auth off.
  To enable Entra ID auth:
  ```powershell
  $api = az ad app create --display-name "DataChronicles-API" | ConvertFrom-Json
  az ad app update --id $api.appId --identifier-uris "api://$($api.appId)"
  # pass -ApiClientId $api.appId to deploy.ps1
  ```

## Deploy (one command)
```powershell
cd infra
./deploy.ps1 -ResourceGroup rg-datachronicles -Location eastus -SqlPassword '<Strong!Pass123>'
```

## Validate before deploying
```powershell
az bicep build --file main.bicep
az deployment group what-if -g rg-datachronicles -f main.bicep -p main.parameters.json `
  -p sqlAdminPassword='<Strong!Pass123>'
```

## Notes
- **APIM is slow** (~30-45 min). Use `-DeployApim $false` for fast iterations, add it later.
- After deploy, add APIM's **validate-jwt** inbound policy if you enabled auth
  (see `../docs/azure-provisioning.md` §9).
- **DB schema** is created on startup via `EnsureCreated()`; prefer EF migrations for prod.
- Teardown: `az group delete -n rg-datachronicles --yes --no-wait`
