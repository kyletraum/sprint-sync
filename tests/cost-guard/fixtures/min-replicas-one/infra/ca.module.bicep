resource api 'Microsoft.App/containerApps@2025-10-02-preview' = {
  name: 'api'
  properties: {
    template: {
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}
