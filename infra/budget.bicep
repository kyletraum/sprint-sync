// Cost backstop (constitution Principle 12, task T049).
//
// The design is meant to cost ~$0 at idle: Container Apps scale to zero, Azure
// SQL uses the free serverless offer and auto-pauses, and the SPA sits on Static
// Web Apps Free. This budget is what catches the case where that reasoning is
// wrong — a misconfiguration, an accidental always-on resource, a runaway loop.
//
// The design's only unavoidable idle cost is the container registry (ACR Basic,
// ~$5/mo) plus a little Log Analytics — everything else scales to zero or
// auto-pauses. So "idle ~= $0" is really "~$5-8/mo floor". The ceiling sits ABOVE
// that known floor so the 50% alert flags a GENUINE anomaly (an accidental
// always-on resource), rather than firing every month on the expected registry
// bill (P1-5).
//
// Provisioned automatically by the azd postprovision hook (azure.yaml) when
// BUDGET_ALERT_EMAILS is set — no manual step (P1-6). To set it:
//   azd env set BUDGET_ALERT_EMAILS '["you@example.com"]'

targetScope = 'resourceGroup'

@description('Name of the budget resource.')
param budgetName string = 'sprint-sync-idle-cost-backstop'

@description('Monthly budget ceiling. Set above the ~$5-8/mo ACR Basic + Log Analytics floor so the 50% alert flags genuine anomalies, not the expected registry bill.')
param amount int = 20

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
