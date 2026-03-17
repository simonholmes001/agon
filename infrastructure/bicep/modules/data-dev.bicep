targetScope = 'resourceGroup'

@description('Primary Azure region for this environment.')
param location string

@description('Environment short name.')
param environment string

@description('Workload/application short name.')
param workloadName string

@description('CAF-style naming prefix.')
param namePrefix string

@description('Microsoft Entra tenant ID used by PostgreSQL auth configuration.')
param tenantId string

@description('PostgreSQL admin username used for bootstrap operations.')
param postgresAdminLogin string

@secure()
@description('PostgreSQL admin password used for bootstrap operations.')
param postgresAdminPassword string

@description('PostgreSQL delegated subnet resource ID.')
param postgresSubnetId string

@description('Private endpoint subnet resource ID.')
param privateEndpointSubnetId string

@description('Private DNS zone ID for PostgreSQL private connectivity.')
param postgresPrivateDnsZoneId string

@description('Private DNS zone ID for Key Vault private endpoint.')
param keyVaultPrivateDnsZoneId string

@description('Private DNS zone ID for Redis private endpoint.')
param redisPrivateDnsZoneId string

@description('Private DNS zone ID for Cognitive Services private endpoint.')
param cognitiveServicesPrivateDnsZoneId string

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

var keyVaultName = 'kv-${replace(namePrefix, '-', '')}-${take(uniqueString(resourceGroup().id), 6)}'
var postgresServerName = 'psql-${namePrefix}-${take(uniqueString(resourceGroup().id), 6)}'
var redisName = 'redis-${namePrefix}-${take(uniqueString(resourceGroup().id), 6)}'
var documentIntelligenceName = 'docint-${namePrefix}-${take(uniqueString(resourceGroup().id), 6)}'
var hasOpenAiKey = !empty(openAiApiKey)
var hasAnthropicKey = !empty(anthropicApiKey)
var hasGoogleKey = !empty(googleApiKey)
var hasDeepSeekKey = !empty(deepSeekApiKey)

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

resource keyVaultPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = {
  name: 'pep-${keyVault.name}'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointSubnetId
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
          privateDnsZoneId: keyVaultPrivateDnsZoneId
        }
      }
    ]
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
      delegatedSubnetResourceId: postgresSubnetId
      privateDnsZoneArmResourceId: postgresPrivateDnsZoneId
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

resource redis 'Microsoft.Cache/Redis@2024-11-01' = {
  name: redisName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'Basic'
      family: 'C'
      capacity: 0
    }
    publicNetworkAccess: 'Disabled'
    disableAccessKeyAuthentication: true
    minimumTlsVersion: '1.2'
    enableNonSslPort: false
    redisConfiguration: {
      'aad-enabled': 'true'
      'maxmemory-policy': 'allkeys-lru'
    }
  }
}

resource documentIntelligence 'Microsoft.CognitiveServices/accounts@2023-05-01' = {
  name: documentIntelligenceName
  location: location
  tags: tags
  kind: 'FormRecognizer'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: documentIntelligenceName
    publicNetworkAccess: 'Disabled'
    disableLocalAuth: true
  }
}

resource redisPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = {
  name: 'pep-${redis.name}'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointSubnetId
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

resource documentIntelligencePrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = {
  name: 'pep-${documentIntelligence.name}'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'documentIntelligenceConnection'
        properties: {
          privateLinkServiceId: documentIntelligence.id
          groupIds: [
            'account'
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
          privateDnsZoneId: redisPrivateDnsZoneId
        }
      }
    ]
  }
}

resource documentIntelligencePrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = {
  parent: documentIntelligencePrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'cognitive-zone'
        properties: {
          privateDnsZoneId: cognitiveServicesPrivateDnsZoneId
        }
      }
    ]
  }
}

resource openAiSecret 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = if (hasOpenAiKey) {
  parent: keyVault
  name: 'openai-key'
  properties: {
    value: openAiApiKey
  }
}

resource anthropicSecret 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = if (hasAnthropicKey) {
  parent: keyVault
  name: 'claude-key'
  properties: {
    value: anthropicApiKey
  }
}

resource googleSecret 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = if (hasGoogleKey) {
  parent: keyVault
  name: 'gemini-key'
  properties: {
    value: googleApiKey
  }
}

resource deepSeekSecret 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = if (hasDeepSeekKey) {
  parent: keyVault
  name: 'deepseek-key'
  properties: {
    value: deepSeekApiKey
  }
}

output keyVaultName string = keyVault.name
output keyVaultId string = keyVault.id
output postgresqlServerName string = postgres.name
output redisCacheName string = redis.name
output redisHostName string = redis.properties.hostName
output documentIntelligenceAccountName string = documentIntelligence.name
output documentIntelligenceId string = documentIntelligence.id
output documentIntelligenceEndpoint string = 'https://${documentIntelligence.name}.cognitiveservices.azure.com'
output openAiSecretUri string = hasOpenAiKey ? openAiSecret!.properties.secretUri : ''
output anthropicSecretUri string = hasAnthropicKey ? anthropicSecret!.properties.secretUri : ''
output googleSecretUri string = hasGoogleKey ? googleSecret!.properties.secretUri : ''
output deepSeekSecretUri string = hasDeepSeekKey ? deepSeekSecret!.properties.secretUri : ''
