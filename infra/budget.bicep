// Cost backstop (constitution Principle 12, task T049).
//
// The design is meant to cost ~$0 at idle: Container Apps scale to zero, Azure
// SQL uses the free serverless offer and auto-pauses, and the SPA sits on Static
// Web Apps Free. This budget is what catches the case where that reasoning is
// wrong — a misconfiguration, an accidental always-on resource, a runaway loop.
//
// $1 is deliberately not a spending allowance. It is the smallest amount that
// still proves something unexpected is running, which is why the first alert
// fires at 50% of it.
//
// Deploy into the app's resource group:
//   az deployment group create -g <rg> -f infra/budget.bicep -p alertEmails='["you@example.com"]'

targetScope = 'resourceGroup'

@description('Name of the budget resource.')
param budgetName string = 'sprint-sync-idle-cost-backstop'

@description('Monthly budget ceiling, in the billing currency. Kept at $1 on purpose.')
param amount int = 1

@description('Addresses notified when a threshold is crossed.')
param alertEmails array

@description('First month the budget applies to. Must be the first of a month, UTC.')
param startDate string = '${utcNow('yyyy-MM')}-01T00:00:00Z'

resource budget 'Microsoft.Consumption/budgets@2023-05-01' = {
  name: budgetName
  properties: {
    category: 'Cost'
    amount: amount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: startDate
    }
    notifications: {
      // Half of a dollar already means something is running that should not be.
      Actual_50_Percent: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 50
        contactEmails: alertEmails
        thresholdType: 'Actual'
      }
      Actual_100_Percent: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        contactEmails: alertEmails
        thresholdType: 'Actual'
      }
      // Forecast catches a climbing spend before the money is actually gone.
      Forecasted_100_Percent: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        contactEmails: alertEmails
        thresholdType: 'Forecasted'
      }
    }
  }
}

output budgetId string = budget.id
