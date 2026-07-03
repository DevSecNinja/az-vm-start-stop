using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using AzVmStartStop.Functions.Options;
using AzVmStartStop.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureLogging(logging =>
    {
        // The .NET isolated worker's Application Insights integration installs a
        // default filter rule that drops all logs below Warning for the App
        // Insights provider. Remove it so our Information-level diagnostics
        // (e.g. "Schedule pass complete. Scanned=...") actually reach App Insights.
        logging.Services.Configure<LoggerFilterOptions>(options =>
        {
            var defaultRule = options.Rules.FirstOrDefault(rule =>
                rule.ProviderName ==
                "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");
            if (defaultRule is not null)
            {
                options.Rules.Remove(defaultRule);
            }
        });
    })
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
