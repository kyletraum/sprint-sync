targetScope = 'subscription'
module cae 'cae.module.bicep' = {
  name: 'cae'
}
module sql 'sql.module.bicep' = {
  name: 'sql'
}
module ca 'ca.module.bicep' = {
  name: 'ca'
}
