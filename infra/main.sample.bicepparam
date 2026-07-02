using 'main.bicep'

param namePrefix = 'azvmss'
param scheduleExpression = '0 */5 * * * *'
param defaultTimeZone = 'Europe/Amsterdam'
param scheduleWindowMinutes = 5
param dryRun = false
// Scan additional subscriptions (the identity needs VM Contributor covering them,
// e.g. via a management-group role assignment). Empty = the RG's subscription.
param subscriptionIds = []
