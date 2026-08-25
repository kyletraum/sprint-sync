param api_containerimage string
resource api 'Microsoft.App/containerApps@2025-10-02-preview' = {
  name: 'api'
  properties: {
    template: {
      containers: [ { image: api_containerimage, name: 'api' } ]
      scale: { minReplicas: 0, maxReplicas: 1 }
    }
  }
}
