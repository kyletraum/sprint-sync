resource api 'Microsoft.App/containerApps@2025-10-02-preview' = {
  name: 'api'
  properties: {
    template: {
      containers: [ { image: 'fixed:latest', name: 'api' } ]
    }
  }
}
