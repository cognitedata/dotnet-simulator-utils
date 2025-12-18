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
            : base(logger, config.Automation)
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

    public async Task ExtractModelInformation(DefaultModelFilestate state, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(state);
        await semaphore.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Initialize();
            dynamic workbook = OpenBook(state.FilePath);
            if (workbook != null)
            {
                workbook.Close(false);
                state.ParsingInfo.SetSuccess();
                return;
            }
            state.ParsingInfo.SetFailure();
        }
        finally
        {
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
