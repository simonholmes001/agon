targetScope = 'resourceGroup'

@description('Primary Azure region for this environment.')
param location string

@description('CAF-style naming prefix.')
param namePrefix string

@description('Application Gateway name to manage.')
param appGatewayName string

@description('Enable Application Gateway start/stop automation resources.')
param enableAppGatewayStartStopAutomation bool = true

@description('Create or update Network Contributor role assignment for the automation identity on the Application Gateway scope.')
param assignGatewayNetworkContributorRole bool = true

@description('Automation schedule timezone (IANA), for example Europe/Paris.')
param scheduleTimeZone string = 'Europe/Paris'

@description('Business timezone for weekend checks in runbooks (Windows timezone ID).')
param businessTimeZoneId string = 'Romance Standard Time'

@description('ISO 8601 start time for the daily start schedule.')
param startScheduleStartTime string = ''

@description('ISO 8601 start time for the daily stop schedule.')
param stopScheduleStartTime string = ''

@description('Base URI that hosts runbook scripts (must be reachable by Azure Automation).')
param runbookScriptsBaseUri string = 'https://raw.githubusercontent.com/simonholmes001/agon/main/infrastructure/automation/runbooks'

@description('UTC deployment timestamp used to derive safe schedule start times when not explicitly provided.')
param deploymentTimestampUtc string = utcNow('o')

var automationAccountName = 'aa-${namePrefix}'
var startRunbookName = 'Start-AppGateway'
var stopRunbookName = 'Stop-AppGateway'
var startScheduleName = 'start-weekday-morning'
var stopScheduleName = 'stop-weekday-evening'
var networkContributorRoleDefinitionId = '4d97b98b-1d4f-4787-a291-c67834d212e7'
var startRunbookJobScheduleGuid = guid(automationAccountName, startRunbookName, startScheduleName)
var stopRunbookJobScheduleGuid = guid(automationAccountName, stopRunbookName, stopScheduleName)
var computedStartScheduleStartTime = empty(startScheduleStartTime) ? dateTimeAdd(deploymentTimestampUtc, 'PT10M') : startScheduleStartTime
var computedStopScheduleStartTime = empty(stopScheduleStartTime) ? dateTimeAdd(deploymentTimestampUtc, 'PT20M') : stopScheduleStartTime

resource appGateway 'Microsoft.Network/applicationGateways@2023-09-01' existing = if (enableAppGatewayStartStopAutomation) {
  name: appGatewayName
}

resource automationAccount 'Microsoft.Automation/automationAccounts@2023-11-01' = if (enableAppGatewayStartStopAutomation) {
  name: automationAccountName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    sku: {
      name: 'Basic'
    }
  }
}

resource startRunbook 'Microsoft.Automation/automationAccounts/runbooks@2023-11-01' = if (enableAppGatewayStartStopAutomation) {
  parent: automationAccount
  name: startRunbookName
  location: location
  properties: {
    runbookType: 'PowerShell'
    logVerbose: false
    logProgress: false
    description: 'Starts ${appGatewayName} on business days'
    publishContentLink: {
      uri: '${runbookScriptsBaseUri}/Start-AppGateway.ps1'
      version: '1.0.0'
    }
  }
}

resource stopRunbook 'Microsoft.Automation/automationAccounts/runbooks@2023-11-01' = if (enableAppGatewayStartStopAutomation) {
  parent: automationAccount
  name: stopRunbookName
  location: location
  properties: {
    runbookType: 'PowerShell'
    logVerbose: false
    logProgress: false
    description: 'Stops ${appGatewayName} on business days'
    publishContentLink: {
      uri: '${runbookScriptsBaseUri}/Stop-AppGateway.ps1'
      version: '1.0.0'
    }
  }
}

resource startSchedule 'Microsoft.Automation/automationAccounts/schedules@2023-11-01' = if (enableAppGatewayStartStopAutomation) {
  parent: automationAccount
  name: startScheduleName
  properties: {
    description: 'Daily start trigger; runbook skips weekends'
    startTime: computedStartScheduleStartTime
    expiryTime: '9999-12-31T23:59:59+00:00'
    interval: 1
    frequency: 'Day'
    timeZone: scheduleTimeZone
  }
}

resource stopSchedule 'Microsoft.Automation/automationAccounts/schedules@2023-11-01' = if (enableAppGatewayStartStopAutomation) {
  parent: automationAccount
  name: stopScheduleName
  properties: {
    description: 'Daily stop trigger; runbook skips weekends'
    startTime: computedStopScheduleStartTime
    expiryTime: '9999-12-31T23:59:59+00:00'
    interval: 1
    frequency: 'Day'
    timeZone: scheduleTimeZone
  }
}

resource startJobSchedule 'Microsoft.Automation/automationAccounts/jobSchedules@2023-11-01' = if (enableAppGatewayStartStopAutomation) {
  parent: automationAccount
  name: startRunbookJobScheduleGuid
  properties: {
    runbook: {
      name: startRunbook.name
    }
    schedule: {
      name: startSchedule.name
    }
    parameters: {
      SubscriptionId: subscription().subscriptionId
      ResourceGroup: resourceGroup().name
      GatewayName: appGatewayName
      BusinessTimeZoneId: businessTimeZoneId
    }
  }
}

resource stopJobSchedule 'Microsoft.Automation/automationAccounts/jobSchedules@2023-11-01' = if (enableAppGatewayStartStopAutomation) {
  parent: automationAccount
  name: stopRunbookJobScheduleGuid
  properties: {
    runbook: {
      name: stopRunbook.name
    }
    schedule: {
      name: stopSchedule.name
    }
    parameters: {
      SubscriptionId: subscription().subscriptionId
      ResourceGroup: resourceGroup().name
      GatewayName: appGatewayName
      BusinessTimeZoneId: businessTimeZoneId
    }
  }
}

resource appGatewayNetworkContributorAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (enableAppGatewayStartStopAutomation && assignGatewayNetworkContributorRole) {
  scope: appGateway
  name: guid(appGateway.id, automationAccount.id, networkContributorRoleDefinitionId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', networkContributorRoleDefinitionId)
    principalId: automationAccount!.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output automationAccountName string = enableAppGatewayStartStopAutomation ? automationAccountName : ''
