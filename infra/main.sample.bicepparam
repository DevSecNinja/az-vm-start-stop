using 'main.bicep'

param location = 'westeurope'
param resourceGroupName = 'rg-az-vm-start'
param namePrefix = 'azvmstart'
param scheduleExpression = '0 */5 * * * *'
param defaultTimeZone = 'Europe/Amsterdam'
param scheduleWindowMinutes = 5
param dryRun = false
param subscriptionIds = []
