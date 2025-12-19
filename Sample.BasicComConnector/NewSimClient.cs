using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;

using CogniteSdk.Alpha;

using Microsoft.Extensions.Logging;

public class NewSimClient : AutomationClient, ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>
{
    private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
    private string _version = "N/A";
    private readonly ILogger logger;

    public NewSimClient(ILogger<NewSimClient> logger, DefaultConfig<NewSimAutomationConfig> config)
            : base(logger, config?.Automation)
    {
        this.logger = logger;
    }

    public async Task TestConnection(CancellationToken _token)
    {
        await semaphore.WaitAsync(_token).ConfigureAwait(false);
        try
        {
            Initialize();
            _version = Server.Version;
        }
        finally
        {
            Shutdown();
            semaphore.Release();
        }
    }

    protected override void PreShutdown()
    {
        Server.Quit();
    }

    public dynamic OpenBook(string path)
    {
        dynamic workbooks = Server.Workbooks;
        return workbooks.Open(path);
    }

    public async Task ExtractModelInformation(
        DefaultModelFilestate state,
        CancellationToken token)
    {
        await semaphore.WaitAsync(token).ConfigureAwait(false);
        dynamic? workbook = null;

        try
        {
            Initialize();

            logger.LogInformation($"Validating model: {state.FilePath}");

            // Just try to open the file
            workbook = OpenBook(state.FilePath);

            if (workbook == null)
            {
                state.ParsingInfo.SetFailure("Failed to open model file");
                return;
            }

            // File is valid - report success with no extracted data
            state.ParsingInfo.SetSuccess();
            logger.LogInformation("Model validation successful");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error validating model");
            state.ParsingInfo.SetFailure(ex.Message);
        }
        finally
        {
            if (workbook != null)
            {
                try
                {
                    workbook.Close(false);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error closing workbook");
                }
            }
            Shutdown();
            semaphore.Release();
        }
    }
    public string GetConnectorVersion(CancellationToken _token)
    {
        return "N/A";
    }

    public string GetSimulatorVersion(CancellationToken _token)
    {
        return _version;
    }

    public async Task<Dictionary<string, SimulatorValueItem>?> RunSimulation(DefaultModelFilestate modelState, SimulatorRoutineRevision routineRev, Dictionary<string, SimulatorValueItem> inputData, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(modelState);
        await semaphore.WaitAsync(token).ConfigureAwait(false);
        dynamic? workbook = null;
        try
        {
            Initialize();
            workbook = OpenBook(modelState.FilePath);

            var routine = new NewSimRoutine(workbook, routineRev, inputData, logger);
            return routine.PerformSimulation(token);
        }
        finally
        {
            if (workbook != null)
            {
                workbook.Close(false);
            }
            Shutdown();
            semaphore.Release();
        }
    }
}
