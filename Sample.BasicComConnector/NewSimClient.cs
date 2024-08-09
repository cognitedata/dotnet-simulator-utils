using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;
using CogniteSdk.Alpha;
using Microsoft.Extensions.Logging;

public class NewSimClient : AutomationClient, ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>
{
    private readonly object simulatorLock = new object();
    private readonly string _version = "N/A";

    public NewSimClient(ILogger<NewSimClient> logger, DefaultConfig<NewSimAutomationConfig> config)
            : base(logger, config.Automation)
    {
        lock (simulatorLock)
        {
            try
            {
                Initialize();
                _version = Server.Version;
            }
            finally
            {
                Shutdown();
            }
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

    public async Task ExtractModelInformation(DefaultModelFilestate state, CancellationToken _token)
    {
        lock (simulatorLock)
        {
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
            }
        }
    }

    public string GetConnectorVersion()
    {
        return "N/A";
    }

    public string GetSimulatorVersion()
    {
        return _version;
    }

    public Task<Dictionary<string, SimulatorValueItem>> RunSimulation(DefaultModelFilestate modelState, SimulatorRoutineRevision simulationConfiguration, Dictionary<string, SimulatorValueItem> inputData)
    {
        throw new NotImplementedException();
    }
}
