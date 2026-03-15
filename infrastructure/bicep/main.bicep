targetScope = 'subscription'

@description('Primary Azure region for the dev environment.')
param location string = 'francecentral'

@description('Environment short name.')
@allowed([
  'dev'
])
param environment string = 'dev'

@description('Workload/application short name.')
param workloadName string = 'agon'

@description('CAF-style naming prefix.')
param namePrefix string = 'agon-dev-frc'

@description('Alert email receiver for action groups.')
param alertEmail string = 'simonholmesabc@gmail.com'

@description('PostgreSQL admin username used for bootstrap operations.')
param postgresAdminLogin string = 'agonadmin'

@secure()
@description('PostgreSQL admin password used for bootstrap operations.')
param postgresAdminPassword string

@description('Microsoft Entra tenant ID used by PostgreSQL auth configuration.')
param tenantId string

@description('Optional OpenAI API key to seed Key Vault secret during deployment.')
@secure()
param openAiApiKey string = ''

@description('Optional Anthropic API key to seed Key Vault secret during deployment.')
@secure()
param anthropicApiKey string = ''

@description('Optional Google API key to seed Key Vault secret during deployment.')
@secure()
param googleApiKey string = ''

@description('Optional DeepSeek API key to seed Key Vault secret during deployment.')
@secure()
param deepSeekApiKey string = ''

@description('Address space for the workload VNet.')
param vnetAddressSpace string = '10.42.0.0/16'

@description('App Service integration subnet prefix.')
param appSubnetPrefix string = '10.42.1.0/24'

@description('Private endpoint subnet prefix.')
param privateEndpointSubnetPrefix string = '10.42.2.0/24'

@description('PostgreSQL delegated subnet prefix.')
param postgresSubnetPrefix string = '10.42.3.0/24'

@description('Application Gateway dedicated subnet prefix.')
param appGatewaySubnetPrefix string = '10.42.4.0/24'

@description('Application Gateway WAF mode for this environment.')
@allowed([
  'Detection'
  'Prevention'
])
param appGatewayWafMode string = 'Detection'

@description('Application Gateway backend request timeout in seconds.')
@minValue(1)
@maxValue(86400)
param appGatewayRequestTimeoutSeconds int = 120

var commonTags = {
  environment: environment
  workload: workloadName
  managedBy: 'bicep'
}

var netResourceGroupName = 'rg-${namePrefix}-net'
var appResourceGroupName = 'rg-${namePrefix}-app'
var dataResourceGroupName = 'rg-${namePrefix}-data'
var appServiceName = 'app-${namePrefix}-${take(uniqueString(subscription().id, rgApp.id, namePrefix, environment), 12)}'

resource rgNet 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: netResourceGroupName
  location: location
  tags: commonTags
}

resource rgApp 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: appResourceGroupName
  location: location
  tags: commonTags
}

resource rgData 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: dataResourceGroupName
  location: location
  tags: commonTags
}

module network './modules/network-dev.bicep' = {
  name: 'network-dev'
  scope: rgNet
  params: {
    location: location
    environment: environment
    workloadName: workloadName
    namePrefix: namePrefix
    vnetAddressSpace: vnetAddressSpace
    appSubnetPrefix: appSubnetPrefix
    privateEndpointSubnetPrefix: privateEndpointSubnetPrefix
    postgresSubnetPrefix: postgresSubnetPrefix
    appGatewaySubnetPrefix: appGatewaySubnetPrefix
  }
}

module data './modules/data-dev.bicep' = {
  name: 'data-dev'
  scope: rgData
  params: {
    location: location
    environment: environment
    workloadName: workloadName
    namePrefix: namePrefix
    tenantId: tenantId
    postgresAdminLogin: postgresAdminLogin
    postgresAdminPassword: postgresAdminPassword
    postgresSubnetId: network.outputs.postgresSubnetId
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    postgresPrivateDnsZoneId: network.outputs.postgresPrivateDnsZoneId
    keyVaultPrivateDnsZoneId: network.outputs.keyVaultPrivateDnsZoneId
    redisPrivateDnsZoneId: network.outputs.redisPrivateDnsZoneId
    openAiApiKey: openAiApiKey
    anthropicApiKey: anthropicApiKey
    googleApiKey: googleApiKey
    deepSeekApiKey: deepSeekApiKey
  }
}

module appEdge './modules/app-edge-dev.bicep' = {
  name: 'app-edge-dev'
  scope: rgApp
  params: {
    location: location
    environment: environment
    workloadName: workloadName
    namePrefix: namePrefix
    alertEmail: alertEmail
    appGatewayWafMode: appGatewayWafMode
    appGatewayRequestTimeoutSeconds: appGatewayRequestTimeoutSeconds
    appSubnetId: network.outputs.appSubnetId
    appGatewaySubnetId: network.outputs.appGatewaySubnetId
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    appServicePrivateDnsZoneId: network.outputs.appServicePrivateDnsZoneId
    postgresConnectionSecretUri: data.outputs.postgresConnectionSecretUri
    redisConnectionSecretUri: data.outputs.redisConnectionSecretUri
    openAiSecretUri: data.outputs.openAiSecretUri
    anthropicSecretUri: data.outputs.anthropicSecretUri
    googleSecretUri: data.outputs.googleSecretUri
    deepSeekSecretUri: data.outputs.deepSeekSecretUri
  }
}

module keyVaultAccess './modules/keyvault-access-dev.bicep' = {
  name: 'keyvault-access-dev'
  scope: rgData
  params: {
    keyVaultName: data.outputs.keyVaultName
    principalId: appEdge.outputs.appPrincipalId
    principalDisplayNameSeed: appServiceName
  }
}

output networkResourceGroup string = rgNet.name
output appResourceGroup string = rgApp.name
output dataResourceGroup string = rgData.name
output appServiceName string = appEdge.outputs.appServiceName
output appServiceDefaultHostName string = appEdge.outputs.appServiceDefaultHostName
output appGatewayPublicIpAddress string = appEdge.outputs.appGatewayPublicIpAddress
output keyVaultName string = data.outputs.keyVaultName
output postgresqlServerName string = data.outputs.postgresqlServerName
output redisCacheName string = data.outputs.redisCacheName
