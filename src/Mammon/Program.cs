global using Azure;
global using Azure.Core;
global using Azure.Identity;
global using Azure.ResourceManager;
global using Azure.ResourceManager.Resources;
global using Dapr;
global using Dapr.Actors;
global using Dapr.Actors.Client;
global using Dapr.Actors.Runtime;
global using Dapr.Client;
global using Dapr.Workflow;
global using FluentValidation;
global using Grpc.Core;
global using Mammon;
global using Mammon.Actors;
global using Mammon.Extensions;
global using Mammon.Models.Actors;
global using Mammon.Models.CostManagement;
global using Mammon.Models.Views;
global using Mammon.Models.Workflows;
global using Mammon.Models.Workflows.Activities;
global using Mammon.Services;
global using Mammon.Utils;
global using Mammon.Workflows;
global using Mammon.Workflows.Activities;
global using Microsoft.ApplicationInsights;
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.AspNetCore.Mvc.Controllers;
global using Polly;
global using Polly.Extensions.Http;
global using Polly.Retry;
global using System.Data;
global using System.Diagnostics;
global using System.Net;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using Westwind.AspNetCore.Views;

#if (DEBUG)
Debugger.Launch();
#endif


var builder = WebApplication.CreateBuilder(args);

var configKVURL = builder.Configuration[Consts.ConfigKeyVaultConfigEnvironmentVariable]?.ToString();
if (string.IsNullOrWhiteSpace(configKVURL))
    throw new InvalidOperationException($"{Consts.ConfigKeyVaultConfigEnvironmentVariable} environment variable is not set");

builder.Configuration.AddAzureKeyVault(
    new Uri(configKVURL),
    new DefaultAzureCredential());

builder.Services.AddApplicationInsightsTelemetry();

builder.Services.AddRazorPages();

builder.Services.AddControllers();

builder.Services
    .AddDaprWorkflow((config) => { 
        config.RegisterWorkflow<SubscriptionWorkflow>();
        config.RegisterWorkflow<ResourceGroupSubWorkflow>();
        config.RegisterWorkflow<TenantWorkflow>();

        config.RegisterActivity<ObtainCostsActivity>();
        config.RegisterActivity<CallResourceActorActivity>();
        config.RegisterActivity<AssignCostCentreActivity>();
    })
    .AddActors(options => {
        // Register actor types and configure actor settings
        options.Actors.RegisterActor<ResourceActor>();
        options.Actors.RegisterActor<CostCentreActor>();
        options.ReentrancyConfig = new ActorReentrancyConfig()
        {
            Enabled = false
        };
    });

builder.Services
    .AddTransient((sp) => new ArmClient(new DefaultAzureCredential()))
    .AddTransient<AzureAuthHandler>()
    .AddSingleton<CostCentreRuleEngine>()
    .AddSingleton<CostCentreReportService>();

var policy = HttpPolicyExtensions
    .HandleTransientHttpError() // HttpRequestException, 5XX and 408
    .OrResult(response => (int)response.StatusCode == 429) // RetryAfter
    .AddCostManagementRetryPolicy();

builder.Services
    .AddHttpClient<CostManagementService>()
    .AddHttpMessageHandler<AzureAuthHandler>()
    .AddPolicyHandler(policy);

var app = builder.Build();

app.UseRouting();

app.MapActorsHandlers();

app.MapRazorPages();

app.MapControllers();

app.MapSubscribeHandler();


app.Lifetime.ApplicationStopped.Register(() => app.Services.GetRequiredService<TelemetryClient>().FlushAsync(default).Wait());

app.Lifetime.ApplicationStarted.Register(async () =>
{
    //var service = app.Services.GetRequiredService<CostCentreReportService>();
    //var report = await service.GenerateReportAsync("test14");
});

app.Run();
