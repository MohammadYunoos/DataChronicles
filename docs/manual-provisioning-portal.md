# Data Chronicles — Manual Provisioning (Azure Portal)

Step-by-step Portal guide for the chosen architecture:
**one App Service hosting the UI + API · APIM · Azure SQL · Key Vault · Application Insights.**

No Azure CLI required. You build the deployment package locally (you already have
`dotnet` 9 and `node`), then upload it via the Portal.

> Suggested names (use your own): RG `rg-datachronicles`, region `East US`.

---

## Step 1 — Resource Group
1. Portal → **Resource groups** → **Create**.
2. Subscription = yours · Name = `rg-datachronicles` · Region = `East US` → **Review + create** → **Create**.

## Step 2 — Application Insights (telemetry)
1. **Create a resource** → search **Application Insights** → **Create**.
2. RG = `rg-datachronicles` · Name = `appi-datachronicles` · Region = East US · Resource mode = **Workspace-based** (it creates/uses a Log Analytics workspace).
3. **Review + create** → **Create**.
4. After deploy → open it → **Overview** → copy the **Connection String** (used in Step 6).

## Step 3 — Azure SQL (database)
1. **Create a resource** → **SQL Database** → **Create**.
2. RG = `rg-datachronicles` · DB name = `sqldb-datachronicles`.
3. **Server** → **Create new**: server name `sql-datachronicles-<unique>`, region East US,
   Authentication = **SQL authentication**, admin login = `dcadmin`, set a strong password (remember it).
4. **Compute + storage** → pick **Basic** or **Standard S0** (cheap for a POC).
5. **Networking** tab → Connectivity = Public endpoint → set **Allow Azure services and resources to access this server = Yes**.
6. **Review + create** → **Create**.
7. Build the connection string (you'll store it in Key Vault next):
   ```
   Server=tcp:sql-datachronicles-u2a.database.windows.net,1433;Database=sqldb-datachronicles;User ID=dcadmin;Password=Yunoos123!;Encrypt=true;TrustServerCertificate=false;
   ```

## Step 4 — Key Vault (+ SQL secret)
1. **Create a resource** → **Key Vault** → **Create**.
2. RG = `rg-datachronicles` · Name = `kv-datachronicles-<unique>` · Region East US.
3. **Access configuration** tab → Permission model = **Azure role-based access control (RBAC)**.
4. **Review + create** → **Create**.
5. Open the vault → **Objects → Secrets → Generate/Import**:
   - Name = `SqlConnection` · Value = the connection string from Step 3.7 → **Create**.
   *(You'll grant the App Service access to this in Step 7.)*

## Step 5 — App Service Plan + Web App (UI + API)
1. **Create a resource** → **Web App** → **Create**.
2. RG = `rg-datachronicles` · Name = `app-datachronicles-<unique>` (this becomes your URL).
3. Publish = **Code** · Runtime stack = **.NET 9 (STS)** · OS = **Linux** · Region = East US.
4. **App Service Plan** → Create new → SKU = **B1 Basic** (or F1 Free for a quick demo).
5. **Monitoring** tab → Application Insights = **Yes** → select `appi-datachronicles`.
6. **Review + create** → **Create**.

## Step 6 — App settings (configuration)
Open the Web App → **Settings → Environment variables → App settings** → add each
(Name → Value), then **Apply**:

| Name | Value |
|---|---|
| `Auth__Enabled` | `false` (set `true` only after Step 9 Entra setup) |
| `ConnectionStrings__Sql` | `@Microsoft.KeyVault(VaultName=kv-datachronicles-<unique>;SecretName=SqlConnection)` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | *(connection string from Step 2.4)* |
| `ApplicationInsights__ConnectionString` | *(same value)* |
| `HuggingFace__Model` | `facebook/bart-large-mnli` |
| `HuggingFace__Token` | *(leave empty — offline classifier is used)* |

> The `@Microsoft.KeyVault(...)` reference resolves automatically once Step 7 grants access.
> SignalR needs Web Sockets: **Settings → Configuration → General settings → Web sockets = On**.

## Step 7 — Managed identity → Key Vault access
1. Web App → **Settings → Identity** → **System assigned** → Status = **On** → **Save**.
2. Open the **Key Vault** → **Access control (IAM)** → **Add → Add role assignment**.
3. Role = **Key Vault Secrets User** → Next.
4. Assign access to = **Managed identity** → **Select members** → pick your Web App → **Review + assign**.
5. Back on the Web App → restart it so the Key Vault reference resolves.

## Step 8 — Build the package locally and deploy
Run locally (you have dotnet 9 + node):
```powershell
# 1. Build the React UI
cd frontend; npm ci; npm run build; cd ..

# 2. Publish the API and bundle the UI into wwwroot
dotnet publish backend/DataChronicles.Api -c Release -o publish
New-Item -ItemType Directory -Force -Path publish/wwwroot | Out-Null
Copy-Item frontend/dist/* publish/wwwroot/ -Recurse -Force

# 3. Zip it
Compress-Archive -Path publish/* -DestinationPath app.zip -Force
```
Deploy the zip through the Portal (no CLI):
- Open `https://app-datachronicles-<unique>.scm.azurewebsites.net/ZipDeployUI`
  (Kudu) and **drag `app.zip`** onto the page, **or**
- Web App → **Deployment Center** → choose a source (Local Git / GitHub) and push.

### CI/CD option (GitHub Actions + publish profile)
A ready workflow lives at [`.github/workflows/azure-webapp.yml`](../.github/workflows/azure-webapp.yml).
It builds the UI, bundles it into the API's `wwwroot`, and deploys on every push to `main`.
It authenticates with the Web App's **publish profile** (requires SCM basic-auth publishing = On).

Set it up (one secret, no CLI):

1. Confirm **Settings → Configuration → "SCM Basic Auth Publishing Credentials" = On** on the Web App.
2. Web App → **Overview → Download publish profile** (downloads a `.PublishSettings` XML file).
3. GitHub repo → **Settings → Secrets and variables → Actions → New repository secret**:
   - Name = `AZURE_WEBAPP_PUBLISH_PROFILE`
   - Value = the **entire contents** of the downloaded file.
4. Set the workflow's `AZURE_WEBAPP_NAME` to your Web App name.
5. Push to `main` (or run from the **Actions** tab) — it builds and deploys automatically.

> A **"No credentials found"** failure means this secret is missing/empty or misnamed — re-check step 3.

> **Alternative (OIDC, no basic auth):** if basic-auth publishing is disabled by policy, switch to
> `azure/login@v2` with a federated credential on an Entra app registration and the secrets
> `AZURE_CLIENT_ID` / `AZURE_TENANT_ID` / `AZURE_SUBSCRIPTION_ID` (add `permissions: id-token: write`,
> drop the `publish-profile` input). Portal-only setup: App registration → **Federated credentials**
> → "GitHub Actions" (Entity = Branch `main`) → grant the app **Contributor** on the resource group.

Verify: browse `https://app-datachronicles-<unique>.azurewebsites.net` (UI) and
`/api/health` (API). The DB schema is created automatically on first run.

## Step 9 — API Management (gateway)
1. **Create a resource** → **API Management** → **Create**.
2. RG = `rg-datachronicles` · Resource name = `apim-datachronicles-<unique>` · Region East US ·
   Organization name + admin email · Pricing tier = **Developer** (no SLA, cheapest).
   *(Provisioning takes ~30–45 minutes.)*
3. When ready → **APIs → Add API → OpenAPI** →
   OpenAPI spec URL = `https://app-datachronicles-<unique>.azurewebsites.net/swagger/v1/swagger.json` ·
   API URL suffix = `datachronicles` → **Create**.
4. Test from APIM → **Test** tab → call `GET /api/health`.
5. *(If you enabled auth)* select the API → **Design → Inbound processing → </> (policy editor)** and add:
   ```xml
   <validate-jwt header-name="Authorization" failed-validation-httpcode="401">
     <openid-config url="https://login.microsoftonline.com/<TENANT_ID>/v2.0/.well-known/openid-configuration" />
     <audiences><audience>api://<API_CLIENT_ID></audience></audiences>
   </validate-jwt>
   ```

## Step 10 (optional) — Entra ID SSO
Only if you want login/token enforcement:
1. **Microsoft Entra ID → App registrations → New registration** → name `DataChronicles-API`.
2. **Expose an API** → Set Application ID URI `api://<app-id>` → **Add a scope** `access_as_user`.
3. Put the **Tenant ID** and **client (application) ID** into the Web App settings
   (`AzureAd__TenantId`, `AzureAd__ClientId`, `AzureAd__Audience=api://<app-id>`) and set
   `Auth__Enabled=true`. Add the validate-jwt policy in Step 9.5.

---

## Quick checklist
- [ ] Resource group
- [ ] Application Insights (copy connection string)
- [ ] Azure SQL (server + DB, allow Azure services)
- [ ] Key Vault + `SqlConnection` secret
- [ ] Web App (.NET 9 Linux, B1) + app settings + web sockets on
- [ ] System-assigned identity + Key Vault **Secrets User** role
- [ ] Build `app.zip` locally → ZipDeploy
- [ ] APIM created → import API from `/swagger/v1/swagger.json`
- [ ] (optional) Entra ID registration + validate-jwt policy

## Teardown
Delete the resource group in the Portal (**Resource groups → rg-datachronicles → Delete**)
to remove everything at once.
