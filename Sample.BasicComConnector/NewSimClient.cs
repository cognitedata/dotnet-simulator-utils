using Cognite.Simulator.Utils;
using CogniteSdk.Alpha;

public class NewSimClient : ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>
{
    public Task ExtractModelInformation(DefaultModelFilestate state, CancellationToken _token)
    {
        throw new NotImplementedException();
    }

    public string GetConnectorVersion()
    {
        return "N/A";
    }

    public string GetSimulatorVersion()
    {
        return "N/A";
    }

    public Task<Dictionary<string, SimulatorValueItem>> RunSimulation(DefaultModelFilestate modelState, SimulatorRoutineRevision simulationConfiguration, Dictionary<string, SimulatorValueItem> inputData)
    {
        throw new NotImplementedException();
    }
}
