targetScope = 'resourceGroup'

@description('Primary Azure region for this environment.')
param location string

@description('Environment short name.')
param environment string

@description('Workload/application short name.')
param workloadName string

@description('CAF-style naming prefix.')
param namePrefix string

@description('Optional suffix for Application Gateway resources to support parallel replacement cutovers (for example: v1).')
@maxLength(20)
param appGatewayResourceSuffix string = ''

@description('Alert email receiver for action groups.')
param alertEmail string

@description('Application Gateway SKU name. Use Basic/Standard_v2 for modern SKUs, or Standard_Small/Standard_Medium/Standard_Large for legacy v1.')
@allowed([
  'Basic'
  'Standard_Small'
  'Standard_Medium'
  'Standard_Large'
  'Standard_v2'
])
param appGatewaySkuName string = 'Standard_v2'

@description('Application Gateway SKU tier. Keep aligned with appGatewaySkuName.')
@allowed([
  'Basic'
  'Standard'
  'Standard_v2'
])
param appGatewaySkuTier string = 'Standard_v2'

@description('Application Gateway instance count for legacy v1 SKUs. Ignored for Basic and Standard_v2.')
@minValue(1)
@maxValue(32)
param appGatewayInstanceCount int = 1

@description('Application Gateway autoscale minimum capacity for Standard_v2.')
@minValue(0)
@maxValue(125)
param appGatewayAutoscaleMinCapacity int = 1

@description('Application Gateway autoscale maximum capacity for Standard_v2.')
@minValue(1)
@maxValue(125)
param appGatewayAutoscaleMaxCapacity int = 2

@description('Application Gateway backend request timeout in seconds.')
@minValue(1)
@maxValue(86400)
param appGatewayRequestTimeoutSeconds int = 120

@description('Enable JWT bearer authentication in backend API runtime settings.')
param authEnabled bool = false

@description('JWT authority URL for token validation.')
param jwtAuthority string = ''

@description('JWT audience expected by the API.')
param jwtAudience string = ''

@description('Optional public client ID used by CLI device-code login.')
param jwtInteractiveClientId string = ''

@description('PFX certificate for Application Gateway HTTPS listener (base64-encoded).')
@secure()
param appGatewaySslCertificatePfxBase64 string = ''

@description('Password for Application Gateway HTTPS listener certificate.')
@secure()
param appGatewaySslCertificatePassword string = ''

@description('Optional public DNS host name for the Application Gateway HTTPS listener (for example: api-dev.example.com).')
param appGatewayPublicHostName string = ''

@description('Predefined Application Gateway TLS policy name used when HTTPS listener is enabled.')
param appGatewaySslPolicyName string = 'AppGwSslPolicy20220101S'

@description('App Service VNet integration subnet resource ID.')
param appSubnetId string

@description('Application Gateway dedicated subnet resource ID.')
param appGatewaySubnetId string

@description('Private endpoint subnet resource ID.')
param privateEndpointSubnetId string

@description('Private DNS zone resource ID for App Service private endpoint resolution.')
param appServicePrivateDnsZoneId string

@description('PostgreSQL server name (without DNS suffix).')
param postgresServerName string

@description('Redis host name.')
param redisHostName string

@description('Key Vault secret URI for OpenAI API key.')
param openAiSecretUri string

@description('Key Vault secret URI for Anthropic API key.')
param anthropicSecretUri string

@description('Key Vault secret URI for Google API key.')
param googleSecretUri string

@description('Key Vault secret URI for DeepSeek API key.')
param deepSeekSecretUri string

@description('Key Vault secret URI for Blob Storage connection string.')
param blobStorageConnectionStringSecretUri string = ''

@description('Blob container name used for session attachments.')
param attachmentContainerName string = 'session-attachments'

@description('Document Intelligence endpoint for attachment extraction.')
param documentIntelligenceEndpoint string = ''

@description('Document Intelligence model ID used by backend attachment extraction.')
param documentIntelligenceModelId string = 'prebuilt-layout'

@description('Blob service endpoint used by backend attachment storage (managed identity mode).')
param attachmentStorageBlobEndpoint string = ''

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

var appGatewayResourceSuffixSegment = empty(appGatewayResourceSuffix) ? '' : '-${appGatewayResourceSuffix}'
var appGatewayName = 'agw-${namePrefix}${appGatewayResourceSuffixSegment}'
var frontendIpConfigName = 'feip-public'
var frontendHttpPortName = 'feport-80'
var frontendHttpsPortName = 'feport-443'
var backendPoolName = 'be-appservice'
var backendHttpSettingsName = 'be-https'
var probeName = 'probe-health'
var httpListenerName = 'listener-http'
var httpsListenerName = 'listener-https'
var requestRoutingRuleName = 'rule-default'
var httpsRequestRoutingRuleName = 'rule-https-default'
var httpRedirectRuleName = 'rule-http-redirect'
var httpToHttpsRedirectName = 'redirect-http-to-https'
var appGatewaySslCertificateName = 'agw-cert'
var enableHttpsListener = !empty(appGatewaySslCertificatePfxBase64) && !empty(appGatewaySslCertificatePassword)
var hasPublicHostName = !empty(appGatewayPublicHostName)
var httpsListenerAdditionalProperties = hasPublicHostName
  ? {
      hostName: appGatewayPublicHostName
    }
  : {}
var normalizedAppGatewaySkuTier = toLower(appGatewaySkuTier)
var isModernAppGatewaySku = endsWith(normalizedAppGatewaySkuTier, '_v2')
var isLegacyV1Sku = !isModernAppGatewaySku
var appGatewayPublicIpName = isModernAppGatewaySku
  ? 'pip-${namePrefix}-agw${appGatewayResourceSuffixSegment}'
  : 'pip-${namePrefix}-agw-v1${appGatewayResourceSuffixSegment}'
var appGatewaySku = isLegacyV1Sku
  ? {
      name: appGatewaySkuName
      tier: appGatewaySkuTier
      capacity: appGatewayInstanceCount
    }
  : {
      name: appGatewaySkuName
      tier: appGatewaySkuTier
    }
var appGatewayPublicIpSkuName = isModernAppGatewaySku ? 'Standard' : 'Basic'
var appGatewayPublicIpAllocationMethod = isModernAppGatewaySku ? 'Static' : 'Dynamic'
var frontendPorts = enableHttpsListener
  ? [
      {
        name: frontendHttpPortName
        properties: {
          port: 80
        }
      }
      {
        name: frontendHttpsPortName
        properties: {
          port: 443
        }
      }
    ]
  : [
      {
        name: frontendHttpPortName
        properties: {
          port: 80
        }
      }
    ]
var sslCertificates = enableHttpsListener
  ? [
      {
        name: appGatewaySslCertificateName
        properties: {
          data: appGatewaySslCertificatePfxBase64
          password: appGatewaySslCertificatePassword
        }
      }
    ]
  : []
var httpListeners = enableHttpsListener
  ? [
      {
        name: httpListenerName
        properties: {
          frontendIPConfiguration: {
            id: resourceId('Microsoft.Network/applicationGateways/frontendIPConfigurations', appGatewayName, frontendIpConfigName)
          }
          frontendPort: {
            id: resourceId('Microsoft.Network/applicationGateways/frontendPorts', appGatewayName, frontendHttpPortName)
          }
          protocol: 'Http'
        }
      }
      {
        name: httpsListenerName
        properties: union(
          {
            frontendIPConfiguration: {
              id: resourceId('Microsoft.Network/applicationGateways/frontendIPConfigurations', appGatewayName, frontendIpConfigName)
            }
            frontendPort: {
              id: resourceId('Microsoft.Network/applicationGateways/frontendPorts', appGatewayName, frontendHttpsPortName)
            }
            protocol: 'Https'
            sslCertificate: {
              id: resourceId('Microsoft.Network/applicationGateways/sslCertificates', appGatewayName, appGatewaySslCertificateName)
            }
          },
          httpsListenerAdditionalProperties
        )
      }
    ]
  : [
      {
        name: httpListenerName
        properties: {
          frontendIPConfiguration: {
            id: resourceId('Microsoft.Network/applicationGateways/frontendIPConfigurations', appGatewayName, frontendIpConfigName)
          }
          frontendPort: {
            id: resourceId('Microsoft.Network/applicationGateways/frontendPorts', appGatewayName, frontendHttpPortName)
          }
          protocol: 'Http'
        }
      }
    ]
var redirectConfigurations = enableHttpsListener
  ? [
      {
        name: httpToHttpsRedirectName
        properties: {
          redirectType: 'Permanent'
          targetListener: {
            id: resourceId('Microsoft.Network/applicationGateways/httpListeners', appGatewayName, httpsListenerName)
          }
          includePath: true
          includeQueryString: true
        }
      }
    ]
  : []
var httpRedirectRuleProperties = {
  ruleType: 'Basic'
  httpListener: {
    id: resourceId('Microsoft.Network/applicationGateways/httpListeners', appGatewayName, httpListenerName)
  }
  redirectConfiguration: {
    id: resourceId('Microsoft.Network/applicationGateways/redirectConfigurations', appGatewayName, httpToHttpsRedirectName)
  }
  priority: 90
}
var httpsRequestRoutingRuleProperties = {
  ruleType: 'Basic'
  httpListener: {
    id: resourceId('Microsoft.Network/applicationGateways/httpListeners', appGatewayName, httpsListenerName)
  }
  backendAddressPool: {
    id: resourceId('Microsoft.Network/applicationGateways/backendAddressPools', appGatewayName, backendPoolName)
  }
  backendHttpSettings: {
    id: resourceId('Microsoft.Network/applicationGateways/backendHttpSettingsCollection', appGatewayName, backendHttpSettingsName)
  }
  priority: 100
}
var defaultRequestRoutingRuleProperties = {
  ruleType: 'Basic'
  httpListener: {
    id: resourceId('Microsoft.Network/applicationGateways/httpListeners', appGatewayName, httpListenerName)
  }
  backendAddressPool: {
    id: resourceId('Microsoft.Network/applicationGateways/backendAddressPools', appGatewayName, backendPoolName)
  }
  backendHttpSettings: {
    id: resourceId('Microsoft.Network/applicationGateways/backendHttpSettingsCollection', appGatewayName, backendHttpSettingsName)
  }
  priority: 100
}
var requestRoutingRules = enableHttpsListener
  ? [
      {
        name: httpRedirectRuleName
        properties: httpRedirectRuleProperties
      }
      {
        name: httpsRequestRoutingRuleName
        properties: httpsRequestRoutingRuleProperties
      }
    ]
  : [
      {
        name: requestRoutingRuleName
        properties: defaultRequestRoutingRuleProperties
      }
    ]

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
          name: 'Database__PostgreSql__UseManagedIdentity'
          value: 'true'
        }
        {
          name: 'Database__PostgreSql__Host'
          value: '${postgresServerName}.postgres.database.azure.com'
        }
        {
          name: 'Database__PostgreSql__Port'
          value: '5432'
        }
        {
          name: 'Database__PostgreSql__Database'
          value: 'agon'
        }
        {
          name: 'Database__PostgreSql__Username'
          value: appServiceName
        }
        {
          name: 'Redis__UseManagedIdentity'
          value: 'true'
        }
        {
          name: 'Redis__Host'
          value: redisHostName
        }
        {
          name: 'Redis__Port'
          value: '6380'
        }
        {
          name: 'Storage__UseManagedIdentity'
          value: 'true'
        }
        {
          name: 'Storage__AttachmentBlobServiceUri'
          value: attachmentStorageBlobEndpoint
        }
        {
          name: 'Storage__AttachmentContainer'
          value: attachmentContainerName
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
        {
          name: 'BLOB_STORAGE_CONNECTION_STRING'
          value: empty(blobStorageConnectionStringSecretUri) ? '' : '@Microsoft.KeyVault(SecretUri=${blobStorageConnectionStringSecretUri})'
        }
        {
          name: 'AttachmentProcessing__DocumentIntelligence__Enabled'
          value: empty(documentIntelligenceEndpoint) ? 'false' : 'true'
        }
        {
          name: 'AttachmentProcessing__DocumentIntelligence__Endpoint'
          value: documentIntelligenceEndpoint
        }
        {
          name: 'AttachmentProcessing__DocumentIntelligence__ModelId'
          value: documentIntelligenceModelId
        }
        {
          name: 'AttachmentProcessing__DocumentIntelligence__UseManagedIdentity'
          value: 'true'
        }
        {
          name: 'Authentication__Enabled'
          value: authEnabled ? 'true' : 'false'
        }
        {
          name: 'Authentication__AzureAd__Authority'
          value: jwtAuthority
        }
        {
          name: 'Authentication__AzureAd__Audience'
          value: jwtAudience
        }
        {
          name: 'Authentication__AzureAd__InteractiveClientId'
          value: jwtInteractiveClientId
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

var appGatewayBaseProperties = {
  sku: appGatewaySku
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
  frontendPorts: frontendPorts
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
        requestTimeout: appGatewayRequestTimeoutSeconds
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
  sslCertificates: sslCertificates
  httpListeners: httpListeners
  redirectConfigurations: redirectConfigurations
  requestRoutingRules: requestRoutingRules
}
var appGatewayAutoscaleProperties = endsWith(normalizedAppGatewaySkuTier, '_v2')
  ? {
      autoscaleConfiguration: {
        minCapacity: appGatewayAutoscaleMinCapacity
        maxCapacity: appGatewayAutoscaleMaxCapacity
      }
    }
  : {}
var appGatewaySslPolicyProperties = enableHttpsListener
  ? {
      sslPolicy: {
        policyType: 'Predefined'
        policyName: appGatewaySslPolicyName
      }
    }
  : {}
var appGatewayProperties = union(appGatewayBaseProperties, appGatewayAutoscaleProperties, appGatewaySslPolicyProperties)

resource appGatewayPublicIp 'Microsoft.Network/publicIPAddresses@2023-09-01' = {
  name: appGatewayPublicIpName
  location: location
  tags: tags
  sku: {
    name: appGatewayPublicIpSkuName
  }
  properties: {
    publicIPAllocationMethod: appGatewayPublicIpAllocationMethod
    publicIPAddressVersion: 'IPv4'
  }
}

resource appGateway 'Microsoft.Network/applicationGateways@2023-09-01' = {
  name: appGatewayName
  location: location
  tags: tags
  properties: appGatewayProperties
}

resource appGatewayDiagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'diag-${appGatewayName}'
  scope: appGateway
  properties: {
    workspaceId: logAnalytics.id
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
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
output appGatewayName string = appGateway.name
output appGatewayPublicIpAddress string = appGatewayPublicIp.properties.ipAddress
output appGatewayPreferredApiUrl string = enableHttpsListener && hasPublicHostName
  ? 'https://${appGatewayPublicHostName}'
  : 'http://${appGatewayPublicIp.properties.ipAddress}'
