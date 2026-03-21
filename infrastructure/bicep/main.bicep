targetScope = 'subscription'

@description('Primary Azure region for the dev environment.')
param location string = 'swedencentral'

@description('Environment short name.')
@allowed([
  'dev'
])
param environment string = 'dev'

@description('Workload/application short name.')
param workloadName string = 'agon'

@description('CAF-style naming prefix.')
param namePrefix string = 'agon-dev-swc'

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

@description('Application Gateway dedicated subnet prefix for Basic/legacy (v1 family) SKUs.')
param appGatewayV1SubnetPrefix string = '10.42.5.0/24'

@description('Optional suffix for Application Gateway resources to support parallel replacement cutovers (for example: v1).')
@maxLength(20)
param appGatewayResourceSuffix string = ''

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

@description('Document Intelligence model used for attachment extraction.')
param documentIntelligenceModelId string = 'prebuilt-layout'

@description('Enable JWT bearer authentication in the backend API.')
param authEnabled bool = false

@description('JWT authority URL for Microsoft Entra validation.')
param jwtAuthority string = ''

@description('JWT audience expected by the backend API.')
param jwtAudience string = ''

@description('PFX certificate for Application Gateway HTTPS listener (base64-encoded).')
@secure()
param appGatewaySslCertificatePfxBase64 string = ''

@description('Password for Application Gateway HTTPS listener certificate.')
@secure()
param appGatewaySslCertificatePassword string = ''

var commonTags = {
  environment: environment
  workload: workloadName
  managedBy: 'bicep'
}

var netResourceGroupName = 'rg-${namePrefix}-net'
var appResourceGroupName = 'rg-${namePrefix}-app'
var dataResourceGroupName = 'rg-${namePrefix}-data'
var appServiceName = 'app-${namePrefix}-${take(uniqueString(subscription().id, rgApp.id, namePrefix, environment), 12)}'
var normalizedAppGatewaySkuTier = toLower(appGatewaySkuTier)
var isModernAppGatewayTier = endsWith(normalizedAppGatewaySkuTier, '_v2')
var isParallelAppGatewayDeployment = !empty(appGatewayResourceSuffix)

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
    appGatewayV1SubnetPrefix: appGatewayV1SubnetPrefix
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
    cognitiveServicesPrivateDnsZoneId: network.outputs.cognitiveServicesPrivateDnsZoneId
    blobPrivateDnsZoneId: network.outputs.blobPrivateDnsZoneId
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
    appGatewayResourceSuffix: appGatewayResourceSuffix
    appGatewaySkuName: appGatewaySkuName
    appGatewaySkuTier: appGatewaySkuTier
    appGatewayInstanceCount: appGatewayInstanceCount
    appGatewayAutoscaleMinCapacity: appGatewayAutoscaleMinCapacity
    appGatewayAutoscaleMaxCapacity: appGatewayAutoscaleMaxCapacity
    appGatewayRequestTimeoutSeconds: appGatewayRequestTimeoutSeconds
    authEnabled: authEnabled
    jwtAuthority: jwtAuthority
    jwtAudience: jwtAudience
    appGatewaySslCertificatePfxBase64: appGatewaySslCertificatePfxBase64
    appGatewaySslCertificatePassword: appGatewaySslCertificatePassword
    appSubnetId: network.outputs.appSubnetId
    appGatewaySubnetId: isParallelAppGatewayDeployment
      ? network.outputs.appGatewayV1SubnetId
      : (isModernAppGatewayTier ? network.outputs.appGatewaySubnetId : network.outputs.appGatewayV1SubnetId)
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    appServicePrivateDnsZoneId: network.outputs.appServicePrivateDnsZoneId
    postgresServerName: data.outputs.postgresqlServerName
    redisHostName: data.outputs.redisHostName
    openAiSecretUri: data.outputs.openAiSecretUri
    anthropicSecretUri: data.outputs.anthropicSecretUri
    googleSecretUri: data.outputs.googleSecretUri
    deepSeekSecretUri: data.outputs.deepSeekSecretUri
    documentIntelligenceEndpoint: data.outputs.documentIntelligenceEndpoint
    documentIntelligenceModelId: documentIntelligenceModelId
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

module documentIntelligenceAccess './modules/document-intelligence-access-dev.bicep' = {
  name: 'document-intelligence-access-dev'
  scope: rgData
  params: {
    documentIntelligenceAccountName: data.outputs.documentIntelligenceAccountName
    principalId: appEdge.outputs.appPrincipalId
    principalDisplayNameSeed: appServiceName
  }
}

module redisAccess './modules/redis-access-dev.bicep' = {
  name: 'redis-access-dev'
  scope: rgData
  params: {
    redisCacheName: data.outputs.redisCacheName
    principalId: appEdge.outputs.appPrincipalId
  }
}

module postgresAccess './modules/postgres-access-dev.bicep' = {
  name: 'postgres-access-dev'
  scope: rgData
  params: {
    postgresqlServerName: data.outputs.postgresqlServerName
    principalObjectId: appEdge.outputs.appPrincipalId
    principalName: appServiceName
    tenantId: tenantId
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
output documentIntelligenceAccountName string = data.outputs.documentIntelligenceAccountName
output attachmentStorageAccountName string = data.outputs.attachmentStorageAccountName
output attachmentStorageAccountId string = data.outputs.attachmentStorageAccountId
output attachmentStorageBlobEndpoint string = data.outputs.attachmentStorageBlobEndpoint
output attachmentContainerName string = data.outputs.attachmentContainerName
