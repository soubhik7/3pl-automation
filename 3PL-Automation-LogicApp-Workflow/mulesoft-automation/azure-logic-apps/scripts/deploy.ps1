# Azure Logic Apps Deployment Script for MuleSoft 3PL Onboarding Automation
# Deploys all MuleSoft Logic App workflows to an Azure Standard Logic App instance
#
# Prerequisites:
#   - Azure CLI installed and authenticated (az login)
#   - Subscription with Logic Apps Standard + Storage Account permissions
#   - Deployed 3pl-automation web app (for YAML patch + translation steps)
#
# Usage:
#   .\deploy.ps1 `
#     -ResourceGroupName "mars-3pl-rg" `
#     -Location "westeurope" `
#     -SubscriptionId "<your-sub-id>" `
#     -MuleWebAppUrl "https://3pl-onboarding.azurewebsites.net" `
#     -Environment "dev"

param(
    [Parameter(Mandatory=$true)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory=$true)]
    [string]$Location,

    [Parameter(Mandatory=$true)]
    [string]$SubscriptionId,

    [Parameter(Mandatory=$true)]
    [string]$MuleWebAppUrl,

    [Parameter(Mandatory=$false)]
    [string]$Environment = "dev"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "MuleSoft 3PL Onboarding - Azure Logic Apps Deployment" -ForegroundColor Cyan
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
$storageAccountName = "mule3plautomation$Environment"
az storage account create `
    --name $storageAccountName `
    --resource-group $ResourceGroupName `
    --location $Location `
    --sku Standard_LRS `
    --kind StorageV2

# Deploy Application Insights
Write-Host ""
Write-Host "Deploying Application Insights..." -ForegroundColor Yellow
$appInsightsName = "mule-3pl-insights-$Environment"
az monitor app-insights component create `
    --app $appInsightsName `
    --location $Location `
    --resource-group $ResourceGroupName `
    --application-type web

# Deploy Logic App (Standard)
Write-Host ""
Write-Host "Deploying Logic App (Standard)..." -ForegroundColor Yellow
$logicAppName = "mulesoft-automation-$Environment"
$appServicePlanName = "mule-3pl-asp-$Environment"

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
        "WORKFLOWS_LOCATION_NAME=$Location" `
        "MULE_WEB_APP_URL=$MuleWebAppUrl" `
        "GITHUB_API_BASE_URL=https://api.github.com"

# Deploy workflows — child workflows first, then orchestrator + dispatcher
Write-Host ""
Write-Host "Deploying MuleSoft 3PL workflows..." -ForegroundColor Yellow

$workflows = @(
    "mule-branch-management",
    "mule-pr-management",
    "yaml-patch-management",
    "translation-management",
    "main-orchestrator",
    "main-dispatcher"
)

$workflowUrls = @{}

foreach ($workflow in $workflows) {
    Write-Host "  - Deploying $workflow..." -ForegroundColor Gray

    $workflowPath = "..\workflows\$workflow.json"

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

# Build parameter files for orchestrator and dispatcher
Write-Host ""
Write-Host "Writing resolved parameter files..." -ForegroundColor Yellow

$orchestratorParams = @{
    muleWebAppUrl           = @{ value = $MuleWebAppUrl }
    muleBranchManagementUrl = @{ value = $workflowUrls["mule-branch-management"] }
    yamlPatchManagementUrl  = @{ value = $workflowUrls["yaml-patch-management"] }
    translationManagementUrl= @{ value = $workflowUrls["translation-management"] }
    mulePrManagementUrl     = @{ value = $workflowUrls["mule-pr-management"] }
}

$dispatcherParams = @{
    yamlPatchManagementUrl  = @{ value = $workflowUrls["yaml-patch-management"] }
    translationManagementUrl= @{ value = $workflowUrls["translation-management"] }
    muleBranchManagementUrl = @{ value = $workflowUrls["mule-branch-management"] }
    mulePrManagementUrl     = @{ value = $workflowUrls["mule-pr-management"] }
    mainOrchestratorUrl     = @{ value = $workflowUrls["main-orchestrator"] }
}

$yamlPatchParams = @{
    muleWebAppUrl    = @{ value = $MuleWebAppUrl }
    githubApiBaseUrl = @{ value = "https://api.github.com" }
}

$translationParams = @{
    muleWebAppUrl = @{ value = $MuleWebAppUrl }
}

$orchestratorParams  | ConvertTo-Json -Depth 5 | Set-Content -Path "..\parameters\orchestrator-params-$Environment.json"  -Encoding utf8
$dispatcherParams    | ConvertTo-Json -Depth 5 | Set-Content -Path "..\parameters\dispatcher-params-$Environment.json"     -Encoding utf8
$yamlPatchParams     | ConvertTo-Json -Depth 5 | Set-Content -Path "..\parameters\yaml-patch-params-$Environment.json"     -Encoding utf8
$translationParams   | ConvertTo-Json -Depth 5 | Set-Content -Path "..\parameters\translation-params-$Environment.json"    -Encoding utf8

Write-Host "  Parameter files written to ..\parameters\" -ForegroundColor Gray
Write-Host "  Apply them in Azure Portal: Logic App -> Workflows -> {name} -> Parameters" -ForegroundColor Yellow

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
Write-Host "3PL Web App URL:   $MuleWebAppUrl"        -ForegroundColor Cyan
Write-Host ""
Write-Host "Workflow Trigger URLs:" -ForegroundColor Yellow
foreach ($key in $workflowUrls.Keys) {
    Write-Host "  $key" -ForegroundColor Cyan
    Write-Host "    $($workflowUrls[$key])" -ForegroundColor Gray
}
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host "  1. Apply resolved parameter files in the Azure Portal (see ..\parameters\)" -ForegroundColor Gray
Write-Host "  2. Obtain GitHub PAT with 'repo' scope for branch/PR operations (Steps 6, 9)" -ForegroundColor Gray
Write-Host "  3. Confirm MuleSoft repo owner + name with Sravani Kare" -ForegroundColor Gray
Write-Host "  4. Configure Teams incoming webhook URL in notificationConfig" -ForegroundColor Gray
Write-Host "  5. Test full onboarding via main-dispatcher using the example payload below" -ForegroundColor Gray
Write-Host ""
Write-Host "Example: Full MuleSoft onboarding via main-dispatcher:" -ForegroundColor Yellow
Write-Host @'
POST https://<logicAppName>.azurewebsites.net/api/main-dispatcher/triggers/manual/invoke
{
  "resource": "fullOnboarding",
  "onboardingInput": {
    "partner_comment": "Added France for SPL partner",
    "country_key":     "france",
    "country_code":    "fr",
    "created_by":      "Your Name",
    "nav": {
      "protocol":     "https",
      "host":         "azr-xxx1234.mars-ad.net",
      "port":         "7124",
      "username":     "SVC_UAT_MULESOFT",
      "domain":       "mars-ad.net",
      "company":      "Company(UAT - Royal Canin France)",
      "service":      "InterfaceWebServices",
      "soap_port":    "7124",
      "soap_path":    "/sop_fra_uat_01/WS/UAT - Royal Canin France/Codeunit/InterfaceWebServices?wsdl",
      "routing_code": "NAV_FM_IN_UAT_FRA",
      "use_common_cert": true,
      "transaction_types": {
        "sal_008": { "enabled": true,  "type": "SHIPMENT" },
        "sal_011": { "enabled": true,  "type": "SALES_RETURN_RECEIPT" },
        "pur_019": { "enabled": true,  "type": "RECADV" },
        "man_001": { "enabled": false, "type": "FINISHED_GOOD" },
        "inv_002": { "enabled": true,  "type": "RECLASSMENT" },
        "inv_003": { "enabled": true,  "type": "INVENTORY" },
        "inv_004": { "enabled": true,  "type": "STOCK_ADJUSTMENT" },
        "wms_004": { "enabled": false, "type": "TO_SHIP_CONFIRMATION" },
        "wms_005": { "enabled": false, "type": "TO_REC_CONFIRMATION" }
      }
    },
    "nav_tst": {
      "host":         "azr-xxx1234-tst.mars-ad.net",
      "company":      "Company(TST - Royal Canin France)",
      "soap_path":    "/sop_fra_tst_01/WS/TST - Royal Canin France/Codeunit/InterfaceWebServices?wsdl",
      "routing_code": "NAV_FM_IN_TST_FRA"
    },
    "translation": {
      "receiver_name": "petc-rc-navision-3plspl-sys",
      "message_types": ["despatchStock"],
      "source_destination_combinations": [
        { "value_from": "PLANT-FR1-PLANT-FR2", "value_to": "spl_001" }
      ],
      "uom_mappings": [
        { "value_from": "EA", "value_to": "UNIT" }
      ]
    }
  },
  "muleWebAppUrl": "https://3pl-onboarding.azurewebsites.net",
  "githubCredentials": { "pat": "<GITHUB_PAT>" },
  "repoConfig": {
    "owner": "<MULE_GITHUB_REPO_OWNER>",
    "repo":  "<MULE_GITHUB_REPO_NAME>",
    "appYamlPath":  "src/main/resources/app.yaml",
    "devYamlPath":  "src/main/resources/dev.yaml",
    "tstYamlPath":  "src/main/resources/tst.yaml",
    "prodYamlPath": "src/main/resources/prod.yaml"
  },
  "notificationConfig": {
    "channel": "teams",
    "teamsWebhookUrl": "<TEAMS_INCOMING_WEBHOOK_URL>",
    "reviewerDisplayName": "Sravani Kare"
  },
  "prReviewers": ["<GITHUB_USERNAME_OF_REVIEWER>"]
}
'@ -ForegroundColor Gray
