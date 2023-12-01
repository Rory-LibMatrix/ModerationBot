// See https://aka.ms/new-console-template for more information

using LibMatrix.Services;
using LibMatrix.Utilities.Bot;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModerationBot;

Console.WriteLine("Hello, World!");

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureHostOptions(host => {
    host.ServicesStartConcurrently = true;
    host.ServicesStopConcurrently = true;
    host.ShutdownTimeout = TimeSpan.FromSeconds(5);
});

if (Environment.GetEnvironmentVariable("MODERATIONBOT_APPSETTINGS_PATH") is string path)
    builder.ConfigureAppConfiguration(x => x.AddJsonFile(path));

var host = builder.ConfigureServices((_, services) => {
    services.AddScoped<TieredStorageService>(x =>
        new TieredStorageService(
            cacheStorageProvider: new FileStorageProvider("bot_data/cache/"),
            dataStorageProvider: new FileStorageProvider("bot_data/data/")
        )
    );
    services.AddSingleton<ModerationBotConfiguration>();

    services.AddRoryLibMatrixServices();
    services.AddBot(withCommands: true);

    services.AddSingleton<PolicyEngine>();

    services.AddHostedService<ModerationBot.ModerationBot>();
}).UseConsoleLifetime().Build();

await host.RunAsync();