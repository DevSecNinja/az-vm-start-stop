using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using AzVmStartStop.Functions.Options;
using AzVmStartStop.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        services.AddOptions<AutoScheduleOptions>()
            .Bind(context.Configuration.GetSection(AutoScheduleOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Managed identity in Azure; developer/CLI credentials locally.
        // Set AZURE_CLIENT_ID to pin a specific user-assigned identity.
        services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
        services.AddSingleton(sp => new ArmClient(sp.GetRequiredService<TokenCredential>()));

        services.AddSingleton<ICronScheduleEvaluator, CronScheduleEvaluator>();
        services.AddSingleton<IVmScheduleService, VmScheduleService>();
    })
    .Build();

host.Run();
