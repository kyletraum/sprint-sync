targetScope = 'subscription'
module sql 'sql.module.bicep' = {
  name: 'sql'
}
module ca 'ca.module.bicep' = {
  name: 'ca'
}
