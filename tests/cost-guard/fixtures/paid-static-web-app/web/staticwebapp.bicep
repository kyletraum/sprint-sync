resource web 'Microsoft.Web/staticSites@2024-04-01' = {
  name: 'swa'
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
}
