// ============================================================================
//  Data Chronicles — Azure infrastructure (Bicep)  [single App Service variant]
//  Resources:
//    App Service (Linux, .NET 9) hosting BOTH the React UI and the API
//    API Management (gateway in front of the API)
//    Azure SQL (server + database)
//    Key Vault (secures the SQL connection string)
//    Application Insights + Log Analytics (telemetry)
//  NOTE: Entra ID app registrations are NOT created here (they live in Microsoft
//  Graph, not ARM). Create with `az ad app create` and pass apiClientId to enable auth.
// ============================================================================

targetScope = 'resourceGroup'

@description('Location for all resources.')
param location string = resourceGroup().location

@description('Short prefix used in resource names.')
param namePrefix string = 'datachronicles'

@description('Entra ID tenant id (for API auth + APIM JWT validation).')
param tenantId string = subscription().tenantId

@description('Entra ID API app registration (client) id. Leave blank to keep auth disabled.')
param apiClientId string = ''

@description('SQL administrator login.')
param sqlAdminUser string = 'dcadmin'

@secure()
@description('SQL administrator password.')
param sqlAdminPassword string

@description('Deploy API Management (adds ~30-45 min to provisioning).')
param deployApim bool = true

param apimPublisherEmail string = 'admin@example.com'
param apimPublisherName string = 'Data Chronicles'

// ---------------------------------------------------------------------------
// Derived, globally-unique names
// ---------------------------------------------------------------------------
var suffix = uniqueString(resourceGroup().id)
var planName = 'asp-${namePrefix}'
var appName = 'app-${namePrefix}-${suffix}'
var apimName = 'apim-${namePrefix}-${suffix}'
var sqlServerName = 'sql-${namePrefix}-${suffix}'
var sqlDbName = 'sqldb-${namePrefix}'
var kvName = toLower(take('kv-${namePrefix}-${suffix}', 24))
var aiName = 'appi-${namePrefix}'
var logName = 'log-${namePrefix}'

// Built-in role: Key Vault Secrets User
var kvSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

// ---------------------------------------------------------------------------
// Observability: Log Analytics + Application Insights
// ---------------------------------------------------------------------------
resource logws 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logName
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: aiName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logws.id
  }
}

// ---------------------------------------------------------------------------
// Azure SQL
// ---------------------------------------------------------------------------
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminUser
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
  }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDbName
  location: location
  sku: { name: 'S0', tier: 'Standard' }
}

resource sqlAllowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// ---------------------------------------------------------------------------
// Key Vault + SQL connection secret
// ---------------------------------------------------------------------------
resource kv 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: kvName
  location: location
  properties: {
    tenantId: tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
  }
}

var sqlConnString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${sqlDbName};User ID=${sqlAdminUser};Password=${sqlAdminPassword};Encrypt=true;TrustServerCertificate=false;'

resource secretSql 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: kv
  name: 'SqlConnection'
  properties: { value: sqlConnString }
}

// ---------------------------------------------------------------------------
// App Service Plan + Web App (UI + API) with managed identity
// ---------------------------------------------------------------------------
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  sku: { name: 'B1', tier: 'Basic' }
  kind: 'linux'
  properties: { reserved: true }
}

resource app 'Microsoft.Web/sites@2023-12-01' = {
  name: appName
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|9.0'
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      webSocketsEnabled: true // SignalR progress hub
      appSettings: [
        { name: 'Auth__Enabled', value: empty(apiClientId) ? 'false' : 'true' }
        { name: 'AzureAd__Instance', value: 'https://login.microsoftonline.com/' }
        { name: 'AzureAd__TenantId', value: tenantId }
        { name: 'AzureAd__ClientId', value: apiClientId }
        { name: 'AzureAd__Audience', value: empty(apiClientId) ? '' : 'api://${apiClientId}' }
        { name: 'ApplicationInsights__ConnectionString', value: appInsights.properties.ConnectionString }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        { name: 'ConnectionStrings__Sql', value: '@Microsoft.KeyVault(SecretUri=${secretSql.properties.secretUri})' }
        { name: 'HuggingFace__Model', value: 'facebook/bart-large-mnli' }
        { name: 'HuggingFace__Token', value: '' } // set later if/when HF is reachable
      ]
    }
  }
}

// Grant the Web App's managed identity read access to Key Vault secrets
resource kvRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kv.id, app.id, kvSecretsUserRoleId)
  scope: kv
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRoleId)
    principalId: app.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------
// API Management (gateway) — optional (slow to provision)
// ---------------------------------------------------------------------------
resource apim 'Microsoft.ApiManagement/service@2023-05-01-preview' = if (deployApim) {
  name: apimName
  location: location
  sku: { name: 'Developer', capacity: 1 }
  properties: {
    publisherEmail: apimPublisherEmail
    publisherName: apimPublisherName
  }
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------
output appName string = appName
output appUrl string = 'https://${app.properties.defaultHostName}'
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output keyVaultName string = kvName
output apimName string = deployApim ? apimName : 'not deployed'
output apimGatewayUrl string = deployApim ? apim.properties.gatewayUrl : 'not deployed'
