targetScope = 'resourceGroup'

@description('Target Document Intelligence (Cognitive Services) account name.')
param documentIntelligenceAccountName string

@description('Principal ID (managed identity object ID) that should call Document Intelligence.')
param principalId string

@description('Stable seed used to generate deterministic role assignment names.')
param principalDisplayNameSeed string

var cognitiveServicesUserRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'a97b65f3-24c7-4388-baec-2e87135dc908')

resource documentIntelligenceAccount 'Microsoft.CognitiveServices/accounts@2023-05-01' existing = {
  name: documentIntelligenceAccountName
}

resource cognitiveServicesUserAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(documentIntelligenceAccount.id, principalDisplayNameSeed, cognitiveServicesUserRoleDefinitionId)
  scope: documentIntelligenceAccount
  properties: {
    roleDefinitionId: cognitiveServicesUserRoleDefinitionId
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
