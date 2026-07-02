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

@description('Prefix for generated resource names (3-11 lowercase alphanumeric chars).')
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

@description('Optional subscription ids to scan. Empty = the resource group subscription.')
param subscriptionIds array = []

var suffix = uniqueString(resourceGroup().id)
var storageAccountName = toLower('${namePrefix}${substring(suffix, 0, 8)}')
var functionAppName = '${namePrefix}-func-${substring(suffix, 0, 6)}'
var planName = '${namePrefix}-plan-${substring(suffix, 0, 6)}'
var identityName = '${namePrefix}-id-${substring(suffix, 0, 6)}'
var logAnalyticsName = '${namePrefix}-law-${substring(suffix, 0, 6)}'
var appInsightsName = '${namePrefix}-ai-${substring(suffix, 0, 6)}'
var deploymentContainerName = 'app-package'

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
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

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
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
    name: 'AutoSchedule:DefaultTimeZone'
    value: defaultTimeZone
  }
  {
    name: 'AutoSchedule:ScheduleWindowMinutes'
    value: string(scheduleWindowMinutes)
  }
  {
    name: 'AutoSchedule:DryRun'
    value: string(dryRun)
  }
]

var subscriptionIdSettings = [for (id, i) in subscriptionIds: {
  name: 'AutoSchedule:SubscriptionIds:${i}'
  value: id
}]

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
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
