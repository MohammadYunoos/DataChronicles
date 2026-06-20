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
| `AzureAI__Endpoint` | *(Azure AI/OpenAI endpoint, e.g. `https://<res>.services.ai.azure.com/openai/v1`; empty = rule-based chat fallback)* |
| `AzureAI__DeploymentName` | *(model/deployment name, e.g. `gpt-4o-mini`)* |
| `AzureAI__ApiKey` | `@Microsoft.KeyVault(VaultName=kv-datachronicles-<unique>;SecretName=AzureAiApiKey)` |
| `AzureAI__ApiVersion` | *(leave empty for `services.ai.azure.com/openai/v1`; set e.g. `2024-10-21` only for a classic `*.openai.azure.com` endpoint)* |
| `AzureAI__EmbeddingDeploymentName` | *(separate embeddings deployment, e.g. `text-embedding-3-small`; enables **semantic** duplicate detection + issue grouping. Empty = deterministic JobName+Category matching)* |
| `AzureAI__SimilarityThreshold` | *(optional; cosine cut-off for "duplicate/similar", default `0.6` — `text-embedding-3-small` scores reworded one-liners ~0.6–0.75; raise toward 0.8 for stricter, near-identical-only matching)* |

> **Azure AI Chat:** with `AzureAI__*` set, the assistant answers via the LLM grounded in the batch data; left
> empty, it uses the built-in rule-based assistant. Store the key in Key Vault (add an `AzureAiApiKey` secret like
> the SQL one in Step 4) — never as a plaintext app setting.

> **Semantic duplicates/grouping:** `AzureAI__EmbeddingDeploymentName` needs its **own** model deployment
> (`gpt-4o-mini` cannot produce embeddings) — deploy e.g. `text-embedding-3-small` in the same resource. Without it,
> duplicates/grouping still work via a deterministic JobName+Category key.

> ⚠️ **Azure SQL schema:** the duplicate/grouping feature adds `IsDuplicate`, `DuplicateOf`, `Embedding` columns to
> `Tickets`. `EnsureCreated` does **not** alter an existing table, so on an already-created DB run once:
> `ALTER TABLE [dbo].[Tickets] ADD [IsDuplicate] bit NOT NULL CONSTRAINT DF_Tickets_IsDuplicate DEFAULT 0, [DuplicateOf] nvarchar(max) NULL, [Embedding] nvarchar(max) NULL;`
> (or drop the table to let it recreate). Same caveat as the earlier `Source` column.

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
5. *(If you enabled the in-app JWT toggle, `Auth__Enabled=true`)* select the API → **Design → Inbound processing → </> (policy editor)** and add:
   ```xml
   <validate-jwt header-name="Authorization" failed-validation-httpcode="401">
     <openid-config url="https://login.microsoftonline.com/<TENANT_ID>/v2.0/.well-known/openid-configuration" />
     <audiences><audience>api://<API_CLIENT_ID></audience></audiences>
   </validate-jwt>
   ```
6. *(Required if **Easy Auth** is enabled — Step 10)* APIM must authenticate to the gated backend, or every call
   through APIM returns **401** — see the next subsection.

### Step 9 runbook: APIM in front of an Easy-Auth-protected backend
When **Easy Auth (Step 10)** gates the App Service, APIM's server-to-server calls must authenticate to the backend
or they fail. This runbook is the verified, working configuration plus the exact error→fix sequence we hit getting
there. Placeholders (`<EASYAUTH_APP_CLIENT_ID>`, `<APIM_MI_CLIENT_ID>`) are **app identifiers, not secrets**.

**The auth model (two hops).** No single bearer token comes from the caller; each hop authenticates differently:
```
client/Postman ──(APIM subscription key)──▶ APIM ──(bearer token APIM mints from its managed identity)──▶ App Service (Easy Auth)
```
- **Hop 1 (caller → APIM):** the `Ocp-Apim-Subscription-Key` — an API key, not an identity.
- **Hop 2 (APIM → backend):** the `authentication-managed-identity` policy makes APIM fetch an Entra token for the
  Easy Auth app and attach it as `Authorization: Bearer …`. This is the bearer token — **APIM supplies it, the
  caller never sees it.** That is why Postman needs no token (see *"Calling the API from Postman"* below).

#### Working configuration
1. **APIM managed identity on** — APIM → **Security → Managed identities → System assigned → On → Save**.
2. **APIM inbound policy** — APIM → APIs → the API → **Design → All operations → Inbound processing → </>**, inside
   `<inbound>` after `<base />`:
   ```xml
   <authentication-managed-identity resource="<EASYAUTH_APP_CLIENT_ID>" />
   ```
   `resource` must equal the Easy Auth app's **Application (client) ID**; the token audience is then
   `api://<EASYAUTH_APP_CLIENT_ID>`, which must match Easy Auth's **Allowed token audiences**.
3. **Easy Auth authorization** — App Service → **Authentication → Edit** the Microsoft provider:
   - **Client application requirement = "Allow requests from specific client applications"**, and
     **Allowed client applications** must list **both**:
     - the **Easy Auth app's own client ID** (so interactive **browser SSO** keeps working), **and**
     - **`<APIM_MI_CLIENT_ID>`** — APIM's *managed-identity* Application (client) ID (so the APIM→backend call is
       allowed). *Listing only the Easy Auth app's own ID is the common mistake: browsers work but APIM still 403s.*
   - Identity requirement = any identity; Tenant requirement = issuer tenant only.
   - **Find `<APIM_MI_CLIENT_ID>`:** Entra ID → **Enterprise applications** → set **Application type = Managed
     Identities** → search the APIM instance name → **Application ID**. (This is *not* the Object/principal ID shown
     on APIM's Managed identities blade.)

**Verify:** APIM **Test → `GET /api/health` → 200** (`server: Kestrel`, `x-ms-middleware-request-id` present); then
`GET /api/categorize/sample` → **200**. A direct browser hit to the App Service still shows the Easy Auth login —
the backend stayed fully gated.

#### Error → fix sequence (what each symptom means)
- **401, `www-authenticate: Bearer realm="<app>.azurewebsites.net"`, `x-ms-middleware-request-id`** — Easy Auth
  rejecting a **tokenless** call (authentication). The `resource_id` in that header is `<EASYAUTH_APP_CLIENT_ID>`.
  → apply the managed-identity policy (config step 2).
- **403, empty body, no `www-authenticate`** — token **accepted** but caller **not authorized** (authorization).
  → add `<APIM_MI_CLIENT_ID>` to Allowed client applications (config step 3).
- **Authentication blade won't persist edits** (re-open shows nothing saved) — the provider points at an
  **orphaned app registration** (portal banner: *"Application … not found in the current tenant"*). The V2 blade
  silently discards edits in that state. Confirm via **Azure Resource Explorer** (`https://resources.azure.com` →
  subscriptions → … → sites → the web app → config → **authsettingsV2** → `globalValidation.defaultAuthorizationPolicy`).
  → **Remove and re-create** the Microsoft identity provider with *Create new app registration*. ⚠️ This mints a
  **new client ID** — you must update the APIM policy `resource` and the Allowed token audiences to the new ID.
- **Timeout / `HTTP/1.1 -1 Unknown`** — with *Unauthenticated requests = HTTP 302 redirect*, a mismatched token
  makes Easy Auth 302 APIM to the Microsoft login page, which APIM follows and hangs. → while debugging set
  **Unauthenticated requests = HTTP 401** (fast, clear failures); 302 is fine again once APIM returns 200.

#### Calling the API from Postman
Call **APIM**, not the App Service directly (direct calls need a full Entra OAuth token; through APIM you need only
the subscription key — APIM adds the backend token via hop 2).
1. **Subscription key:** APIM → **Subscriptions → Built-in all-access subscription → Show/hide keys → Primary key**.
   Treat it as a secret; regenerate if leaked. (For external consumers, instead associate the API with a **Product**
   and issue product subscriptions.)
2. **Base URL:** `https://<apim-name>.azure-api.net/datachronicles`. Add header `Ocp-Apim-Subscription-Key: <key>`
   to every request.
3. **Requests:** `GET /api/health`; `GET /api/categorize/sample`; `POST /api/categorize/upload` (Body → form-data,
   key `file` = .xlsx); `POST /api/chat` (raw JSON `{ "question": "...", "batchId": "..." }`);
   `GET /api/categorize/download/{batchId}`. Fastest setup: **Import** `datachronicles-openapi.json` into Postman to
   get the whole collection, then set the base URL + subscription-key header as collection variables.

> **Security note:** through APIM the subscription key is the *only* credential and carries **no user identity** —
> protect it like a password. The browser path keeps real per-user Entra SSO. To require caller identity at the
> gateway too, add an APIM **`validate-jwt`** inbound policy (callers then present their own Entra token).

## Step 10 (optional) — Entra ID SSO via App Service Authentication ("Easy Auth")
Gate the whole site behind org sign-in at the **platform level** — no app code, no redeploy.

1. Web App → **Settings → Authentication** → **Add identity provider**.
2. **Identity provider** = **Microsoft**.
3. **App registration** = *Create new app registration* (Azure creates + wires it automatically) ·
   name e.g. `datachronicles-easyauth` · **Supported account types = Current tenant – Single tenant**.
4. **Restrict access** = **Require authentication**.
5. **Unauthenticated requests** = **HTTP 302 Found redirect (Microsoft Entra ID)** → **Add**.
6. Keep the in-app setting **`Auth__Enabled = false`** (Easy Auth protects at the platform layer; don't
   double-protect). No code change or redeploy is needed.

After saving, visiting the site redirects to the Microsoft login; a session cookie then authenticates the
SPA, all `/api/*` calls, and `/progressHub`. The UI shows "Signed in as <name>" + a **Logout** link
(built on Easy Auth's `/.auth/me` and `/.auth/logout` endpoints).

**Notes:**
- `/api/health` is also gated. To allow anonymous monitoring, add it under **Authentication → Edit →
  excluded paths**.
- **If APIM (Step 9) fronts this app**, the gate also blocks APIM's server-to-server calls (every APIM Test/call
  returns 401). Give APIM a managed identity + `authentication-managed-identity` policy and authorize it — see
  *"Step 9 runbook: APIM in front of an Easy-Auth-protected backend"* above.
- To limit access to specific people (not all org users): **Microsoft Entra ID → Enterprise applications →
  `datachronicles-easyauth` → Properties → Assignment required = Yes**, then assign users/groups.
- For per-endpoint roles/claims or a split UI/API later, use the in-app MSAL+JWT path instead
  (`Auth__Enabled=true` + `AzureAd__*` + APIM validate-jwt in Step 9.5).

### Troubleshooting: my corporate account can't sign in
Symptom — after enabling Easy Auth, a cloud account works but a corporate account
(e.g. `you@company.com`) fails with:

> *"Selected user account does not exist in tenant 'Default Directory' and cannot access the application
> '…' in that tenant. The account needs to be added as an external user in the tenant first."*

Cause — the Azure subscription is in a personal **"Default Directory"** Entra tenant, so Easy Auth registered
the app there as **single-tenant**. Only users *in that directory* can sign in; the corporate account lives in
a different tenant.

Fix (self-service, keeps single-tenant) — **invite the corporate account as a guest (B2B) user** into the
Default Directory tenant. Do all of this while the Portal **directory switcher** (top-right) is set to
*Default Directory*:
1. **Microsoft Entra ID → Users → New user → Invite external user** → enter the corporate email → send.
2. Open the invitation email in that mailbox → **Accept/redeem** (one-time).
3. *Only if* the enterprise app has **Assignment required = Yes**, add the new guest under
   **Enterprise applications → `datachronicles-easyauth` → Users and groups → Add**.
4. Reopen the app in an incognito window → **Use another account** → the corporate account now signs in.

> To instead allow **every** corporate employee with their work account, the app must be made multi-tenant or
> registered in the corporate Entra tenant (needs that tenant's admin) — beyond a self-service POC.

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
- [ ] (optional) SSO via App Service Authentication (Easy Auth) — Step 10

## Teardown
Delete the resource group in the Portal (**Resource groups → rg-datachronicles → Delete**)
to remove everything at once.
