targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the environment that can be used as part of naming resource convention, the name of the resource group for your application will use this name, prefixed with rg-')
param environmentName string

@minLength(1)
@description('The location used for all deployed resources')
param location string

@description('Id of the user or app to assign application roles')
param principalId string = ''

param AzureAdAudience string
param AzureAdClientId string
param AzureAdInstance string
param AzureAdTenantId string

var tags = {
  'azd-env-name': environmentName
}

resource rg 'Microsoft.Resources/resourceGroups@2022-09-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
}

module api_identity 'api-identity/api-identity.module.bicep' = {
  name: 'api-identity'
  scope: rg
  params: {
    location: location
  }
}
module api_roles_sql 'api-roles-sql/api-roles-sql.module.bicep' = {
  name: 'api-roles-sql'
  scope: rg
  params: {
    location: location
    principalId: api_identity.outputs.principalId
    principalName: api_identity.outputs.principalName
    sql_outputs_name: sql.outputs.name
    sql_outputs_sqlserveradminname: sql.outputs.sqlServerAdminName
  }
}
module cae 'cae/cae.module.bicep' = {
  name: 'cae'
  scope: rg
  params: {
    cae_acr_outputs_name: cae_acr.outputs.name
    location: location
    userPrincipalId: principalId
  }
}
module cae_acr 'cae-acr/cae-acr.module.bicep' = {
  name: 'cae-acr'
  scope: rg
  params: {
    location: location
  }
}
module sql 'sql/sql.module.bicep' = {
  name: 'sql'
  scope: rg
  params: {
    location: location
  }
}
output API_IDENTITY_CLIENTID string = api_identity.outputs.clientId
output API_IDENTITY_ID string = api_identity.outputs.id
output AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN string = cae.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = cae.outputs.AZURE_CONTAINER_REGISTRY_ENDPOINT
output CAE_AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN string = cae.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN
output CAE_AZURE_CONTAINER_APPS_ENVIRONMENT_ID string = cae.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_ID
output CAE_AZURE_CONTAINER_REGISTRY_ENDPOINT string = cae.outputs.AZURE_CONTAINER_REGISTRY_ENDPOINT
output CAE_AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID string = cae.outputs.AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID
output SQL_SQLSERVERFQDN string = sql.outputs.sqlServerFqdn
