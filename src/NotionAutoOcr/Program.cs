using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NotionAutoOcr;
using NotionAutoOcr.Ocr;

// ── Validate config eagerly so startup fails with a clear message ────────────
Config config;
try
{
    config = new Config();
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"[FATAL] Configuration error: {ex.Message}");
    return 1;
}

// ── Build and run the host ───────────────────────────────────────────────────
var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(
            config.Debug ? LogLevel.Debug : LogLevel.Information);
    })
    .ConfigureServices(services =>
    {
        services.AddSingleton(config);

        // Named HttpClient for image downloads — no auth headers leaking.
        services.AddHttpClient("notion-images");

        // HttpClient for the Notion API — typed client exposed via interface.
        services.AddHttpClient<INotionClient, NotionClient>((sp, http) =>
        {
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.NotionToken}");
            http.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");
        });

        // Select OCR provider based on config.
        if (config.OcrProvider == OcrProviderType.IronOcr)
            services.AddSingleton<IOcrProvider, IronOcrProvider>();
        else
            services.AddSingleton<IOcrProvider, AzureOcrProvider>();

        services.AddHostedService<ScanWorker>();
    })
    .Build();

await host.RunAsync();
return 0;

