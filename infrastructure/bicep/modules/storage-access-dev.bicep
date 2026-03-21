targetScope = 'resourceGroup'

@description('Target storage account name for attachment blobs.')
param storageAccountName string

@description('Principal ID (managed identity object ID) that should access blob data plane.')
param principalId string

@description('Stable seed used to generate deterministic role assignment names.')
param principalDisplayNameSeed string

var storageBlobDataContributorRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

resource storageBlobDataContributorAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, principalDisplayNameSeed, storageBlobDataContributorRoleDefinitionId)
  scope: storageAccount
  properties: {
    roleDefinitionId: storageBlobDataContributorRoleDefinitionId
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
