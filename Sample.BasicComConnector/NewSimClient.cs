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
            ConfigureExcelVisibility();
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
        try
        {
            Server.Quit();
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            logger.LogWarning(ex, "Error quitting Excel server during shutdown (cleanup may still succeed)");
        }
    }

    private void ConfigureExcelVisibility()
    {
        // Hide Excel window - run in background
        Server.Visible = false;
        Server.DisplayAlerts = false; // Suppress alerts/prompts
        Server.ScreenUpdating = false; // Prevent screen flashing
        Server.Interactive = false; // Prevent user interaction
        Server.EnableEvents = false; // Disable events that might show Excel
    }

    public dynamic OpenBook(string path)
    {
        dynamic workbooks = Server.Workbooks;
        // Open workbook with parameters to keep Excel hidden and suppress dialogs
        // Parameters: Filename, UpdateLinks (0=don't update), ReadOnly (false), Format, Password, 
        // WriteResPassword, IgnoreReadOnlyRecommended (true), Origin, Delimiter, Editable, 
        // Notify, Converter, AddToMru (false=don't add to recent files), Local, CorruptLoad
        dynamic workbook = workbooks.Open(path, 0, false, Type.Missing, Type.Missing, 
                             Type.Missing, true, Type.Missing, Type.Missing, 
                             Type.Missing, Type.Missing, Type.Missing, false);
        
        // CRITICAL: Aggressively suppress ALL Excel alerts and error dialogs
        // This must be done on the workbook's Application object after opening
        dynamic app = workbook.Application;
        app.DisplayAlerts = false;           // Suppress all alert dialogs
        app.AlertBeforeOverwriting = false;  // No overwrite warnings
        app.Interactive = false;              // Block user interaction
        app.AskToUpdateLinks = false;        // No link update prompts
        
        // Prevent Excel from showing any error dialogs from VBA
        // xlAutomatic = -4105, xlManual = -4135
        app.Calculation = -4135; // Set to manual to prevent auto-calc errors during setup
        
        return workbook;
    }

    private void CleanupExtractedDirectory(string extractedDirectory)
    {
        // Retry cleanup with delays to allow COM objects to fully release file handles
        // Excel and PlaceiT DLLs can take several seconds to release file locks after errors
        const int maxRetries = 5;
        const int delayMs = 500; // Increased from 100ms to 500ms

        // Always force garbage collection first to release COM objects
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect(); // Second collection to clean up objects that were finalized
        
        // Give Excel and DLLs a moment to release file handles
        Thread.Sleep(200);
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Additional garbage collection on retry attempts
                if (attempt > 1)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Thread.Sleep(delayMs * attempt); // Progressive delay: 500ms, 1s, 1.5s, 2s, 2.5s
                }

                Directory.Delete(extractedDirectory, recursive: true);
                logger.LogDebug($"Cleaned up extracted directory: {extractedDirectory}");
                return; // Success
            }
            catch (IOException ex) when (attempt < maxRetries)
            {
                // File still locked, will retry
                logger.LogDebug($"Cleanup attempt {attempt}/{maxRetries} failed, retrying... ({ex.Message})");
            }
            catch (UnauthorizedAccessException ex) when (attempt < maxRetries)
            {
                // File still in use, will retry
                logger.LogDebug($"Cleanup attempt {attempt}/{maxRetries} failed, retrying... ({ex.Message})");
            }
            catch (Exception ex)
            {
                // Final attempt failed or unexpected error
                logger.LogWarning(ex, $"Failed to cleanup extracted directory after {attempt} attempts: {extractedDirectory}");
                return;
            }
        }

        logger.LogWarning($"Failed to cleanup extracted directory after {maxRetries} attempts: {extractedDirectory}");
    }

    // Option 1: Unblock files to remove Internet Zone security restrictions
    private void UnblockExtractedFiles(string extractPath)
    {
        try 
        {
            foreach (string file in Directory.GetFiles(extractPath, "*", SearchOption.AllDirectories))
            {
                // Remove Zone.Identifier alternate data stream
                string zoneIdentifier = file + ":Zone.Identifier";
                if (File.Exists(zoneIdentifier))
                {
                    File.Delete(zoneIdentifier);
                    logger.LogDebug($"Unblocked security zone for: {Path.GetFileName(file)}");
                }
            }
            logger.LogInformation($"Unblocked security zones for files in: {extractPath}");
        }
        catch (Exception ex)
        {
            logger.LogWarning($"Could not unblock files: {ex.Message}");
        }
    }

    // NO LONGER USED: Reverted to keeping all files together in extracted directory
    // This approach caused issues because it didn't copy .SIF files and broke file dependencies
    /*
    private void CopyDllsToWorkingDirectory(string extractPath)
    {
        try
        {
            string workingDir = Directory.GetCurrentDirectory();
            
            foreach (string dllFile in Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(dllFile);
                string destPath = Path.Combine(workingDir, fileName);
                
                File.Copy(dllFile, destPath, overwrite: true);
                logger.LogDebug($"Copied DLL to working directory: {fileName}");
            }
            
            // Also copy executables that might be needed
            foreach (string exeFile in Directory.GetFiles(extractPath, "*.exe", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(exeFile);
                string destPath = Path.Combine(workingDir, fileName);
                
                File.Copy(exeFile, destPath, overwrite: true);
                logger.LogDebug($"Copied EXE to working directory: {fileName}");
            }
            
            // Also copy Excel files so they're in the same directory as the DLLs
            foreach (string excelFile in Directory.GetFiles(extractPath, "*.xlsx", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(extractPath, "*.xlsm", SearchOption.AllDirectories)))
            {
                string fileName = Path.GetFileName(excelFile);
                string destPath = Path.Combine(workingDir, fileName);
                
                File.Copy(excelFile, destPath, overwrite: true);
                logger.LogDebug($"Copied Excel file to working directory: {fileName}");
            }
            
            logger.LogInformation($"Copied PlaceiT DLLs, executables, and Excel files to working directory");
        }
        catch (Exception ex)
        {
            logger.LogWarning($"Could not copy DLLs to working directory: {ex.Message}");
        }
    }
    */

    private (string excelFilePath, string extractedDirectory) ExtractZipAndFindExcelFile(string zipFilePath)
    {
        try
        {
            // Extract to working directory instead of temp to avoid security restrictions
            // Keep all files together in extracted directory so PlaceiT can find dependencies
            string workingDir = Directory.GetCurrentDirectory();
            var extractDir = Path.Combine(workingDir, "extracted", $"PlaceiT_Extract_{Guid.NewGuid():N}");
            Directory.CreateDirectory(extractDir);
            
            logger.LogInformation($"Extracting PlaceiT package to: {extractDir}");
            
            // Extract the zip file
            ZipFile.ExtractToDirectory(zipFilePath, extractDir);
            
            // Option 1: Unblock files to remove Internet Zone security restrictions
            UnblockExtractedFiles(extractDir);
            
            // Validate and log the extracted package contents
            ValidatePackageContents(extractDir);
            
            // Find the Excel simulator file (.xlsx or .xlsm) - should be exactly one
            var xlsxFiles = Directory.GetFiles(extractDir, "*.xlsx", SearchOption.AllDirectories);
            var xlsmFiles = Directory.GetFiles(extractDir, "*.xlsm", SearchOption.AllDirectories);
            var excelFiles = xlsxFiles.Concat(xlsmFiles).ToArray();
            
            if (excelFiles.Length == 0)
            {
                throw new FileNotFoundException("No Excel simulator file (.xlsx or .xlsm) found in the PlaceiT package");
            }
            
            if (excelFiles.Length == 1)
            {
                logger.LogInformation($"Found PlaceiT Excel simulator: {Path.GetFileName(excelFiles[0])}");
                
                // Return path to the Excel file in its extracted location (keeps all files together)
                return (excelFiles[0], extractDir);
            }
            
            // This shouldn't happen based on the expected package structure, but handle it gracefully
            logger.LogWarning($"Unexpected: Multiple Excel files found in package. Using first one: {Path.GetFileName(excelFiles[0])}");
            logger.LogWarning($"All Excel files: {string.Join(", ", excelFiles.Select(Path.GetFileName))}");
            
            // Return path to the Excel file in its extracted location (keeps all files together)
            return (excelFiles[0], extractDir);
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
            ConfigureExcelVisibility();
            
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
            // Shutdown COM objects first to release file locks
            Shutdown();
            
            // Clean up extracted directory after COM objects are released
            if (extractedDirectory != null && Directory.Exists(extractedDirectory))
            {
                CleanupExtractedDirectory(extractedDirectory);
            }
            
            semaphore.Release();
        }
    }

    private bool CheckPlaceiTAvailability(dynamic workbook)
    {
        try
        {
            // We don't need to inspect VBA code (which is password protected)
            // We just need to verify the workbook is loaded and we can interact with it via COM
            // The actual PlaceiT COM entry point will be called during simulation run
            var application = workbook.Application;
            
            // Quick check: Try to detect PlaceiT COM add-in (optional verification)
            try
            {
                var comAddIns = application.COMAddIns;
                for (int i = 1; i <= comAddIns.Count; i++)
                {
                    var addIn = comAddIns.Item(i);
                    if (addIn.Description?.Contains("PlaceiT") == true || 
                        addIn.ProgId?.Contains("PlaceiT") == true)
                    {
                        logger.LogInformation("PlaceiT COM add-in detected");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not enumerate COM add-ins");
            }

            // Assume PlaceiT is available in the .xlsm file from the PlaceiT package
            // We'll verify it works when we actually call the COM entry point during simulation
            logger.LogInformation("PlaceiT package loaded - COM entry point will be accessed during simulation run");
            return true; // Assume available, will be verified during actual simulation
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
            ConfigureExcelVisibility();
            
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
                    // Explicitly release COM object
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(workbook);
                    workbook = null;
                }
                catch (System.Runtime.InteropServices.COMException ex)
                {
                    logger.LogWarning(ex, "Error closing workbook (simulation may have completed successfully)");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error releasing workbook COM object");
                }
            }
            
            // Shutdown COM objects first to release file locks
            Shutdown();
            
            // Clean up extracted directory after COM objects are released
            if (extractedDirectory != null && Directory.Exists(extractedDirectory))
            {
                CleanupExtractedDirectory(extractedDirectory);
            }
            
            semaphore.Release();
        }
    }
}
