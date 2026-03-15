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

@description('Application Gateway WAF mode. Use Detection in dev to avoid blocking legitimate prompt text.')
@allowed([
  'Detection'
  'Prevention'
])
param appGatewayWafMode string = 'Detection'

@description('App Service VNet integration subnet resource ID.')
param appSubnetId string

@description('Application Gateway dedicated subnet resource ID.')
param appGatewaySubnetId string

@description('Private endpoint subnet resource ID.')
param privateEndpointSubnetId string

@description('Private DNS zone resource ID for App Service private endpoint resolution.')
param appServicePrivateDnsZoneId string

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

var appServicePlanName = 'asp-${namePrefix}'
var appServiceName = 'app-${namePrefix}-${uniqueSuffix}'
var appInsightsName = 'appi-${namePrefix}'
var logAnalyticsName = 'log-${namePrefix}'
var actionGroupName = 'ag-${namePrefix}-ops'

var appGatewayName = 'agw-${namePrefix}'
var appGatewayPublicIpName = 'pip-${namePrefix}-agw'
var frontendIpConfigName = 'feip-public'
var frontendPortName = 'feport-80'
var backendPoolName = 'be-appservice'
var backendHttpSettingsName = 'be-https'
var probeName = 'probe-health'
var httpListenerName = 'listener-http'
var requestRoutingRuleName = 'rule-default'

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
    publicNetworkAccess: 'Disabled'
    virtualNetworkSubnetId: appSubnetId
    siteConfig: {
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

resource appServicePrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = {
  name: 'pep-${appService.name}'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'appServiceConnection'
        properties: {
          privateLinkServiceId: appService.id
          groupIds: [
            'sites'
          ]
        }
      }
    ]
  }
}

resource appServicePrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = {
  parent: appServicePrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'appsvc-zone'
        properties: {
          privateDnsZoneId: appServicePrivateDnsZoneId
        }
      }
    ]
  }
}

resource appGatewayPublicIp 'Microsoft.Network/publicIPAddresses@2023-09-01' = {
  name: appGatewayPublicIpName
  location: location
  tags: tags
  sku: {
    name: 'Standard'
  }
  properties: {
    publicIPAllocationMethod: 'Static'
    publicIPAddressVersion: 'IPv4'
  }
}

resource appGateway 'Microsoft.Network/applicationGateways@2023-09-01' = {
  name: appGatewayName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'WAF_v2'
      tier: 'WAF_v2'
    }
    autoscaleConfiguration: {
      minCapacity: 1
      maxCapacity: 2
    }
    gatewayIPConfigurations: [
      {
        name: 'gwipc-main'
        properties: {
          subnet: {
            id: appGatewaySubnetId
          }
        }
      }
    ]
    frontendIPConfigurations: [
      {
        name: frontendIpConfigName
        properties: {
          publicIPAddress: {
            id: appGatewayPublicIp.id
          }
        }
      }
    ]
    frontendPorts: [
      {
        name: frontendPortName
        properties: {
          port: 80
        }
      }
    ]
    backendAddressPools: [
      {
        name: backendPoolName
        properties: {
          backendAddresses: [
            {
              fqdn: appService.properties.defaultHostName
            }
          ]
        }
      }
    ]
    backendHttpSettingsCollection: [
      {
        name: backendHttpSettingsName
        properties: {
          protocol: 'Https'
          port: 443
          requestTimeout: 30
          hostName: appService.properties.defaultHostName
          pickHostNameFromBackendAddress: false
          probe: {
            id: resourceId('Microsoft.Network/applicationGateways/probes', appGatewayName, probeName)
          }
        }
      }
    ]
    probes: [
      {
        name: probeName
        properties: {
          protocol: 'Https'
          path: '/health'
          interval: 30
          timeout: 30
          unhealthyThreshold: 3
          pickHostNameFromBackendHttpSettings: true
          match: {
            statusCodes: [
              '200-399'
            ]
          }
        }
      }
    ]
    httpListeners: [
      {
        name: httpListenerName
        properties: {
          frontendIPConfiguration: {
            id: resourceId('Microsoft.Network/applicationGateways/frontendIPConfigurations', appGatewayName, frontendIpConfigName)
          }
          frontendPort: {
            id: resourceId('Microsoft.Network/applicationGateways/frontendPorts', appGatewayName, frontendPortName)
          }
          protocol: 'Http'
        }
      }
    ]
    requestRoutingRules: [
      {
        name: requestRoutingRuleName
        properties: {
          ruleType: 'Basic'
          priority: 100
          httpListener: {
            id: resourceId('Microsoft.Network/applicationGateways/httpListeners', appGatewayName, httpListenerName)
          }
          backendAddressPool: {
            id: resourceId('Microsoft.Network/applicationGateways/backendAddressPools', appGatewayName, backendPoolName)
          }
          backendHttpSettings: {
            id: resourceId('Microsoft.Network/applicationGateways/backendHttpSettingsCollection', appGatewayName, backendHttpSettingsName)
          }
        }
      }
    ]
    webApplicationFirewallConfiguration: {
      enabled: true
      firewallMode: appGatewayWafMode
      ruleSetType: 'OWASP'
      ruleSetVersion: '3.2'
    }
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
output appGatewayName string = appGateway.name
output appGatewayPublicIpAddress string = appGatewayPublicIp.properties.ipAddress
