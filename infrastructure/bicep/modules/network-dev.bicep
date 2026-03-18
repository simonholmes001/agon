targetScope = 'resourceGroup'

@description('Primary Azure region for this environment.')
param location string

@description('Environment short name.')
param environment string

@description('Workload/application short name.')
param workloadName string

@description('CAF-style naming prefix.')
param namePrefix string

@description('Address space for workload VNet.')
param vnetAddressSpace string

@description('App Service integration subnet prefix.')
param appSubnetPrefix string

@description('Private endpoint subnet prefix.')
param privateEndpointSubnetPrefix string

@description('PostgreSQL delegated subnet prefix.')
param postgresSubnetPrefix string

@description('Application Gateway dedicated subnet prefix.')
param appGatewaySubnetPrefix string

@description('Application Gateway dedicated subnet prefix for Basic/legacy (v1 family) SKUs.')
param appGatewayV1SubnetPrefix string

var tags = {
  environment: environment
  workload: workloadName
  managedBy: 'bicep'
}

var vnetName = 'vnet-${namePrefix}'
var appSubnetName = 'snet-${namePrefix}-app'
var privateEndpointSubnetName = 'snet-${namePrefix}-pep'
var postgresSubnetName = 'snet-${namePrefix}-psql'
var appGatewaySubnetName = 'snet-${namePrefix}-agw'
var appGatewayV1SubnetName = 'snet-${namePrefix}-agw-v1'

var keyVaultPrivateDnsZoneName = 'privatelink.vaultcore.azure.net'
var redisPrivateDnsZoneName = 'privatelink.redis.cache.windows.net'
var postgresPrivateDnsZoneName = 'privatelink.postgres.database.azure.com'
var appServicePrivateDnsZoneName = 'privatelink.azurewebsites.net'
var cognitiveServicesPrivateDnsZoneName = 'privatelink.cognitiveservices.azure.com'

resource vnet 'Microsoft.Network/virtualNetworks@2023-11-01' = {
  name: vnetName
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [
        vnetAddressSpace
      ]
    }
    subnets: [
      {
        name: appSubnetName
        properties: {
          addressPrefix: appSubnetPrefix
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
          addressPrefix: privateEndpointSubnetPrefix
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
      {
        name: postgresSubnetName
        properties: {
          addressPrefix: postgresSubnetPrefix
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
      {
        name: appGatewaySubnetName
        properties: {
          addressPrefix: appGatewaySubnetPrefix
        }
      }
      {
        name: appGatewayV1SubnetName
        properties: {
          addressPrefix: appGatewayV1SubnetPrefix
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

resource appGatewaySubnet 'Microsoft.Network/virtualNetworks/subnets@2023-11-01' existing = {
  parent: vnet
  name: appGatewaySubnetName
}

resource appGatewayV1Subnet 'Microsoft.Network/virtualNetworks/subnets@2023-11-01' existing = {
  parent: vnet
  name: appGatewayV1SubnetName
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

resource postgresPrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: postgresPrivateDnsZoneName
  location: 'global'
  tags: tags
}

resource appServicePrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: appServicePrivateDnsZoneName
  location: 'global'
  tags: tags
}

resource cognitiveServicesPrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: cognitiveServicesPrivateDnsZoneName
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

resource cognitiveServicesDnsVnetLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: cognitiveServicesPrivateDnsZone
  name: '${vnet.name}-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnet.id
    }
  }
}

output vnetId string = vnet.id
output appSubnetId string = appSubnet.id
output privateEndpointSubnetId string = privateEndpointSubnet.id
output postgresSubnetId string = postgresSubnet.id
output appGatewaySubnetId string = appGatewaySubnet.id
output appGatewayV1SubnetId string = appGatewayV1Subnet.id
output keyVaultPrivateDnsZoneId string = keyVaultPrivateDnsZone.id
output redisPrivateDnsZoneId string = redisPrivateDnsZone.id
output postgresPrivateDnsZoneId string = postgresPrivateDnsZone.id
output appServicePrivateDnsZoneId string = appServicePrivateDnsZone.id
output cognitiveServicesPrivateDnsZoneId string = cognitiveServicesPrivateDnsZone.id
