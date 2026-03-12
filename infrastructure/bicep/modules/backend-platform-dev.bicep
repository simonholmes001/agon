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

@description('PostgreSQL admin username used for bootstrap operations.')
param postgresAdminLogin string

@secure()
@description('PostgreSQL admin password used for bootstrap operations.')
param postgresAdminPassword string

@description('Microsoft Entra tenant ID used by PostgreSQL auth configuration.')
param tenantId string

@secure()
param openAiApiKey string

@secure()
param anthropicApiKey string

@secure()
param googleApiKey string

@secure()
param deepSeekApiKey string

var tags = {
  environment: environment
  workload: workloadName
  managedBy: 'bicep'
}

var appServicePlanName = 'asp-${namePrefix}'
var appServiceName = 'app-${namePrefix}-${uniqueString(resourceGroup().id)}'
var appInsightsName = 'appi-${namePrefix}'
var logAnalyticsName = 'log-${namePrefix}'
var keyVaultName = 'kv-${replace(namePrefix, '-', '')}-${take(uniqueString(resourceGroup().id), 6)}'
var vnetName = 'vnet-${namePrefix}'
var appSubnetName = 'snet-${namePrefix}-app'
var privateEndpointSubnetName = 'snet-${namePrefix}-pep'
var postgresSubnetName = 'snet-${namePrefix}-psql'
var postgresServerName = 'psql-${namePrefix}-${take(uniqueString(resourceGroup().id), 6)}'
var redisName = 'redis-${namePrefix}-${take(uniqueString(resourceGroup().id), 6)}'
var actionGroupName = 'ag-${namePrefix}-ops'

var keyVaultPrivateDnsZoneName = 'privatelink.vaultcore.azure.net'
var redisPrivateDnsZoneName = 'privatelink.redis.cache.windows.net'
var appServicePrivateDnsZoneName = 'privatelink.azurewebsites.net'
var postgresPrivateDnsZoneName = 'privatelink.postgres.database.azure.com'

var keyVaultSecretsUserRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')

resource vnet 'Microsoft.Network/virtualNetworks@2023-11-01' = {
  name: vnetName
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.42.0.0/16'
      ]
    }
    subnets: [
      {
        name: appSubnetName
        properties: {
          addressPrefix: '10.42.1.0/24'
          delegations: [
            {
              name: 'webappDelegation'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
        }
      }
      {
        name: privateEndpointSubnetName
        properties: {
          addressPrefix: '10.42.2.0/24'
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
      {
        name: postgresSubnetName
        properties: {
          addressPrefix: '10.42.3.0/24'
          delegations: [
            {
              name: 'postgresDelegation'
              properties: {
                serviceName: 'Microsoft.DBforPostgreSQL/flexibleServers'
              }
            }
          ]
        }
      }
    ]
  }
}

resource appSubnet 'Microsoft.Network/virtualNetworks/subnets@2023-11-01' existing = {
  parent: vnet
  name: appSubnetName
}

resource privateEndpointSubnet 'Microsoft.Network/virtualNetworks/subnets@2023-11-01' existing = {
  parent: vnet
  name: privateEndpointSubnetName
}

resource postgresSubnet 'Microsoft.Network/virtualNetworks/subnets@2023-11-01' existing = {
  parent: vnet
  name: postgresSubnetName
}

resource keyVaultPrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: keyVaultPrivateDnsZoneName
  location: 'global'
  tags: tags
}

resource redisPrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: redisPrivateDnsZoneName
  location: 'global'
  tags: tags
}

resource appServicePrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: appServicePrivateDnsZoneName
  location: 'global'
  tags: tags
}

resource postgresPrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: postgresPrivateDnsZoneName
  location: 'global'
  tags: tags
}

resource keyVaultDnsVnetLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: keyVaultPrivateDnsZone
  name: '${vnet.name}-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnet.id
    }
  }
}

resource redisDnsVnetLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: redisPrivateDnsZone
  name: '${vnet.name}-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnet.id
    }
  }
}

resource appServiceDnsVnetLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: appServicePrivateDnsZone
  name: '${vnet.name}-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnet.id
    }
  }
}

resource postgresDnsVnetLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: postgresPrivateDnsZone
  name: '${vnet.name}-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnet.id
    }
  }
}

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

resource keyVault 'Microsoft.KeyVault/vaults@2023-02-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    tenantId: tenantId
    enableRbacAuthorization: true
    enabledForTemplateDeployment: true
    sku: {
      family: 'A'
      name: 'standard'
    }
    publicNetworkAccess: 'Disabled'
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
    }
  }
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2022-12-01' = {
  name: postgresServerName
  location: location
  tags: tags
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    administratorLogin: postgresAdminLogin
    administratorLoginPassword: postgresAdminPassword
    createMode: 'Default'
    version: '16'
    storage: {
      storageSizeGB: 32
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    network: {
      delegatedSubnetResourceId: postgresSubnet.id
      privateDnsZoneArmResourceId: postgresPrivateDnsZone.id
    }
    authConfig: {
      activeDirectoryAuth: 'Enabled'
      passwordAuth: 'Enabled'
      tenantId: tenantId
    }
  }
}

resource postgresDb 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2022-12-01' = {
  parent: postgres
  name: 'agon'
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

resource redis 'Microsoft.Cache/Redis@2023-08-01' = {
  name: redisName
  location: location
  tags: tags
  properties: {
    publicNetworkAccess: 'Disabled'
    minimumTlsVersion: '1.2'
    enableNonSslPort: false
    redisConfiguration: {
      'maxmemory-policy': 'allkeys-lru'
    }
  }
  sku: {
    name: 'Basic'
    family: 'C'
    capacity: 0
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
    virtualNetworkSubnetId: appSubnet.id
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
          value: '@Microsoft.KeyVault(SecretUri=${postgresConnectionSecret.properties.secretUriWithVersion})'
        }
        {
          name: 'ConnectionStrings__Redis'
          value: '@Microsoft.KeyVault(SecretUri=${redisConnectionSecret.properties.secretUriWithVersion})'
        }
        {
          name: 'OPENAI_KEY'
          value: '@Microsoft.KeyVault(SecretUri=${openAiSecret.properties.secretUriWithVersion})'
        }
        {
          name: 'CLAUDE_KEY'
          value: '@Microsoft.KeyVault(SecretUri=${anthropicSecret.properties.secretUriWithVersion})'
        }
        {
          name: 'GEMINI_KEY'
          value: '@Microsoft.KeyVault(SecretUri=${googleSecret.properties.secretUriWithVersion})'
        }
        {
          name: 'DEEPSEEK_KEY'
          value: '@Microsoft.KeyVault(SecretUri=${deepSeekSecret.properties.secretUriWithVersion})'
        }
      ]
    }
  }
}

resource keyVaultSecretsReaderAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  // Role assignment name must be deployment-time deterministic; use stable resource IDs/names only.
  name: guid(keyVault.id, appService.name, keyVaultSecretsUserRoleDefinitionId)
  scope: keyVault
  properties: {
    roleDefinitionId: keyVaultSecretsUserRoleDefinitionId
    principalId: appService.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource postgresConnectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = {
  parent: keyVault
  name: 'connectionstrings-postgresql'
  properties: {
    value: 'Host=${postgres.name}.postgres.database.azure.com;Port=5432;Database=agon;Username=${postgresAdminLogin};Password=${postgresAdminPassword};SSL Mode=Require;Trust Server Certificate=false;Include Error Detail=true'
  }
}

resource redisConnectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = {
  parent: keyVault
  name: 'connectionstrings-redis'
  properties: {
    value: '${redis.name}.redis.cache.windows.net:6380,password=${redis.listKeys().primaryKey},ssl=True,abortConnect=False'
  }
}

resource openAiSecret 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = {
  parent: keyVault
  name: 'openai-key'
  properties: {
    value: openAiApiKey
  }
}

resource anthropicSecret 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = {
  parent: keyVault
  name: 'claude-key'
  properties: {
    value: anthropicApiKey
  }
}

resource googleSecret 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = {
  parent: keyVault
  name: 'gemini-key'
  properties: {
    value: googleApiKey
  }
}

resource deepSeekSecret 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = {
  parent: keyVault
  name: 'deepseek-key'
  properties: {
    value: deepSeekApiKey
  }
}

resource keyVaultPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = {
  name: 'pep-${keyVault.name}'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointSubnet.id
    }
    privateLinkServiceConnections: [
      {
        name: 'keyVaultConnection'
        properties: {
          privateLinkServiceId: keyVault.id
          groupIds: [
            'vault'
          ]
        }
      }
    ]
  }
}

resource keyVaultPrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = {
  parent: keyVaultPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'kv-zone'
        properties: {
          privateDnsZoneId: keyVaultPrivateDnsZone.id
        }
      }
    ]
  }
}

resource redisPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = {
  name: 'pep-${redis.name}'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointSubnet.id
    }
    privateLinkServiceConnections: [
      {
        name: 'redisConnection'
        properties: {
          privateLinkServiceId: redis.id
          groupIds: [
            'redisCache'
          ]
        }
      }
    ]
  }
}

resource redisPrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = {
  parent: redisPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'redis-zone'
        properties: {
          privateDnsZoneId: redisPrivateDnsZone.id
        }
      }
    ]
  }
}

resource appServicePrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = {
  name: 'pep-${appService.name}'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointSubnet.id
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
        name: 'app-zone'
        properties: {
          privateDnsZoneId: appServicePrivateDnsZone.id
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
  name: 'alert-${namePrefix}-app-availability'
  location: 'global'
  tags: tags
  properties: {
    description: 'Alerts when App Service availability drops under 99%.'
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
          name: 'availability-threshold'
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
output keyVaultName string = keyVault.name
output postgresqlServerName string = postgres.name
output redisCacheName string = redis.name
