targetScope = 'resourceGroup'

@description('Target Azure Cache for Redis name.')
param redisCacheName string

@description('Principal ID (managed identity object ID) that should access Redis data plane.')
param principalId string

resource redis 'Microsoft.Cache/Redis@2024-11-01' existing = {
  name: redisCacheName
}

resource redisDataOwnerAssignment 'Microsoft.Cache/redis/accessPolicyAssignments@2024-11-01' = {
  parent: redis
  name: 'mi-data-owner'
  properties: {
    accessPolicyName: 'Data Owner'
    objectId: principalId
    objectIdAlias: principalId
  }
}
