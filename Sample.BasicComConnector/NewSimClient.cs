using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;

using CogniteSdk.Alpha;

using Microsoft.Extensions.Logging;
using System.IO.Compression;

public class NewSimClient : AutomationClient, ISimulatorClient<DefaultModelFilestate, SimulatorRoutineRevision>
{
    private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

    private readonly string _version = "N/A";
    private readonly ILogger logger;

    public NewSimClient(ILogger<NewSimClient> logger, DefaultConfig<NewSimAutomationConfig> config)
            : base(logger, config?.Automation)
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

    public Task TestConnection(CancellationToken _token)
    {
        return Task.CompletedTask;
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

    private (string excelFilePath, string extractedDirectory) ExtractZipAndFindExcelFile(string zipFilePath)
    {
        try
        {
            // Create a unique temporary directory for extraction
            var tempDir = Path.Combine(Path.GetTempPath(), $"PlaceiT_Extract_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            
            logger.LogInformation($"Extracting PlaceiT package to: {tempDir}");
            
            // Extract the zip file
            ZipFile.ExtractToDirectory(zipFilePath, tempDir);
            
            // Validate and log the extracted package contents
            ValidatePackageContents(tempDir);
            
            // Find the Excel simulator file (.xlsx or .xlsm) - should be exactly one
            var xlsxFiles = Directory.GetFiles(tempDir, "*.xlsx", SearchOption.AllDirectories);
            var xlsmFiles = Directory.GetFiles(tempDir, "*.xlsm", SearchOption.AllDirectories);
            var excelFiles = xlsxFiles.Concat(xlsmFiles).ToArray();
            
            if (excelFiles.Length == 0)
            {
                throw new FileNotFoundException("No Excel simulator file (.xlsx or .xlsm) found in the PlaceiT package");
            }
            
            if (excelFiles.Length == 1)
            {
                logger.LogInformation($"Found PlaceiT Excel simulator: {Path.GetFileName(excelFiles[0])}");
                return (excelFiles[0], tempDir);
            }
            
            // This shouldn't happen based on the expected package structure, but handle it gracefully
            logger.LogWarning($"Unexpected: Multiple Excel files found in package. Using first one: {Path.GetFileName(excelFiles[0])}");
            logger.LogWarning($"All Excel files: {string.Join(", ", excelFiles.Select(Path.GetFileName))}");
            
            return (excelFiles[0], tempDir);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to extract PlaceiT package: {zipFilePath}");
            throw new InvalidOperationException($"Could not extract PlaceiT package: {ex.Message}", ex);
        }
    }

    private void ValidatePackageContents(string extractedDir)
    {
        try
        {
            // Log all extracted files for debugging
            var allFiles = Directory.GetFiles(extractedDir, "*", SearchOption.AllDirectories);
            logger.LogDebug($"Extracted {allFiles.Length} files from PlaceiT package");
            
            // Count expected file types
            var applications = allFiles.Where(f => Path.GetExtension(f).ToLower() == ".exe").ToList();
            var dlls = allFiles.Where(f => Path.GetExtension(f).ToLower() == ".dll").ToList();
            var sifFiles = allFiles.Where(f => Path.GetExtension(f).ToLower() == ".sif").ToList();
            var xlsxFiles = allFiles.Where(f => Path.GetExtension(f).ToLower() == ".xlsx").ToList();
            var xlsmFiles = allFiles.Where(f => Path.GetExtension(f).ToLower() == ".xlsm").ToList();
            var excelFiles = xlsxFiles.Concat(xlsmFiles).ToList();
            
            // Log findings
            logger.LogInformation($"Package contents validation:");
            logger.LogInformation($"  - Applications (.exe): {applications.Count} found");
            logger.LogInformation($"  - DLL files (.dll): {dlls.Count} found (expected: 2)");
            logger.LogInformation($"  - SIF files (.sif): {sifFiles.Count} found (expected: 1)");
            logger.LogInformation($"  - Excel simulator (.xlsx/.xlsm): {excelFiles.Count} found (expected: 1)");
            
            // Log specific filenames for troubleshooting
            if (applications.Any())
                logger.LogDebug($"  Applications: {string.Join(", ", applications.Select(Path.GetFileName))}");
            if (dlls.Any())
                logger.LogDebug($"  DLL files: {string.Join(", ", dlls.Select(Path.GetFileName))}");
            if (sifFiles.Any())
                logger.LogDebug($"  SIF files: {string.Join(", ", sifFiles.Select(Path.GetFileName))}");
            if (excelFiles.Any())
                logger.LogDebug($"  Excel files: {string.Join(", ", excelFiles.Select(Path.GetFileName))}");
            
            // Warn about missing expected files (but don't fail - let simulation attempt proceed)
            if (dlls.Count != 2)
                logger.LogWarning($"Expected 2 DLL files, found {dlls.Count}. PlaceiT functionality may be affected.");
            if (sifFiles.Count != 1)
                logger.LogWarning($"Expected 1 SIF file, found {sifFiles.Count}. PlaceiT configuration may be affected.");
            if (excelFiles.Count != 1)
                logger.LogWarning($"Expected 1 Excel file (.xlsx or .xlsm), found {excelFiles.Count}. This may cause issues.");
            if (applications.Count == 0)
                logger.LogInformation("No application (.exe) files found - this may be normal depending on PlaceiT installation.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to validate package contents, but continuing with extraction");
        }
    }

    public async Task ExtractModelInformation(DefaultModelFilestate state, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(state);
        await semaphore.WaitAsync(token).ConfigureAwait(false);
        string? extractedDirectory = null;
        try
        {
            Initialize();
            
            // Extract zip file and find the main Excel file
            (string excelFilePath, extractedDirectory) = ExtractZipAndFindExcelFile(state.FilePath);
            
            dynamic workbook = OpenBook(excelFilePath);
            if (workbook != null)
            {
                // Check if PlaceiT COM functionality is available in this workbook
                bool placeitAvailable = CheckPlaceiTAvailability(workbook);
                
                workbook.Close(false);
                
                if (!placeitAvailable)
                {
                    throw new InvalidOperationException($"PlaceiT COM functionality not detected in workbook: {state.FilePath}. Ensure the .xlsm file contains the PlaceiTCOMEntryPoint macro.");
                }
                
                logger.LogInformation($"PlaceiT COM functionality detected in extracted workbook from: {state.FilePath}");
                return;
            }
            throw new FileNotFoundException($"No Excel file found in ZIP package: {state.FilePath}");
        }
        finally
        {
            // Clean up extracted directory after validation
            if (extractedDirectory != null && Directory.Exists(extractedDirectory))
            {
                try
                {
                    Directory.Delete(extractedDirectory, recursive: true);
                    logger.LogDebug($"Cleaned up extracted directory: {extractedDirectory}");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, $"Failed to cleanup extracted directory: {extractedDirectory}");
                }
            }
            
            Shutdown();
            semaphore.Release();
        }
    }

    private bool CheckPlaceiTAvailability(dynamic workbook)
    {
        try
        {
            // Try to access PlaceiT COM functionality
            // This could be through COM add-ins, VBA modules, or direct COM references
            var application = workbook.Application;
            
            // Check for PlaceiT COM add-in
            try
            {
                var comAddIns = application.COMAddIns;
                for (int i = 1; i <= comAddIns.Count; i++)
                {
                    var addIn = comAddIns.Item(i);
                    if (addIn.Description?.Contains("PlaceiT") == true || 
                        addIn.ProgId?.Contains("PlaceiT") == true)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not check COM add-ins for PlaceiT");
            }

            // Check for VBA modules that might contain PlaceiT functions
            try
            {
                var vbaProject = workbook.VBProject;
                var vbaComponents = vbaProject.VBComponents;
                for (int i = 1; i <= vbaComponents.Count; i++)
                {
                    var component = vbaComponents.Item(i);
                    var codeModule = component.CodeModule;
                    var lineCount = codeModule.CountOfLines;
                    
                    for (int line = 1; line <= Math.Min(lineCount, 100); line++) // Check first 100 lines for performance
                    {
                        var lineText = codeModule.Lines[line, 1];
                        if (lineText.Contains("PlaceiTCOMEntryPoint"))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not check VBA modules for PlaceiT");
            }

            return false; // PlaceiT functionality not found
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error checking for PlaceiT availability");
            return false;
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

    public async Task<Dictionary<string, SimulatorValueItem>> RunSimulation(DefaultModelFilestate modelState, SimulatorRoutineRevision routineRev, Dictionary<string, SimulatorValueItem> inputData, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(modelState);
        await semaphore.WaitAsync(token).ConfigureAwait(false);
        dynamic? workbook = null;
        string? extractedDirectory = null;
        try
        {
            Initialize();
            
            // Extract zip file and find the main Excel file
            (string excelFilePath, extractedDirectory) = ExtractZipAndFindExcelFile(modelState.FilePath);
            
            workbook = OpenBook(excelFilePath);

            var routine = new NewSimRoutine(workbook, routineRev, inputData, logger);
            return routine.PerformSimulation(token);
        }
        finally
        {
            if (workbook != null)
            {
                try
                {
                    workbook.Close(false);
                }
                catch (System.Runtime.InteropServices.COMException ex)
                {
                    logger.LogWarning(ex, "Error closing workbook (simulation may have completed successfully)");
                }
                
                // Clean up extracted directory after closing workbook
                if (extractedDirectory != null && Directory.Exists(extractedDirectory))
                {
                    try
                    {
                        Directory.Delete(extractedDirectory, recursive: true);
                        logger.LogDebug($"Cleaned up extracted directory: {extractedDirectory}");
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, $"Failed to cleanup extracted directory: {extractedDirectory}");
                    }
                }
            }
            
            Shutdown();
            semaphore.Release();
        }
    }
}
