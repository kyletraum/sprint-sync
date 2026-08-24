// Shape that defeated the old whole-file rule: a compliant Consumption entry
// sitting in the same workloadProfiles array as a billable dedicated one.
resource cae 'Microsoft.App/managedEnvironments@2024-10-02-preview' = {
  name: 'cae'
  properties: {
    workloadProfiles: [
      {
        name: 'consumption'
        workloadProfileType: 'Consumption'
      }
      {
        name: 'dedicated'
        workloadProfileType: 'D4'
        minimumCount: 1
        maximumCount: 3
      }
    ]
  }
}
