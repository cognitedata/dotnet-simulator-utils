using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class Program
{
    public static int Main(string[] args)
    {
        // Detect if running as Windows service or standalone
        // Windows services run non-interactively, or can be explicitly requested with --service flag
        bool isService = args.Contains("--service") || !Environment.UserInteractive;
        
        if (isService)
        {
            // Run as Windows service
            CreateHostBuilder(args).Build().Run();
        }
        else
        {
            // Run as standalone/interactive application
            RunStandalone();
        }
        return 0;
    }

    private static void RunStandalone()
    {
        ConnectorRuntime.RunStandalone().Wait();
    }

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseWindowsService(options =>
            {
                options.ServiceName = "PlaceiT Simulator Connector";
            })
            .ConfigureServices((hostContext, services) =>
            {
                services.AddHostedService<ConnectorServiceHost>();
            })
            .ConfigureLogging((context, logging) =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                
                // Event Log is Windows-only (this application is Windows-only due to Excel COM)
                if (OperatingSystem.IsWindows())
                {
                    logging.AddEventLog(settings =>
                    {
                        settings.SourceName = "PlaceiT Connector";
                    });
                }
            });
}
