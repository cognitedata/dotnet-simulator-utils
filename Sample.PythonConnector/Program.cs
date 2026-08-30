namespace Sample.PythonConnector;

public class Program
{
    public static int Main(string[] args)
    {
        try
        {
            ConnectorRuntime.RunStandalone().Wait();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.Error.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }
            return 1;
        }
    }
}
