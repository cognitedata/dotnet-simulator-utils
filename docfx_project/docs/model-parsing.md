# Implement model parsing

To run simulations using the Cognite simulator integration, open the model file and extract the necessary information from it.

In the below example, you use an empty Excel sheet as a model file.

### Implement COM call to open the Excel file:

Copy the below method to the `NewSimClient` class.

```csharp
public dynamic OpenBook(string path)
{
    dynamic workbooks = Server.Workbooks;
    return workbooks.Open(path);
}
```

### Implement the ExtractModelInformation method

The `ExtractModelInformation` method extracts the model information from the simulator.
In the example below, we'll open the Excel `.xlsx` file.

If the file is opened successfully, set the parsing information to `success`; otherwise, set it to `failure`.

In the `NewSimClient` class:

```csharp
public async Task ExtractModelInformation(DefaultModelFilestate state, CancellationToken _token)
    {
        await semaphore.WaitAsync(_token).ConfigureAwait(false);
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
```
Note: Make sure to close the workbook after opening it.

The boolean parameter in `workbook.Close(false)` ensures that workbook changes aren't saved.
If we fail to close the file, it won't be possible to run multiple simulations with the same model.

Updated `NewSimClient` class:

```csharp
using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;
using CogniteSdk.Alpha;
using Microsoft.Extensions.Logging;

public class NewSimClient : AutomationClient, ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>
{
    private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
    private readonly string _version = "N/A";
    private readonly ILogger logger;

    public NewSimClient(ILogger<NewSimClient> logger, DefaultConfig<NewSimAutomationConfig> config)
            : base(logger, config.Automation)
    {
        this.logger = logger;
        semaphore.Wait();
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

    public dynamic OpenBook(string path)
    {
        dynamic workbooks = Server.Workbooks;
        return workbooks.Open(path);
    }

    public async Task ExtractModelInformation(DefaultModelFilestate state, CancellationToken _token)
    {
        await semaphore.WaitAsync(_token).ConfigureAwait(false);
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

    protected override void PreShutdown()
    {
        Server.Quit();
    }
}
```

Now, use CDF to upload an empty Excel file and monitor its status.

To upload the file, see below:
![Model upload](../images/model-upload.png)

You have parsed the model.
![Model parsing](../images/model-parsing.png)
