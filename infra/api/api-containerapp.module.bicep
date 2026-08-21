@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param cae_outputs_azure_container_apps_environment_default_domain string

param cae_outputs_azure_container_apps_environment_id string

param api_containerimage string

param api_identity_outputs_id string

param api_containerport string

param sql_outputs_sqlserverfqdn string

param azureadinstance_value string

param azureadtenantid_value string

param azureadclientid_value string

param azureadaudience_value string

param api_identity_outputs_clientid string

param cae_outputs_azure_container_registry_endpoint string

param cae_outputs_azure_container_registry_managed_identity_id string

resource api 'Microsoft.App/containerApps@2025-10-02-preview' = {
  name: 'api'
  location: location
  properties: {
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: int(api_containerport)
        transport: 'http'
      }
      registries: [
        {
          server: cae_outputs_azure_container_registry_endpoint
          identity: cae_outputs_azure_container_registry_managed_identity_id
        }
      ]
      runtime: {
        dotnet: {
          autoConfigureDataProtection: true
        }
      }
    }
    environmentId: cae_outputs_azure_container_apps_environment_id
    template: {
      containers: [
        {
          image: api_containerimage
          name: 'api'
          env: [
            {
              name: 'OTEL_DOTNET_EXPERIMENTAL_OTLP_RETRY'
              value: 'in_memory'
            }
            {
              name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED'
              value: 'true'
            }
            {
              name: 'HTTP_PORTS'
              value: api_containerport
            }
            {
              name: 'ConnectionStrings__sprintsync'
              value: 'Server=tcp:${sql_outputs_sqlserverfqdn},1433;Encrypt=True;Authentication="Active Directory Default";Database=sprintsync'
            }
            {
              name: 'SPRINTSYNC_HOST'
              value: sql_outputs_sqlserverfqdn
            }
            {
              name: 'SPRINTSYNC_PORT'
              value: '1433'
            }
            {
              name: 'SPRINTSYNC_URI'
              value: 'mssql://${sql_outputs_sqlserverfqdn}:1433/sprintsync'
            }
            {
              name: 'SPRINTSYNC_JDBCCONNECTIONSTRING'
              value: 'jdbc:sqlserver://${sql_outputs_sqlserverfqdn}:1433;database=sprintsync;encrypt=true;trustServerCertificate=false'
            }
            {
              name: 'SPRINTSYNC_DATABASENAME'
              value: 'sprintsync'
            }
            {
              name: 'Database__MigrateOnStartup'
              value: 'true'
            }
            {
              name: 'DemoData__Enabled'
              value: 'true'
            }
            {
              name: 'AzureAd__Instance'
              value: azureadinstance_value
            }
            {
              name: 'AzureAd__TenantId'
              value: azureadtenantid_value
            }
            {
              name: 'AzureAd__ClientId'
              value: azureadclientid_value
            }
            {
              name: 'AzureAd__Audience'
              value: azureadaudience_value
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: api_identity_outputs_clientid
            }
            {
              name: 'AZURE_TOKEN_CREDENTIALS'
              value: 'ManagedIdentityCredential'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
      }
    }
  }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${api_identity_outputs_id}': { }
      '${cae_outputs_azure_container_registry_managed_identity_id}': { }
    }
  }
}