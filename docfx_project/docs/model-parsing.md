# Implement Model Parsing

This guide explains how to implement the `ExtractModelInformation` method to validate simulator model files and optionally extract metadata.

## What is Model Parsing?

Model parsing is the process of validating/parsing if the file associated with a given simulator model revision is valid. Besides checking if the file is valid, you can also extract useful information from a model file, such as the flowsheet structure (nodes, edges, thermodynamics) and arbitrary metadata (info).

### Minimum Requirements

At a minimum, your `ExtractModelInformation` implementation must:

1. **Validate the file exists and can be opened**
2. **Set success or failure status**

## Basic Implementation

Here's a minimal implementation that just validates the file:

```csharp
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
```

## Optional: Extract Flowsheets

If your model contains a flowsheet, you can optionally extract **flowsheet** information from the model. This creates a browsable structure in CDF that users can reference when creating routines.

### Flowsheet Structure

The API supports saving:

- **Flowsheets** - Hierarchical structure of simulator objects
  - **Nodes** - Objects in the flowsheet (streams, unit operations, etc.)
  - **Edges** - Connections between nodes
  - **Thermodynamics** - Thermodynamic package information

### When to Extract Flowsheets

Extract flowsheets when:
- Your simulator has a clear object model (streams, units, operations)
- Users would benefit from browsing model structure in CDF
- You want to enable validation of routine references
- The simulator API makes extraction straightforward

<!-- TODO: Uncomment when DWSIM connector implements flowsheet extraction
## Example: Flowsheet Extraction

For a complete example of flowsheet extraction, see the **[DWSIM Connector](https://github.com/cognitedata/dwsim-connector-dotnet)** which extracts:
- Nodes with properties and graphical information
- Connections between nodes
- Thermodynamic package information

The DWSIM implementation shows how to:
1. Iterate through simulator objects
2. Map them to the flowsheet structure
3. Extract relevant properties
4. Build the node/edge relationships
-->

## Optional: Extract Info

You can also optionally extract arbitrary metadata about the model using the `info` field. This is a key-value structure that can hold any JSON-serializable data.

### Info Structure

```csharp
var info = new Dictionary<string, string>
{
    ["ModelVersion"] = "2.1",
    ["CreatedDate"] = "2024-01-15",
    ["Author"] = "John Doe",
    ["Description"] = "Distillation column model",
    ["CustomField"] = "Custom value"
};
```

### When to Use Info

Use `info` for:
- Model metadata that doesn't fit the flowsheet structure
- Version information
- Author/creation details
- Custom simulator-specific data
- Any JSON-serializable information

## Summary

For the Excel connector tutorial, our basic implementation simply validates that the workbook can be opened. This is sufficient for most use cases.

<!-- TODO: Uncomment when DWSIM connector implements flowsheet extraction
For more advanced scenarios, consult the [DWSIM Connector](https://github.com/cognitedata/dwsim-connector-dotnet) source code to see how to extract comprehensive flowsheet information.
-->

---

**Next:** Continue to [Implement Routines](implement-routine.md) to add simulation execution capabilities.
