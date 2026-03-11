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

var resourceGroupName = 'rg-${namePrefix}-app'

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: resourceGroupName
  location: location
  tags: {
    environment: environment
    workload: workloadName
    managedBy: 'bicep'
  }
}

module backendPlatform './modules/backend-platform-dev.bicep' = {
  name: 'backend-platform-dev'
  scope: rg
  params: {
    location: location
    environment: environment
    workloadName: workloadName
    namePrefix: namePrefix
    alertEmail: alertEmail
    postgresAdminLogin: postgresAdminLogin
    postgresAdminPassword: postgresAdminPassword
    tenantId: tenantId
    openAiApiKey: openAiApiKey
    anthropicApiKey: anthropicApiKey
    googleApiKey: googleApiKey
    deepSeekApiKey: deepSeekApiKey
  }
}

output targetResourceGroup string = rg.name
output appServiceName string = backendPlatform.outputs.appServiceName
output appServiceDefaultHostName string = backendPlatform.outputs.appServiceDefaultHostName
output keyVaultName string = backendPlatform.outputs.keyVaultName
output postgresqlServerName string = backendPlatform.outputs.postgresqlServerName
output redisCacheName string = backendPlatform.outputs.redisCacheName
