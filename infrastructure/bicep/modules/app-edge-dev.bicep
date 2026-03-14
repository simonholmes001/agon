targetScope = 'resourceGroup'

@description('Primary Azure region for this environment.')
param location string

@description('Environment short name.')
param environment string

@description('Workload/application short name.')
param workloadName string

@description('CAF-style naming prefix.')
param namePrefix string

@description('Alert email receiver for action groups.')
param alertEmail string

@description('App Service VNet integration subnet resource ID.')
param appSubnetId string

@description('Key Vault secret URI for PostgreSQL connection string.')
param postgresConnectionSecretUri string

@description('Key Vault secret URI for Redis connection string.')
param redisConnectionSecretUri string

@description('Key Vault secret URI for OpenAI API key.')
param openAiSecretUri string

@description('Key Vault secret URI for Anthropic API key.')
param anthropicSecretUri string

@description('Key Vault secret URI for Google API key.')
param googleSecretUri string

@description('Key Vault secret URI for DeepSeek API key.')
param deepSeekSecretUri string

var tags = {
  environment: environment
  workload: workloadName
  managedBy: 'bicep'
}

var uniqueSuffix = take(uniqueString(subscription().id, resourceGroup().id, namePrefix, environment), 12)
var endpointPrefix = replace(namePrefix, '-', '')

var appServicePlanName = 'asp-${namePrefix}'
var appServiceName = 'app-${namePrefix}-${uniqueSuffix}'
var appInsightsName = 'appi-${namePrefix}'
var logAnalyticsName = 'log-${namePrefix}'
var actionGroupName = 'ag-${namePrefix}-ops'

var frontDoorProfileName = 'afd-${namePrefix}'
var frontDoorEndpointName = 'afd-${endpointPrefix}-${uniqueSuffix}'
var frontDoorOriginGroupName = 'og-default'
var frontDoorOriginName = 'app-origin'
var frontDoorRouteName = 'route-default'
var frontDoorWafPolicyName = 'waf-${namePrefix}'

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
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
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  tags: tags
  sku: {
    name: 'B1'
    tier: 'Basic'
    capacity: 1
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

resource appService 'Microsoft.Web/sites@2023-12-01' = {
  name: appServiceName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    publicNetworkAccess: 'Enabled'
    virtualNetworkSubnetId: appSubnetId
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|9.0'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      vnetRouteAllEnabled: true
      appSettings: [
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'ConnectionStrings__PostgreSQL'
          value: '@Microsoft.KeyVault(SecretUri=${postgresConnectionSecretUri})'
        }
        {
          name: 'ConnectionStrings__Redis'
          value: '@Microsoft.KeyVault(SecretUri=${redisConnectionSecretUri})'
        }
        {
          name: 'OPENAI_KEY'
          value: empty(openAiSecretUri) ? '' : '@Microsoft.KeyVault(SecretUri=${openAiSecretUri})'
        }
        {
          name: 'CLAUDE_KEY'
          value: empty(anthropicSecretUri) ? '' : '@Microsoft.KeyVault(SecretUri=${anthropicSecretUri})'
        }
        {
          name: 'GEMINI_KEY'
          value: empty(googleSecretUri) ? '' : '@Microsoft.KeyVault(SecretUri=${googleSecretUri})'
        }
        {
          name: 'DEEPSEEK_KEY'
          value: empty(deepSeekSecretUri) ? '' : '@Microsoft.KeyVault(SecretUri=${deepSeekSecretUri})'
        }
      ]
    }
  }
}

resource frontDoorProfile 'Microsoft.Cdn/profiles@2023-05-01' = {
  name: frontDoorProfileName
  location: 'global'
  tags: tags
  sku: {
    name: 'Premium_AzureFrontDoor'
  }
}

resource frontDoorWafPolicy 'Microsoft.Cdn/CdnWebApplicationFirewallPolicies@2023-05-01' = {
  name: frontDoorWafPolicyName
  location: 'global'
  sku: {
    name: 'Premium_AzureFrontDoor'
  }
  tags: tags
  properties: {
    policySettings: {
      enabledState: 'Enabled'
      mode: 'Prevention'
    }
    managedRules: {
      managedRuleSets: [
        {
          ruleSetType: 'Microsoft_DefaultRuleSet'
          ruleSetVersion: '2.1'
        }
        {
          ruleSetType: 'Microsoft_BotManagerRuleSet'
          ruleSetVersion: '1.0'
        }
      ]
    }
  }
}

resource frontDoorEndpoint 'Microsoft.Cdn/profiles/afdEndpoints@2023-05-01' = {
  parent: frontDoorProfile
  name: frontDoorEndpointName
  location: 'global'
  tags: tags
  properties: {
    enabledState: 'Enabled'
  }
}

resource frontDoorOriginGroup 'Microsoft.Cdn/profiles/originGroups@2023-05-01' = {
  parent: frontDoorProfile
  name: frontDoorOriginGroupName
  properties: {
    sessionAffinityState: 'Disabled'
    loadBalancingSettings: {
      sampleSize: 4
      successfulSamplesRequired: 3
      additionalLatencyInMilliseconds: 0
    }
    healthProbeSettings: {
      probeIntervalInSeconds: 120
      probePath: '/health'
      probeProtocol: 'Https'
      probeRequestType: 'HEAD'
    }
  }
}

resource frontDoorOrigin 'Microsoft.Cdn/profiles/originGroups/origins@2023-05-01' = {
  parent: frontDoorOriginGroup
  name: frontDoorOriginName
  properties: {
    hostName: appService.properties.defaultHostName
    originHostHeader: appService.properties.defaultHostName
    priority: 1
    weight: 1000
    enabledState: 'Enabled'
    httpsPort: 443
  }
}

resource frontDoorRoute 'Microsoft.Cdn/profiles/afdEndpoints/routes@2023-05-01' = {
  parent: frontDoorEndpoint
  name: frontDoorRouteName
  properties: {
    enabledState: 'Enabled'
    forwardingProtocol: 'HttpsOnly'
    httpsRedirect: 'Enabled'
    linkToDefaultDomain: 'Enabled'
    patternsToMatch: [
      '/*'
    ]
    supportedProtocols: [
      'Http'
      'Https'
    ]
    originGroup: {
      id: frontDoorOriginGroup.id
    }
  }
}

resource frontDoorSecurityPolicy 'Microsoft.Cdn/profiles/securityPolicies@2023-05-01' = {
  parent: frontDoorProfile
  name: 'sp-default'
  properties: {
    parameters: {
      type: 'WebApplicationFirewall'
      wafPolicy: {
        id: frontDoorWafPolicy.id
      }
      associations: [
        {
          domains: [
            {
              id: frontDoorEndpoint.id
            }
          ]
          patternsToMatch: [
            '/*'
          ]
        }
      ]
    }
  }
}

resource appWebConfig 'Microsoft.Web/sites/config@2023-12-01' = {
  parent: appService
  name: 'web'
  properties: {
    ipSecurityRestrictionsDefaultAction: 'Deny'
    scmIpSecurityRestrictionsUseMain: true
    ipSecurityRestrictions: [
      {
        name: 'Allow-Azure-FrontDoor'
        description: 'Allow only traffic coming through Azure Front Door with matching profile ID.'
        action: 'Allow'
        priority: 100
        tag: 'ServiceTag'
        ipAddress: 'AzureFrontDoor.Backend'
        headers: {
          'x-azure-fdid': [
            frontDoorProfile.properties.frontDoorId
          ]
        }
      }
    ]
  }
}

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: actionGroupName
  location: 'global'
  tags: tags
  properties: {
    groupShortName: 'agdevops'
    enabled: true
    emailReceivers: [
      {
        name: 'primary'
        emailAddress: alertEmail
        useCommonAlertSchema: true
      }
    ]
  }
}

resource appServiceAvailabilityAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-${namePrefix}-app-http5xx'
  location: 'global'
  tags: tags
  properties: {
    description: 'Alerts when App Service HTTP 5xx count rises.'
    severity: 2
    enabled: true
    scopes: [
      appService.id
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    targetResourceType: 'Microsoft.Web/sites'
    targetResourceRegion: location
    criteria: {
      allOf: [
        {
          name: 'http5xx-threshold'
          metricName: 'Http5xx'
          metricNamespace: 'Microsoft.Web/sites'
          operator: 'GreaterThan'
          threshold: 5
          timeAggregation: 'Total'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
    }
    actions: [
      {
        actionGroupId: actionGroup.id
      }
    ]
  }
}

output appServiceName string = appService.name
output appServiceDefaultHostName string = appService.properties.defaultHostName
output appPrincipalId string = appService.identity.principalId
output frontDoorEndpointHostName string = frontDoorEndpoint.properties.hostName
output frontDoorProfileId string = frontDoorProfile.id
