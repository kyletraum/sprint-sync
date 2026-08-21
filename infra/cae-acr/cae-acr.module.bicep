@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

resource cae_acr 'Microsoft.ContainerRegistry/registries@2025-04-01' = {
  name: take('caeacr${uniqueString(resourceGroup().id)}', 50)
  location: location
  sku: {
    name: 'Basic'
  }
  tags: {
    'aspire-resource-name': 'cae-acr'
  }
}

output name string = cae_acr.name

output loginServer string = cae_acr.properties.loginServer

output id string = cae_acr.id