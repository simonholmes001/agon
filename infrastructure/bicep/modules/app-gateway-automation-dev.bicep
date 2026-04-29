targetScope = 'resourceGroup'

@description('Primary Azure region for this environment.')
param location string

@description('CAF-style naming prefix.')
param namePrefix string

@description('Application Gateway name to manage.')
param appGatewayName string

@description('Enable Application Gateway start/stop automation resources.')
param enableAppGatewayStartStopAutomation bool = true

@description('Automation schedule timezone (IANA), for example Europe/Paris.')
param scheduleTimeZone string = 'Europe/Paris'

@description('Business timezone for weekend checks in runbooks (Windows timezone ID).')
param businessTimeZoneId string = 'Romance Standard Time'

@description('ISO 8601 start time for the daily start schedule.')
param startScheduleStartTime string = '2026-01-01T08:45:00+01:00'

@description('ISO 8601 start time for the daily stop schedule.')
param stopScheduleStartTime string = '2026-01-01T20:15:00+01:00'

@description('Base URI that hosts runbook scripts (must be reachable by Azure Automation).')
param runbookScriptsBaseUri string = 'https://raw.githubusercontent.com/simonholmes001/agon/main/infrastructure/automation/runbooks'

var automationAccountName = 'aa-${namePrefix}'
var startRunbookName = 'Start-AppGateway'
var stopRunbookName = 'Stop-AppGateway'
var startScheduleName = 'start-weekday-morning'
var stopScheduleName = 'stop-weekday-evening'
var networkContributorRoleDefinitionId = '4d97b98b-1d4f-4787-a291-c67834d212e7'
var startRunbookJobScheduleGuid = guid(automationAccountName, startRunbookName, startScheduleName)
var stopRunbookJobScheduleGuid = guid(automationAccountName, stopRunbookName, stopScheduleName)

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
    startTime: startScheduleStartTime
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
    startTime: stopScheduleStartTime
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

resource appGatewayNetworkContributorAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (enableAppGatewayStartStopAutomation) {
  scope: appGateway
  name: guid(appGateway.id, automationAccount.id, networkContributorRoleDefinitionId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', networkContributorRoleDefinitionId)
    principalId: automationAccount!.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output automationAccountName string = enableAppGatewayStartStopAutomation ? automationAccountName : ''
