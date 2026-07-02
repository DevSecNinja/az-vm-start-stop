// Function App (Flex Consumption) with system-assigned identity, identity-based
// storage (no access keys), and workspace-based Application Insights.
targetScope = 'resourceGroup'

@description('Azure region for all resources.')
param location string

@description('Prefix for generated resource names (3-11 lowercase alphanumeric chars).')
@minLength(3)
@maxLength(11)
param namePrefix string

@description('Timer cadence as a 6-field NCRONTAB expression.')
param scheduleExpression string

@description('Default time zone used when a VM has no AutoStartTimeZone tag.')
param defaultTimeZone string

@description('Look-back window (minutes) for the first run.')
param scheduleWindowMinutes int

@description('When true, evaluate and log without starting any VM.')
param dryRun bool

@description('Optional subscription ids to scan.')
param subscriptionIds array

var suffix = uniqueString(resourceGroup().id)
var storageAccountName = toLower('${namePrefix}${substring(suffix, 0, 8)}')
var functionAppName = '${namePrefix}-func-${substring(suffix, 0, 6)}'
var planName = '${namePrefix}-plan-${substring(suffix, 0, 6)}'
var logAnalyticsName = '${namePrefix}-law-${substring(suffix, 0, 6)}'
var appInsightsName = '${namePrefix}-ai-${substring(suffix, 0, 6)}'
var deploymentContainerName = 'app-package'

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
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    value: appInsights.properties.ConnectionString
  }
  {
    name: 'ScheduleExpression'
    value: scheduleExpression
  }
  {
    name: 'AutoStart:DefaultTimeZone'
    value: defaultTimeZone
  }
  {
    name: 'AutoStart:ScheduleWindowMinutes'
    value: string(scheduleWindowMinutes)
  }
  {
    name: 'AutoStart:DryRun'
    value: string(dryRun)
  }
]

var subscriptionIdSettings = [for (id, i) in subscriptionIds: {
  name: 'AutoStart:SubscriptionIds:${i}'
  value: id
}]

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
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
            type: 'SystemAssignedIdentity'
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

// Identity-based access to storage for AzureWebJobsStorage and deployment container.
var storageBlobDataOwnerRoleId = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'

resource storageBlobAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionApp.id, storageBlobDataOwnerRoleId)
  scope: storage
  properties: {
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataOwnerRoleId)
  }
}

output functionAppName string = functionApp.name
output principalId string = functionApp.identity.principalId
