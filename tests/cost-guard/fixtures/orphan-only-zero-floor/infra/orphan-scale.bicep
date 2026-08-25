resource ghost 'Microsoft.App/containerApps@2025-10-02-preview' = {
  name: 'ghost'
  properties: {
    template: {
      scale: { minReplicas: 0, maxReplicas: 1 }
    }
  }
}
