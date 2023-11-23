// See https://aka.ms/new-console-template for more information

using LibMatrix.Services;
using LibMatrix.Utilities.Bot;
using ModerationBot;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

Console.WriteLine("Hello, World!");

var host = Host.CreateDefaultBuilder(args).ConfigureServices((_, services) => {
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
