using Azure.Data.Tables;
using Azure.Identity;
using HourlyPriceAlerts.Core;
using HourlyPriceAlerts.Functions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();
builder.UseMiddleware<ExceptionLogging>();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// By default the Application Insights provider only forwards Warning and above; host.json controls the volume instead.
builder.Services.Configure<LoggerFilterOptions>(options =>
{
    var rule = options.Rules.FirstOrDefault(r =>
        r.ProviderName == "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");
    if (rule is not null)
    {
        options.Rules.Remove(rule);
    }
});

// One line per request stage adds up on a 0.1 GB daily log cap.
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

var configuration = builder.Configuration;

var apnsOptions = new ApnsOptions();
configuration.GetSection("Apns").Bind(apnsOptions);

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton(apnsOptions);
builder.Services.AddSingleton<IAlertStore>(_ => new TableStore(CreateTableService(configuration)));
builder.Services.AddSingleton<IFeedClient>(_ => new ComEdFeedClient(CreateHttpClient(TimeSpan.FromSeconds(20))));
builder.Services.AddSingleton<IPushSender>(sp => new ApnsClient(
    CreateHttpClient(TimeSpan.FromSeconds(30)),
    sp.GetRequiredService<ApnsOptions>(),
    sp.GetRequiredService<IClock>(),
    sp.GetRequiredService<IAlertStore>(),
    sp.GetRequiredService<ILogger<ApnsClient>>()));
builder.Services.AddSingleton<DeviceService>();
builder.Services.AddSingleton<PollService>();
builder.Services.AddSingleton<HealthService>();

builder.Build().Run();

static TableServiceClient CreateTableService(IConfiguration configuration)
{
    var connectionString = configuration["Storage:ConnectionString"];
    if (!string.IsNullOrEmpty(connectionString))
    {
        return new TableServiceClient(connectionString);
    }

    var serviceUri = configuration["Storage:TableServiceUri"];
    if (!string.IsNullOrEmpty(serviceUri))
    {
        return new TableServiceClient(new Uri(serviceUri), new DefaultAzureCredential());
    }

    throw new InvalidOperationException(
        "Set Storage__TableServiceUri (Azure) or Storage__ConnectionString (local development).");
}

// One long-lived client per service, with a connection lifetime so DNS changes are picked up.
static HttpClient CreateHttpClient(TimeSpan timeout)
    => new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) }) { Timeout = timeout };
