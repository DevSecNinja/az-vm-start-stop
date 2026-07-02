// Deploys the tag-driven VM auto-start Function App into a resource group and
// grants its managed identity the rights needed to start VMs.
//
// Scope: subscription (creates/uses a resource group and, by default, assigns
// Virtual Machine Contributor at subscription scope so the function can start
// any VM in the subscription).
targetScope = 'subscription'

@description('Azure region for all resources.')
param location string

@description('Name of the resource group to create/use for the function resources.')
param resourceGroupName string

@description('Prefix for generated resource names (3-11 lowercase alphanumeric chars).')
@minLength(3)
@maxLength(11)
param namePrefix string = 'azvmstart'

@description('Timer cadence as a 6-field NCRONTAB expression. Default: every 5 minutes.')
param scheduleExpression string = '0 */5 * * * *'

@description('Default time zone used when a VM has no AutoStartTimeZone tag.')
param defaultTimeZone string = 'Europe/Amsterdam'

@description('Look-back window (minutes) for the first run; should match the timer cadence.')
@minValue(1)
@maxValue(1440)
param scheduleWindowMinutes int = 5

@description('When true, evaluate and log without starting any VM.')
param dryRun bool = false

@description('Optional subscription ids to scan. Empty = the default subscription of the function identity.')
param subscriptionIds array = []

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
}

module functionApp 'functionApp.bicep' = {
  name: 'functionApp'
  scope: rg
  params: {
    location: location
    namePrefix: namePrefix
    scheduleExpression: scheduleExpression
    defaultTimeZone: defaultTimeZone
    scheduleWindowMinutes: scheduleWindowMinutes
    dryRun: dryRun
    subscriptionIds: subscriptionIds
  }
}

// Virtual Machine Contributor lets the identity start (and stop) VMs.
var vmContributorRoleId = '9980e02c-c2be-4d73-94e8-173b1dc7cf3c'

resource vmContributorAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription().id, resourceGroupName, vmContributorRoleId)
  properties: {
    principalId: functionApp.outputs.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', vmContributorRoleId)
  }
}

output functionAppName string = functionApp.outputs.functionAppName
output functionAppPrincipalId string = functionApp.outputs.principalId
