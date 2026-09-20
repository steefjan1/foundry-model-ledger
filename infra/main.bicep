targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the azd environment; used to derive resource names.')
param environmentName string

@minLength(1)
@description('Primary location for all resources.')
param location string

@description('Region the explorer opens on. Any region that hosts Microsoft.CognitiveServices/accounts.')
param defaultRegion string = 'swedencentral'

@description('Currency code for the Retail Prices API (USD, EUR, ...).')
param priceCurrency string = 'USD'

@description('Resource group name. Defaults to rg-<environmentName>.')
param resourceGroupName string = ''

var abbrs = loadJsonContent('abbreviations.json')
var resourceToken = toLower(uniqueString(subscription().id, environmentName, location))
var tags = { 'azd-env-name': environmentName, project: 'foundry-model-explorer' }

resource rg 'Microsoft.Resources/resourceGroups@2022-09-01' = {
  name: !empty(resourceGroupName) ? resourceGroupName : '${abbrs.resourcesResourceGroups}${environmentName}'
  location: location
  tags: tags
}

module app 'app.bicep' = {
  name: 'app'
  scope: rg
  params: {
    location: location
    tags: tags
    resourceToken: resourceToken
    abbrs: abbrs
    defaultRegion: defaultRegion
    priceCurrency: priceCurrency
  }
}

// The explorer reads the model catalog, provider manifest, subscription locations and Resource Graph.
// Reader on the subscription covers all four (Resource Graph honours the caller's RBAC).
// A separate module: a role assignment's name must be known at deployment start, and the identity's
// principal id is a runtime output, so the guid() is computed inside the module instead (BCP120).
module readerAssignment 'reader-role.bicep' = {
  name: 'reader-role'
  params: {
    principalId: app.outputs.identityPrincipalId
  }
}

output AZURE_LOCATION string = location
output AZURE_RESOURCE_GROUP string = rg.name
output AZURE_FUNCTION_APP_NAME string = app.outputs.functionAppName
output SERVICE_API_ENDPOINT string = app.outputs.functionAppUrl
output APPLICATIONINSIGHTS_CONNECTION_STRING string = app.outputs.appInsightsConnectionString
