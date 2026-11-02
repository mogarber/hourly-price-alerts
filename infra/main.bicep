// Hourly Price Alerts backend. Deploy at subscription scope:
//   az deployment sub create --location northcentralus --template-file infra/main.bicep \
//     --parameters budgetContactEmail=you@example.com apnsKeyId=ABC123DEFG apnsTeamId=TEAM123456
// The APNs private key is NOT part of this template. After the first deployment, set the Key Vault secret
// named 'apns-private-key' to the PEM text of the .p8 key (see the outputs for the vault name).
targetScope = 'subscription'

@description('Azure region for every resource.')
param location string = 'northcentralus'

@description('Short prefix for resource names. Lowercase letters and digits.')
@minLength(3)
@maxLength(12)
param namePrefix string = 'hourlyprice'

@description('Resource group to create and deploy into.')
param resourceGroupName string = 'rg-${namePrefix}'

@description('Monthly budget in USD. Alerts at 80% actual, 100% actual and 100% forecast.')
param budgetAmount int = 5

@description('Email address that receives budget alerts. Leave empty to skip creating a budget, for example when the subscription already has one.')
param budgetContactEmail string = ''

@description('First day of the month the budget starts. Must be the current or a future month. On a redeploy in a later month pass the original start date.')
param budgetStartDate string = utcNow('yyyy-MM-01')

@description('APNs key id (the 10 character id of the .p8 key). Not secret.')
param apnsKeyId string = ''

@description('Apple developer team id. Not secret.')
param apnsTeamId string = ''

@description('Bundle id of the iOS app, used as the apns-topic.')
param apnsBundleId string = 'io.github.mogarber.hourlypricealerts'

@description('Daily Log Analytics ingestion cap in GB. Application Insights is workspace based, so this caps both.')
param dailyCapGb string = '0.1'

@description('Optional. Object id of a user or group to give Key Vault Secrets Officer, so they can set the APNs secret. Leave empty to grant yourself the role separately.')
param keyVaultAdminObjectId string = ''

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
}

module resources 'modules/resources.bicep' = {
  name: 'app-resources'
  scope: rg
  params: {
    location: location
    namePrefix: namePrefix
    apnsKeyId: apnsKeyId
    apnsTeamId: apnsTeamId
    apnsBundleId: apnsBundleId
    dailyCapGb: dailyCapGb
    keyVaultAdminObjectId: keyVaultAdminObjectId
  }
}

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = if (!empty(budgetContactEmail)) {
  name: '${namePrefix}-monthly'
  properties: {
    category: 'Cost'
    amount: budgetAmount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: budgetStartDate
    }
    notifications: {
      actual80: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 80
        thresholdType: 'Actual'
        contactEmails: [
          budgetContactEmail
        ]
      }
      actual100: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        thresholdType: 'Actual'
        contactEmails: [
          budgetContactEmail
        ]
      }
      forecast100: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [
          budgetContactEmail
        ]
      }
    }
  }
}

output resourceGroupName string = rg.name
output functionAppName string = resources.outputs.functionAppName
output functionAppHostName string = resources.outputs.functionAppHostName
output keyVaultName string = resources.outputs.keyVaultName
output storageAccountName string = resources.outputs.storageAccountName
