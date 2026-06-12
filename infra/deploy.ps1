# ============================================================================
#  Data Chronicles — one-shot deployment (single App Service: UI + API)
#  Provisions infra (Bicep) then builds the React UI into the API's wwwroot
#  and deploys the combined app to App Service.
#  Usage:
#    ./deploy.ps1 -ResourceGroup rg-datachronicles -Location eastus -SqlPassword '<Strong!Pass123>'
# ============================================================================
param(
  [Parameter(Mandatory = $true)] [string] $ResourceGroup,
  [string] $Location = 'eastus',
  [Parameter(Mandatory = $true)] [string] $SqlPassword,
  [string] $ApiClientId = '',
  [bool]   $DeployApim = $true
)

$ErrorActionPreference = 'Stop'
$infra = $PSScriptRoot
$root = Split-Path $infra -Parent

Write-Host "==> Creating resource group $ResourceGroup ($Location)" -ForegroundColor Cyan
az group create -n $ResourceGroup -l $Location | Out-Null

Write-Host "==> Deploying Bicep (30-45 min if APIM enabled)" -ForegroundColor Cyan
$deploy = az deployment group create `
  -g $ResourceGroup -f "$infra/main.bicep" -p "$infra/main.parameters.json" `
  -p sqlAdminPassword=$SqlPassword apiClientId=$ApiClientId deployApim=$DeployApim location=$Location `
  | ConvertFrom-Json

$appName = $deploy.properties.outputs.appName.value
$appUrl = $deploy.properties.outputs.appUrl.value

Write-Host "==> Building React UI" -ForegroundColor Cyan
Push-Location "$root/frontend"; npm ci; npm run build; Pop-Location

Write-Host "==> Publishing API + bundling UI into wwwroot" -ForegroundColor Cyan
$publish = "$root/publish"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish "$root/backend/DataChronicles.Api" -c Release -o $publish
New-Item -ItemType Directory -Force -Path "$publish/wwwroot" | Out-Null
Copy-Item "$root/frontend/dist/*" "$publish/wwwroot/" -Recurse -Force

Write-Host "==> Zipping and deploying to $appName" -ForegroundColor Cyan
$zip = "$root/app.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$publish/*" -DestinationPath $zip -Force
az webapp deploy -g $ResourceGroup -n $appName --src-path $zip --type zip | Out-Null

if ($DeployApim) {
  Write-Host "==> Importing API into APIM" -ForegroundColor Cyan
  $apimName = $deploy.properties.outputs.apimName.value
  az apim api import -g $ResourceGroup --service-name $apimName --path datachronicles `
    --api-id datachronicles-api --specification-format OpenApi `
    --specification-url "$appUrl/swagger/v1/swagger.json" 2>$null
}

Write-Host "`n==================== DONE ====================" -ForegroundColor Green
Write-Host "App      : $appUrl"
Write-Host "Health   : $appUrl/api/health"
$deploy.properties.outputs | ConvertTo-Json -Depth 4
