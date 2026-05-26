using BrowserMcpServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

var headless = args.Contains("--headless") || args.Contains("-h");
var proxy = args.FirstOrDefault(a => a.StartsWith("--proxy="))?.Substring("--proxy=".Length);

builder.Logging
    .ClearProviders()
    .AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddHttpClient()
    .AddSingleton<BrowserService>(_ => new BrowserService(headless: headless, proxy: proxy))
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var logger = builder.Services.BuildServiceProvider().GetService<ILogger<Program>>();
logger?.LogInformation("Запуск в режиме: {Mode}", headless ? "Headless" : "Visible");

await builder.Build().RunAsync();