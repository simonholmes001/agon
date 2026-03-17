targetScope = 'resourceGroup'

@description('Target PostgreSQL flexible server name.')
param postgresqlServerName string

@description('Microsoft Entra principal object ID (GUID) to configure as PostgreSQL Entra admin.')
param principalObjectId string

@description('Microsoft Entra principal name that should be configured as PostgreSQL Entra admin.')
param principalName string

@description('Microsoft Entra tenant ID for PostgreSQL auth.')
param tenantId string

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2022-12-01' existing = {
  name: postgresqlServerName
}

resource postgresEntraAdmin 'Microsoft.DBforPostgreSQL/flexibleServers/administrators@2022-12-01' = {
  parent: postgres
  name: principalObjectId
  properties: {
    principalName: principalName
    principalType: 'ServicePrincipal'
    tenantId: tenantId
  }
}
