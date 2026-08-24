resource api 'Microsoft.App/containerApps@2025-10-02-preview' = {
  name: 'api'
  properties: {
    template: {
      scale: { minReplicas: 0, maxReplicas: 1 }
    }
  }
}
