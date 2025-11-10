using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public class Program
{
    public static void Main(string[] args)
    {
        var services = new ServiceCollection();
        ConfigureServices(services);

        var serviceProvider = services.BuildServiceProvider();
        var worker = serviceProvider.GetService<AntivirusWorker>();
        worker.Run();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddTransient<AntivirusWorker>();

        // Optional: Add logging if needed, e.g., to a file or the event log
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddEventLog();
        });
    }
}
