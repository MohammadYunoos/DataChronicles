# Data Chronicles — Azure Deployment Architecture & Provisioning Guide

This document lists every Azure resource required to run **Data Chronicles** as per the
reference architecture, and gives step-by-step provisioning instructions (Portal + Azure CLI).

## Architecture → Resource mapping

| Diagram block | Component | Azure resource | Purpose |
|---|---|---|---|
| **Client** | End User | *(none — browser)* | Consumes the web app |
| **Frontend** | React UI | **Azure Static Web Apps** | Hosts the built SPA (`frontend/dist`) on global CDN |
| **Identity** | Entra ID SSO | **Microsoft Entra ID** — 2 App Registrations | SSO login (SPA) + protect the API (`access_as_user` scope) |
| **API Gateway** | Azure APIM | **Azure API Management** | Single ingress, JWT validation, rate-limit, routing to API |
| **Backend Services** | .NET API (App Service) | **Azure App Service (Linux)** + **App Service Plan** | Hosts the .NET 9 Web API |
| **Backend Services** | App Insights (telemetry) | **Application Insights** + **Log Analytics Workspace** | Telemetry, logs, metrics |
| **AI Services** | Azure AI Chat | **Azure OpenAI** + a chat model deployment | LLM chat / summarize over categorized data |
| **AI Services** | Hugging Face BART LLM | *(external SaaS — not Azure)* | Zero-shot categorization (`facebook/bart-large-mnli`) |

### Supporting resources (required by the app, implied by the code)

| Resource | Purpose | Code reference |
|---|---|---|
| **Azure SQL Database** + Logical Server | Persist categorized tickets (EF Core) | `ConnectionStrings:Sql` |
| **Azure Storage Account** (Blob) | Archive generated output `.xlsx` | `AzureStorage:BlobConnectionString` |
| **Azure Key Vault** | Store secrets (HF token, SQL conn, OpenAI key) | `appsettings` → Key Vault refs |
| **Resource Group** | Logical container for all of the above | — |
| **Managed Identity** (system-assigned on App Service) | Secret-less access to Key Vault / SQL / Storage | — |

> **Hugging Face note:** BART runs on Hugging Face's hosted inference (external). If you must
> keep inference inside Azure, host BART on an **Azure Machine Learning managed online endpoint**
> instead and point `HuggingFace:Model`/base URL at it. Either way the app's offline fallback
> still covers outages.

---

## Naming convention used below
```
rg-datachronicles                 Resource group
asp-datachronicles                App Service Plan
app-datachronicles-api            Backend Web App (.NET API)
swa-datachronicles-web            Static Web App (React UI)
apim-datachronicles               API Management
sql-datachronicles                SQL logical server
sqldb-datachronicles              SQL database
stdatachronicles                  Storage account (3-24 lowercase, globally unique)
kv-datachronicles                 Key Vault (globally unique)
appi-datachronicles               Application Insights
log-datachronicles                Log Analytics workspace
oai-datachronicles                Azure OpenAI account
```
Set shared variables (PowerShell):
```powershell
$RG="rg-datachronicles"; $LOC="eastus"
$PLAN="asp-datachronicles"; $API="app-datachronicles-api"
$SWA="swa-datachronicles-web"; $APIM="apim-datachronicles"
$SQLSRV="sql-datachronicles"; $SQLDB="sqldb-datachronicles"
$ST="stdatachronicles$((Get-Random -Max 9999))"; $KV="kv-datachronicles$((Get-Random -Max 9999))"
$APPI="appi-datachronicles"; $LOG="log-datachronicles"; $OAI="oai-datachronicles$((Get-Random -Max 9999))"
```

---

## Provisioning steps

### 0. Prerequisites
```powershell
az login
az account set --subscription "<YOUR_SUBSCRIPTION_ID>"
az group create -n $RG -l $LOC
```

### 1. Microsoft Entra ID — App registrations (Identity)
Create one registration for the **API** (exposes a scope) and one for the **SPA** (signs users in).
```powershell
# API app registration
$apiApp = az ad app create --display-name "DataChronicles-API" | ConvertFrom-Json
az ad app update --id $apiApp.appId --identifier-uris "api://$($apiApp.appId)"
# Expose scope access_as_user (do in Portal: Expose an API → Add scope, or via manifest)

# SPA app registration (SPA redirect URI = Static Web App URL)
$spaApp = az ad app create --display-name "DataChronicles-SPA" `
  --spa-redirect-uris "https://$SWA.azurestaticapps.net"
```
Record: **TenantId**, API **ClientId** (`$apiApp.appId`), SPA **ClientId** (`$spaApp.appId`),
and grant the SPA delegated permission to the API's `access_as_user` scope (Portal → API permissions).

### 2. Log Analytics + Application Insights (telemetry)
```powershell
az monitor log-analytics workspace create -g $RG -n $LOG -l $LOC
$logId = az monitor log-analytics workspace show -g $RG -n $LOG --query id -o tsv
az monitor app-insights component create -g $RG -a $APPI -l $LOC --workspace $logId
$aiConn = az monitor app-insights component show -g $RG -a $APPI --query connectionString -o tsv
```

### 3. Azure SQL (persistence)
```powershell
az sql server create -g $RG -n $SQLSRV -l $LOC `
  --admin-user dcadmin --admin-password "<STRONG_PASSWORD>"
az sql db create -g $RG -s $SQLSRV -n $SQLDB --service-objective S0
# Allow Azure services through the firewall
az sql server firewall-rule create -g $RG -s $SQLSRV -n AllowAzure `
  --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0
$sqlConn = "Server=tcp:$SQLSRV.database.windows.net,1433;Database=$SQLDB;User ID=dcadmin;Password=<STRONG_PASSWORD>;Encrypt=true;"
```

### 4. Storage account (Blob archival)
```powershell
az storage account create -g $RG -n $ST -l $LOC --sku Standard_LRS --kind StorageV2
az storage container create --account-name $ST -n datachronicles
$stConn = az storage account show-connection-string -g $RG -n $ST --query connectionString -o tsv
```

### 5. Azure OpenAI (Azure AI Chat)
```powershell
az cognitiveservices account create -g $RG -n $OAI -l $LOC `
  --kind OpenAI --sku S0 --custom-domain $OAI
# Deploy a chat model (availability varies by region/quota)
az cognitiveservices account deployment create -g $RG -n $OAI `
  --deployment-name gpt-4o --model-name gpt-4o --model-version "2024-08-06" `
  --model-format OpenAI --sku-capacity 10 --sku-name Standard
$oaiEndpoint = az cognitiveservices account show -g $RG -n $OAI --query properties.endpoint -o tsv
$oaiKey = az cognitiveservices account keys list -g $RG -n $OAI --query key1 -o tsv
```

### 6. Key Vault (secrets)
```powershell
az keyvault create -g $RG -n $KV -l $LOC --enable-rbac-authorization true
# Store secrets
az keyvault secret set --vault-name $KV -n SqlConnection --value "$sqlConn"
az keyvault secret set --vault-name $KV -n BlobConnection --value "$stConn"
az keyvault secret set --vault-name $KV -n OpenAiKey --value "$oaiKey"
az keyvault secret set --vault-name $KV -n HuggingFaceToken --value "<HF_TOKEN>"
```

### 7. App Service Plan + Backend Web App (.NET API)
```powershell
az appservice plan create -g $RG -n $PLAN --sku B1 --is-linux
az webapp create -g $RG -p $PLAN -n $API --runtime "DOTNETCORE:9.0"
# System-assigned managed identity for secret-less Key Vault access
az webapp identity assign -g $RG -n $API
$miId = az webapp identity show -g $RG -n $API --query principalId -o tsv
$kvId = az keyvault show -n $KV --query id -o tsv
az role assignment create --assignee $miId --role "Key Vault Secrets User" --scope $kvId

# App settings (use Key Vault references for secrets)
az webapp config appsettings set -g $RG -n $API --settings `
  Auth__Enabled=true `
  AzureAd__TenantId="<TENANT_ID>" `
  AzureAd__ClientId="$($apiApp.appId)" `
  AzureAd__Audience="api://$($apiApp.appId)" `
  ApplicationInsights__ConnectionString="$aiConn" `
  HuggingFace__Token="@Microsoft.KeyVault(VaultName=$KV;SecretName=HuggingFaceToken)" `
  ConnectionStrings__Sql="@Microsoft.KeyVault(VaultName=$KV;SecretName=SqlConnection)" `
  AzureStorage__BlobConnectionString="@Microsoft.KeyVault(VaultName=$KV;SecretName=BlobConnection)" `
  AzureAI__Endpoint="$oaiEndpoint" `
  AzureAI__DeploymentName="gpt-4o" `
  AzureAI__ApiKey="@Microsoft.KeyVault(VaultName=$KV;SecretName=OpenAiKey)"

# Deploy the published API
dotnet publish backend/DataChronicles.Api -c Release -o publish
Compress-Archive -Path publish/* -DestinationPath api.zip -Force
az webapp deploy -g $RG -n $API --src-path api.zip --type zip
```

### 8. Static Web App (React UI / Frontend)
```powershell
# Build locally
Push-Location frontend; npm ci; npm run build; Pop-Location
# Create SWA and deploy the dist folder (or connect to a GitHub repo for CI/CD)
az staticwebapp create -g $RG -n $SWA -l $LOC
# Deploy with the SWA CLI:  npx @azure/static-web-apps-cli deploy frontend/dist --deployment-token <token>
```
Set the SPA to call the API through APIM (point `vite.config`/build-time base URL at the APIM gateway
URL, or configure SWA's `staticwebapp.config.json` API proxy).

### 9. API Management (API Gateway)
```powershell
az apim create -g $RG -n $APIM -l $LOC `
  --publisher-email admin@yourorg.com --publisher-name "DataChronicles" --sku-name Developer
# Import the API from the backend's OpenAPI/Swagger
az apim api import -g $RG --service-name $APIM --path datachronicles `
  --specification-format OpenApi `
  --specification-url "https://$API.azurewebsites.net/swagger/v1/swagger.json" `
  --api-id datachronicles-api
```
Then add a **validate-jwt** inbound policy on the API so APIM enforces Entra ID tokens
(matches the diagram's "validate token" arrow):
```xml
<inbound>
  <base />
  <validate-jwt header-name="Authorization" failed-validation-httpcode="401">
    <openid-config url="https://login.microsoftonline.com/<TENANT_ID>/v2.0/.well-known/openid-configuration" />
    <audiences><audience>api://<API_CLIENT_ID></audience></audiences>
  </validate-jwt>
  <set-backend-service base-url="https://<API>.azurewebsites.net" />
</inbound>
```

### 10. Final wiring
- **CORS**: set the API's `Cors:AllowedOrigins` (app setting `Cors__AllowedOrigins__0`) to the SWA URL.
- **SPA config**: set MSAL `clientId`/`authority`/API scope, and base API URL = APIM gateway URL.
- **DB schema**: app calls `EnsureCreated()` on startup; for production prefer EF Core migrations.
- **Smoke test**: `GET https://<APIM>.azure-api.net/datachronicles/api/health`.

---

## Cost-saving / dev tiers
| Resource | Dev/test SKU |
|---|---|
| App Service Plan | **B1** (or F1 free) |
| Azure SQL | **Basic / S0** (or serverless) |
| APIM | **Developer** (non-SLA) |
| Storage | **Standard_LRS** |
| Static Web Apps | **Free** tier |
| Azure OpenAI | **S0** pay-as-you-go (mind token quota) |

## Teardown
```powershell
az group delete -n $RG --yes --no-wait
```
