resource db 'Microsoft.Sql/servers/databases@2023-05-01' = {
  name: 'sprintsync'
  properties: {
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
  }
}
