using Azure.Core;
using Azure.Identity;
using FoundryModelExplorer.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ExplorerOptions>(_ => ExplorerOptions.FromEnvironment());

// One credential for everything that talks to Azure Resource Manager.
// Locally this is the az / azd / Visual Studio login; in Azure it is the user-assigned managed identity
// (AZURE_CLIENT_ID is set by the Bicep template so DefaultAzureCredential picks the right identity).
builder.Services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential(new DefaultAzureCredentialOptions
{
    ExcludeInteractiveBrowserCredential = true
}));

builder.Services.AddHttpClient("arm", client =>
{
    client.BaseAddress = new Uri("https://management.azure.com/");
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddHttpClient("prices", client =>
{
    client.BaseAddress = new Uri("https://prices.azure.com/");
    client.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddSingleton<ArmGateway>();
builder.Services.AddSingleton<RegionService>();
builder.Services.AddSingleton<PricingService>();
builder.Services.AddSingleton<CatalogService>();
builder.Services.AddSingleton<DeploymentService>();
builder.Services.AddSingleton<SampleData>();

builder.Build().Run();
