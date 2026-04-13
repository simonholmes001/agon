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

@description('Optional Blob Storage connection string to seed Key Vault secret during deployment.')
@secure()
param blobStorageConnectionString string = ''

@description('Blob container name used for session attachments.')
param attachmentContainerName string = 'session-attachments'

@description('Retention window (days) for attachment blobs before lifecycle deletion.')
@minValue(1)
param attachmentRetentionDays int = 90

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

@description('Maximum extracted attachment text chars passed to backend processing.')
@minValue(1)
param attachmentProcessingMaxExtractedTextChars int = 200000

@description('Reject attachments whose route cannot be resolved to a supported text/image/document family.')
param attachmentProcessingValidationRejectUnsupportedFormats bool = true

@description('Absolute maximum attachment upload size in bytes.')
@minValue(1024)
param attachmentProcessingValidationMaxUploadBytes int = 26214400

@description('Maximum text-route attachment size in bytes.')
@minValue(1024)
param attachmentProcessingValidationMaxTextUploadBytes int = 10485760

@description('Maximum document-route attachment size in bytes.')
@minValue(1024)
param attachmentProcessingValidationMaxDocumentUploadBytes int = 26214400

@description('Maximum image-route attachment size in bytes.')
@minValue(1024)
param attachmentProcessingValidationMaxImageUploadBytes int = 20971520

@description('Maximum transient retry attempts for attachment extraction HTTP operations.')
@minValue(1)
param attachmentProcessingTransientRetryMaxAttempts int = 3

@description('Base retry delay in milliseconds for transient attachment extraction failures.')
@minValue(1)
param attachmentProcessingTransientRetryBaseDelayMs int = 250

@description('Maximum retry delay in milliseconds for transient attachment extraction failures.')
@minValue(1)
param attachmentProcessingTransientRetryMaxDelayMs int = 2000

@description('Enable long-document context-window chunk-loop processing.')
param attachmentProcessingChunkLoopEnabled bool = true

@description('Extracted text length threshold that activates chunk-loop processing.')
@minValue(1)
param attachmentProcessingChunkLoopActivationThresholdChars int = 14000

@description('Target chunk size in characters for chunk-loop processing.')
@minValue(1)
param attachmentProcessingChunkLoopChunkSizeChars int = 12000

@description('Chunk overlap in characters between adjacent chunk-loop passes.')
@minValue(0)
param attachmentProcessingChunkLoopChunkOverlapChars int = 1000

@description('Maximum chunk count processed per attachment during chunk-loop prelude.')
@minValue(1)
param attachmentProcessingChunkLoopMaxChunksPerAttachment int = 20

@description('Maximum chars retained from each chunk-pass note before final synthesis.')
@minValue(1)
param attachmentProcessingChunkLoopMaxChunkNoteChars int = 1200

@description('Maximum chunk-pass notes retained per agent before final synthesis.')
@minValue(1)
param attachmentProcessingChunkLoopMaxFinalNotesPerAgent int = 8

@description('Enable JWT bearer authentication in the backend API.')
param authEnabled bool = false

@description('JWT authority URL for Microsoft Entra validation.')
param jwtAuthority string = ''

@description('JWT audience expected by the backend API.')
param jwtAudience string = ''

@description('Optional public client ID used by CLI device-code login.')
param jwtInteractiveClientId string = ''

@description('Enable invite-only trial access controls in the backend.')
param trialAccessEnabled bool = false

@allowed([
  'RestrictedGroups'
  'AllAuthenticatedUsers'
])
@description('Trial rollout access mode. RestrictedGroups for early testers, AllAuthenticatedUsers for post-early rollout.')
param trialAccessMode string = 'RestrictedGroups'

@description('Require Entra group membership claim for trial access decisions.')
param trialEnforceEntraGroupMembership bool = true

@description('Comma-separated Entra group object IDs used as the trial tester allowlist source of truth.')
param trialRequiredEntraGroupObjectIdsCsv string = ''

@description('Comma-separated Entra group object IDs for admin/operator bypass of trial controls.')
param trialAdminBypassEntraGroupObjectIdsCsv string = ''

@description('Enable token quota enforcement for trial users.')
param trialQuotaEnabled bool = true

@description('Per-user token limit for each trial quota window.')
@minValue(1)
param trialQuotaTokenLimit int = 40000

@description('Quota window size in days.')
@minValue(1)
param trialQuotaWindowDays int = 7

@description('Enable per-user request rate limiting for trial users.')
param trialRequestRateLimitEnabled bool = true

@description('Per-user allowed requests per minute for trial access checks.')
@minValue(1)
param trialRequestsPerMinute int = 20

@description('Per-user burst capacity for request rate limiting.')
@minValue(1)
param trialBurstCapacity int = 10

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
    blobStorageConnectionString: blobStorageConnectionString
    attachmentRetentionDays: attachmentRetentionDays
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
    jwtInteractiveClientId: jwtInteractiveClientId
    trialAccessEnabled: trialAccessEnabled
    trialAccessMode: trialAccessMode
    trialEnforceEntraGroupMembership: trialEnforceEntraGroupMembership
    trialRequiredEntraGroupObjectIdsCsv: trialRequiredEntraGroupObjectIdsCsv
    trialAdminBypassEntraGroupObjectIdsCsv: trialAdminBypassEntraGroupObjectIdsCsv
    trialQuotaEnabled: trialQuotaEnabled
    trialQuotaTokenLimit: trialQuotaTokenLimit
    trialQuotaWindowDays: trialQuotaWindowDays
    trialRequestRateLimitEnabled: trialRequestRateLimitEnabled
    trialRequestsPerMinute: trialRequestsPerMinute
    trialBurstCapacity: trialBurstCapacity
    appGatewaySslCertificatePfxBase64: appGatewaySslCertificatePfxBase64
    appGatewaySslCertificatePassword: appGatewaySslCertificatePassword
    appGatewayPublicHostName: appGatewayPublicHostName
    appGatewaySslPolicyName: appGatewaySslPolicyName
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
    blobStorageConnectionStringSecretUri: data.outputs.blobStorageConnectionStringSecretUri
    attachmentContainerName: attachmentContainerName
    attachmentRetentionDays: attachmentRetentionDays
    attachmentProcessingMaxExtractedTextChars: attachmentProcessingMaxExtractedTextChars
    attachmentProcessingValidationRejectUnsupportedFormats: attachmentProcessingValidationRejectUnsupportedFormats
    attachmentProcessingValidationMaxUploadBytes: attachmentProcessingValidationMaxUploadBytes
    attachmentProcessingValidationMaxTextUploadBytes: attachmentProcessingValidationMaxTextUploadBytes
    attachmentProcessingValidationMaxDocumentUploadBytes: attachmentProcessingValidationMaxDocumentUploadBytes
    attachmentProcessingValidationMaxImageUploadBytes: attachmentProcessingValidationMaxImageUploadBytes
    attachmentProcessingTransientRetryMaxAttempts: attachmentProcessingTransientRetryMaxAttempts
    attachmentProcessingTransientRetryBaseDelayMs: attachmentProcessingTransientRetryBaseDelayMs
    attachmentProcessingTransientRetryMaxDelayMs: attachmentProcessingTransientRetryMaxDelayMs
    attachmentProcessingChunkLoopEnabled: attachmentProcessingChunkLoopEnabled
    attachmentProcessingChunkLoopActivationThresholdChars: attachmentProcessingChunkLoopActivationThresholdChars
    attachmentProcessingChunkLoopChunkSizeChars: attachmentProcessingChunkLoopChunkSizeChars
    attachmentProcessingChunkLoopChunkOverlapChars: attachmentProcessingChunkLoopChunkOverlapChars
    attachmentProcessingChunkLoopMaxChunksPerAttachment: attachmentProcessingChunkLoopMaxChunksPerAttachment
    attachmentProcessingChunkLoopMaxChunkNoteChars: attachmentProcessingChunkLoopMaxChunkNoteChars
    attachmentProcessingChunkLoopMaxFinalNotesPerAgent: attachmentProcessingChunkLoopMaxFinalNotesPerAgent
    documentIntelligenceEndpoint: data.outputs.documentIntelligenceEndpoint
    documentIntelligenceModelId: documentIntelligenceModelId
    attachmentStorageBlobEndpoint: data.outputs.attachmentStorageBlobEndpoint
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

module storageAccess './modules/storage-access-dev.bicep' = {
  name: 'storage-access-dev'
  scope: rgData
  params: {
    storageAccountName: data.outputs.attachmentStorageAccountName
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
output appGatewayPreferredApiUrl string = appEdge.outputs.appGatewayPreferredApiUrl
output keyVaultName string = data.outputs.keyVaultName
output postgresqlServerName string = data.outputs.postgresqlServerName
output redisCacheName string = data.outputs.redisCacheName
output documentIntelligenceAccountName string = data.outputs.documentIntelligenceAccountName
output attachmentStorageAccountName string = data.outputs.attachmentStorageAccountName
output attachmentStorageAccountId string = data.outputs.attachmentStorageAccountId
output attachmentStorageBlobEndpoint string = data.outputs.attachmentStorageBlobEndpoint
output attachmentContainerName string = data.outputs.attachmentContainerName
