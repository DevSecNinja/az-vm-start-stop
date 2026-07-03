// Deploys the tag-driven VM auto-start/stop Function App into a PRE-EXISTING
// resource group. Scope: resourceGroup — the deploying identity only needs
// Owner on this resource group (no subscription/management-group rights).
//
// The function runs as a USER-ASSIGNED managed identity created here. Because the
// function must start/stop VMs across other resource groups or subscriptions, an
// administrator grants that identity `Virtual Machine Contributor` at management
// group (or subscription) scope out-of-band — see README. That single higher-scope
// role assignment is the only privileged step; it is intentionally NOT performed by
// this deployment so the CI identity can stay limited to Owner on one RG.
targetScope = 'resourceGroup'

@description('Azure region for all resources. Defaults to the resource group location.')
param location string = resourceGroup().location

@description('Workload/application name used in Azure CAF resource names (e.g. azvmss). Hyphens are allowed for most types and stripped for the storage account name.')
@minLength(3)
@maxLength(11)
param namePrefix string = 'azvmss'

@description('Timer cadence as a 6-field NCRONTAB expression. Default: every 5 minutes.')
param scheduleExpression string = '0 */5 * * * *'

@description('Default time zone used when a VM has no per-action time zone tag.')
param defaultTimeZone string = 'Europe/Amsterdam'

@description('Look-back window (minutes) for the first run; should match the timer cadence.')
@minValue(1)
@maxValue(1440)
param scheduleWindowMinutes int = 5

@description('When true, evaluate and log without starting/stopping any VM.')
param dryRun bool = false

@description('Optional subscription ids to scan. Empty = all subscriptions accessible to the identity.')
param subscriptionIds array = []

// Microsoft Cloud Adoption Framework naming: <type-abbreviation>-<workload>-<token>.
// Abbreviations come from the standard Azure Developer CLI (azd) abbreviations.json;
// resourceToken keeps names deterministic and unique per resource group. Each name is
// shaped to its resource type's rules (e.g. storage: lowercase alphanumeric, <=24).
var abbrs = loadJsonContent('abbreviations.json')
var resourceToken = toLower(uniqueString(resourceGroup().id))
var sanitizedPrefix = toLower(replace(replace(namePrefix, '-', ''), '_', ''))

var identityName = '${abbrs.managedIdentityUserAssignedIdentities}${namePrefix}-${resourceToken}'
var storageAccountName = take('${abbrs.storageStorageAccounts}${sanitizedPrefix}${resourceToken}', 24)
var functionAppName = '${abbrs.webSitesFunctions}${namePrefix}-${resourceToken}'
var planName = '${abbrs.webServerFarms}${namePrefix}-${resourceToken}'
var logAnalyticsName = '${abbrs.operationalInsightsWorkspaces}${namePrefix}-${resourceToken}'
var appInsightsName = '${abbrs.insightsComponents}${namePrefix}-${resourceToken}'
var deploymentContainerName = 'app-package'

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  // Holds only the function's runtime state + deployment package; hardened below
  // with TLS1_2, HTTPS-only, identity-based access (no shared key), no public blob.
  //checkov:skip=CKV_AZURE_35:Default-deny network rules would block the function's identity-based access to its own storage; no VNet in this lean deployment.
  //checkov:skip=CKV_AZURE_206:Standard_LRS is sufficient for transient function state; geo/zone replication is unnecessary cost here.
  //checkov:skip=CKV_AZURE_43:Name is generated to valid rules (lowercase alphanumeric, <=24 chars); false positive.
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: deploymentContainerName
  properties: {
    publicAccess: 'None'
  }
}

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// Notifies subscription Owners (the admins) by email — via ARM role, so no
// email address is hardcoded.
resource alertActionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: '${abbrs.insightsActionGroups}${namePrefix}-${resourceToken}'
  location: 'global'
  properties: {
    groupShortName: take(sanitizedPrefix, 12)
    enabled: true
    armRoleReceivers: [
      {
        name: 'Subscription Owners'
        roleId: '8e3af657-a8ff-443c-a75c-2fe8c4bcb635' // Owner
        useCommonAlertSchema: true
      }
    ]
  }
}

// Near-real-time liveness alert: fire when the function has logged no
// "Schedule pass complete" trace in the last hour, i.e. it has stopped running.
resource noSchedulePassAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'sqr-${namePrefix}-no-schedule-pass'
  location: location
  properties: {
    displayName: '${namePrefix}: function not running (no schedule passes)'
    description: 'No "Schedule pass complete" trace in the last hour — the function may have stopped running.'
    severity: 1
    enabled: true
    scopes: [
      appInsights.id
    ]
    evaluationFrequency: 'PT15M'
    windowSize: 'PT1H'
    criteria: {
      allOf: [
        {
          query: 'traces | where message has "Schedule pass complete" | summarize count_ = count()'
          timeAggregation: 'Total'
          metricMeasureColumn: 'count_'
          operator: 'LessThan'
          threshold: 1
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    autoMitigate: true
    actions: {
      actionGroups: [
        alertActionGroup.id
      ]
    }
  }
}

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  //checkov:skip=CKV_AZURE_225:Single-purpose scheduler on Flex Consumption; zone redundancy is unnecessary cost for this workload.
  name: planName
  location: location
  kind: 'functionapp'
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  properties: {
    reserved: true
  }
}

var baseAppSettings = [
  {
    name: 'AzureWebJobsStorage__accountName'
    value: storage.name
  }
  {
    name: 'AzureWebJobsStorage__credential'
    value: 'managedidentity'
  }
  {
    name: 'AzureWebJobsStorage__clientId'
    value: identity.properties.clientId
  }
  {
    name: 'AZURE_CLIENT_ID'
    value: identity.properties.clientId
  }
  {
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    value: appInsights.properties.ConnectionString
  }
  {
    name: 'ScheduleExpression'
    value: scheduleExpression
  }
  {
    name: 'AutoSchedule__DefaultTimeZone'
    value: defaultTimeZone
  }
  {
    name: 'AutoSchedule__ScheduleWindowMinutes'
    value: string(scheduleWindowMinutes)
  }
  {
    name: 'AutoSchedule__DryRun'
    value: string(dryRun)
  }
]

var subscriptionIdSettings = [for (id, i) in subscriptionIds: {
  name: 'AutoSchedule__SubscriptionIds__${i}'
  value: id
}]

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  // Timer-triggered function on Flex Consumption: no inbound HTTP surface, and
  // TLS/FTP/HTTP-version/public-access are platform-managed on Flex. httpsOnly is set.
  //checkov:skip=CKV_AZURE_67:No HTTP trigger; HTTP/2 is irrelevant and platform-managed on Flex Consumption.
  //checkov:skip=CKV_AZURE_18:No HTTP trigger; HTTP version is platform-managed on Flex Consumption.
  //checkov:skip=CKV_AZURE_15:TLS is platform-managed on Flex Consumption (HTTPS enforced); no siteConfig.minTlsVersion knob.
  //checkov:skip=CKV_AZURE_17:No inbound HTTP clients (timer-only); client certificates are not applicable.
  //checkov:skip=CKV_AZURE_78:Flex Consumption deploys via OneDeploy (identity-based blob container), not FTP.
  //checkov:skip=CKV_AZURE_222:Disabling public network access requires private endpoints/VNet; out of scope for this lean deployment.
  //checkov:skip=CKV_AZURE_212:Flex Consumption autoscales; fixed min-instance failover is not applicable.
  //checkov:skip=CKV_AZURE_213:Health-check endpoints apply to HTTP apps; this function is timer-only with no HTTP surface.
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deploymentContainerName}'
          authentication: {
            type: 'UserAssignedIdentity'
            userAssignedIdentityResourceId: identity.id
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 40
        instanceMemoryMB: 2048
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '8.0'
      }
    }
    siteConfig: {
      appSettings: concat(baseAppSettings, subscriptionIdSettings)
    }
  }
}

// Identity-based access to storage for AzureWebJobsStorage and the deployment
// container. This assignment is within the RG, so RG Owner can create it.
var storageBlobDataOwnerRoleId = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'

resource storageBlobAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, identity.id, storageBlobDataOwnerRoleId)
  scope: storage
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataOwnerRoleId)
  }
}

output functionAppName string = functionApp.name

@description('Object (principal) id of the function user-assigned identity. Grant this Virtual Machine Contributor at management group / subscription scope.')
output identityPrincipalId string = identity.properties.principalId

@description('Client id of the function user-assigned identity.')
output identityClientId string = identity.properties.clientId

@description('Resource id of the function user-assigned identity.')
output identityResourceId string = identity.id
