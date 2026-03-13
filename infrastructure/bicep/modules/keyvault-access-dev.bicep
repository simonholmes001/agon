targetScope = 'resourceGroup'

@description('Target Key Vault name.')
param keyVaultName string

@description('Principal ID (managed identity object ID) that should read secrets.')
param principalId string

@description('Stable seed used to generate deterministic role assignment names.')
param principalDisplayNameSeed string

var keyVaultSecretsUserRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')

resource keyVault 'Microsoft.KeyVault/vaults@2023-02-01' existing = {
  name: keyVaultName
}

resource keyVaultSecretsReaderAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, principalDisplayNameSeed, keyVaultSecretsUserRoleDefinitionId)
  scope: keyVault
  properties: {
    roleDefinitionId: keyVaultSecretsUserRoleDefinitionId
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

