# Azure Logic Apps Deployment Script for BTP Automation
# Deploys all BTP Logic App workflows to an Azure Standard Logic App instance

param(
    [Parameter(Mandatory=$true)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory=$true)]
    [string]$Location,

    [Parameter(Mandatory=$true)]
    [string]$SubscriptionId,

    [Parameter(Mandatory=$false)]
    [string]$Environment = "dev"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "BTP Automation - Azure Logic Apps Deployment" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Set Azure subscription context
Write-Host "Setting Azure subscription context..." -ForegroundColor Yellow
az account set --subscription $SubscriptionId

# Create resource group if it doesn't exist
Write-Host "Ensuring resource group exists..." -ForegroundColor Yellow
az group create --name $ResourceGroupName --location $Location

# Deploy Storage Account (required for Logic App Standard)
Write-Host ""
Write-Host "Deploying Storage Account..." -ForegroundColor Yellow
$storageAccountName = "btpautomation$Environment"
az storage account create `
    --name $storageAccountName `
    --resource-group $ResourceGroupName `
    --location $Location `
    --sku Standard_LRS `
    --kind StorageV2

# Deploy Application Insights
Write-Host ""
Write-Host "Deploying Application Insights..." -ForegroundColor Yellow
$appInsightsName = "btp-insights-$Environment"
az monitor app-insights component create `
    --app $appInsightsName `
    --location $Location `
    --resource-group $ResourceGroupName `
    --application-type web

# Deploy Logic App (Standard)
Write-Host ""
Write-Host "Deploying Logic App (Standard)..." -ForegroundColor Yellow
$logicAppName = "btp-automation-$Environment"
$appServicePlanName = "btp-asp-$Environment"

# Create App Service Plan (WS1 = Workflow Standard)
az appservice plan create `
    --name $appServicePlanName `
    --resource-group $ResourceGroupName `
    --location $Location `
    --sku WS1 `
    --is-linux

# Create Logic App
az logicapp create `
    --resource-group $ResourceGroupName `
    --name $logicAppName `
    --storage-account $storageAccountName `
    --plan $appServicePlanName

# Configure Logic App settings
Write-Host "Configuring Logic App app settings..." -ForegroundColor Yellow
$instrumentationKey = az monitor app-insights component show `
    --app $appInsightsName `
    --resource-group $ResourceGroupName `
    --query instrumentationKey `
    --output tsv

az logicapp config appsettings set `
    --name $logicAppName `
    --resource-group $ResourceGroupName `
    --settings `
        "APPINSIGHTS_INSTRUMENTATIONKEY=$instrumentationKey" `
        "WORKFLOWS_SUBSCRIPTION_ID=$SubscriptionId" `
        "WORKFLOWS_RESOURCE_GROUP_NAME=$ResourceGroupName" `
        "WORKFLOWS_LOCATION_NAME=$Location"

# Deploy workflows
Write-Host ""
Write-Host "Deploying BTP workflows..." -ForegroundColor Yellow

$workflows = @(
    "subaccount-management",
    "entitlements-management",
    "provisioning-management",
    "authorization-management",
    "destinations-management",
    "main-orchestrator",
    "main-dispatcher"
)

$workflowUrls = @{}

foreach ($workflow in $workflows) {
    Write-Host "  - Deploying $workflow..." -ForegroundColor Gray

    $workflowPath = "../workflows/$workflow.json"

    az logicapp workflow create `
        --resource-group $ResourceGroupName `
        --name $logicAppName `
        --workflow-name $workflow `
        --definition $workflowPath

    if ($LASTEXITCODE -eq 0) {
        Write-Host "    Deployed: $workflow" -ForegroundColor Green
    } else {
        Write-Host "    FAILED:   $workflow" -ForegroundColor Red
    }
}

# Retrieve trigger URLs for child workflows after deployment
Write-Host ""
Write-Host "Retrieving workflow trigger URLs..." -ForegroundColor Yellow

foreach ($workflow in $workflows) {
    $url = az logicapp workflow show `
        --resource-group $ResourceGroupName `
        --name $logicAppName `
        --workflow-name $workflow `
        --query "properties.accessEndpoint" `
        --output tsv 2>$null

    if ($url) {
        $workflowUrls[$workflow] = "$url/api/$workflow/triggers/manual/invoke?api-version=2022-05-01"
    }
}

# Update main-dispatcher and main-orchestrator parameters with resolved URLs
Write-Host ""
Write-Host "Updating workflow parameters with resolved URLs..." -ForegroundColor Yellow

$dispatcherParams = @{
    subaccountManagementUrl    = @{ value = $workflowUrls["subaccount-management"] }
    entitlementsManagementUrl  = @{ value = $workflowUrls["entitlements-management"] }
    provisioningManagementUrl  = @{ value = $workflowUrls["provisioning-management"] }
    authorizationManagementUrl = @{ value = $workflowUrls["authorization-management"] }
    destinationsManagementUrl  = @{ value = $workflowUrls["destinations-management"] }
    btp-mainOrchestratorUrl        = @{ value = $workflowUrls["main-orchestrator"] }
}

$orchestratorParams = @{
    subaccountManagementUrl    = @{ value = $workflowUrls["subaccount-management"] }
    entitlementsManagementUrl  = @{ value = $workflowUrls["entitlements-management"] }
    provisioningManagementUrl  = @{ value = $workflowUrls["provisioning-management"] }
    authorizationManagementUrl = @{ value = $workflowUrls["authorization-management"] }
}

$dispatcherParams  | ConvertTo-Json -Depth 5 | Set-Content -Path "../parameters/dispatcher-params-$Environment.json"  -Encoding utf8
$orchestratorParams | ConvertTo-Json -Depth 5 | Set-Content -Path "../parameters/orchestrator-params-$Environment.json" -Encoding utf8

Write-Host "  Parameter files written to ../parameters/" -ForegroundColor Gray
Write-Host "  Manually update workflow parameters in the Azure Portal with these values." -ForegroundColor Yellow

# Output deployment summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Deployment Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Resource Group:    $ResourceGroupName"    -ForegroundColor Cyan
Write-Host "Logic App Name:    $logicAppName"         -ForegroundColor Cyan
Write-Host "Storage Account:   $storageAccountName"   -ForegroundColor Cyan
Write-Host "App Insights:      $appInsightsName"      -ForegroundColor Cyan
Write-Host ""
Write-Host "Workflow Trigger URLs:" -ForegroundColor Yellow
foreach ($key in $workflowUrls.Keys) {
    Write-Host "  $key" -ForegroundColor Cyan
    Write-Host "    $($workflowUrls[$key])" -ForegroundColor Gray
}
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host "  1. Copy trigger URLs above into the main-dispatcher and main-orchestrator workflow parameters" -ForegroundColor Gray
Write-Host "  2. Obtain BTP CIS Central OAuth credentials (client_id / client_secret)" -ForegroundColor Gray
Write-Host "  3. Obtain XSUAA OAuth credentials for authorization operations" -ForegroundColor Gray
Write-Host "  4. Test using: POST https://<logicAppName>.azurewebsites.net/api/main-dispatcher/triggers/manual/invoke" -ForegroundColor Gray
Write-Host ""
Write-Host "Example dispatcher payload (full provision):" -ForegroundColor Yellow
Write-Host @'
{
  "resource": "fullProvision",
  "action": "provision",
  "cisCredentials": {
    "tokenUrl": "https://<subdomain>-ga.authentication.eu10.hana.ondemand.com/oauth/token",
    "clientId": "<CIS_CLIENT_ID>",
    "clientSecret": "<CIS_CLIENT_SECRET>"
  },
  "xsuaaCredentials": {
    "tokenUrl": "https://<subdomain>.authentication.us10.hana.ondemand.com/oauth/token",
    "clientId": "<XSUAA_CLIENT_ID>",
    "clientSecret": "<XSUAA_CLIENT_SECRET>",
    "baseUrl":  "https://<subdomain>.authentication.us10.hana.ondemand.com"
  },
  "btpConfig": {
    "globalAccountGuid":     "<GLOBAL_ACCOUNT_GUID>",
    "globalAccountSubdomain": "<GLOBAL_ACCOUNT_SUBDOMAIN>",
    "accountsServiceUrl":    "https://accounts-service.cfapps.eu10.hana.ondemand.com",
    "entitlementsServiceUrl":"https://entitlements-service.cfapps.eu10.hana.ondemand.com",
    "provisioningServiceUrl":"https://provisioning-service.cfapps.eu10.hana.ondemand.com"
  },
  "config": {
    "subaccount": {
      "guid": "",
      "displayName": "my-subaccount",
      "subdomain": "my-subaccount",
      "region": "us10",
      "description": "Provisioned via Azure Logic App"
    },
    "entitlements": [
      { "serviceName": "destination",  "planName": "lite" },
      { "serviceName": "connectivity", "planName": "lite" }
    ],
    "environment": {
      "type": "cloudfoundry",
      "name": "my-cf-env",
      "planName": "standard",
      "serviceName": "cloudfoundry"
    },
    "roleCollections": [
      {
        "name": "BTP-Developers",
        "description": "Developer access",
        "roles": [],
        "users": ["developer@example.com"]
      }
    ]
  }
}
'@ -ForegroundColor Gray
