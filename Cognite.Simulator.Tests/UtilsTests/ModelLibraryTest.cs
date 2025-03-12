using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using Cognite.Simulator.Utils;
using Cognite.Simulator.Utils.Automation;

using CogniteSdk;
using CogniteSdk.Alpha;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Xunit;

namespace Cognite.Simulator.Tests.UtilsTests
{
    [Collection(nameof(SequentialTestCollection))]
    public class ModelLibraryTest
    {
        private static void CleanUp(bool deleteDirectory, StateStoreConfig stateConfig)
        {
            try
            {
                if (deleteDirectory && Directory.Exists("./files"))
                {
                    Directory.Delete("./files", true);
                }
                if (stateConfig != null)
                {
                    StateUtils.DeleteLocalFile(stateConfig.Location);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error cleaning up: {ex.Message}");
            }
        }

        [Fact]
        public async Task TestModelLibrary()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddHttpClient<FileStorageClient>();
            services.AddSingleton(SeedData.SimulatorCreate);
            services.AddSingleton<ModeLibraryTest>();
            services.AddSingleton<ModelParsingInfo>();
            var loggerConfig = new LoggerConfig
            {
                Console = new Extractor.Logging.ConsoleConfig
                {
                    Level = "debug"
                },
                Remote = new RemoteLogConfig
                {
                    Enabled = true,
                    Level = "information"
                }
            };
            services.AddSingleton(loggerConfig);

            StateStoreConfig stateConfig = null;
            using var provider = services.BuildServiceProvider();

            var cdf = provider.GetRequiredService<Client>();
            var sink = provider.GetRequiredService<ScopedRemoteApiSink>();

            try
            {
                await SeedData.GetOrCreateSimulator(cdf, SeedData.SimulatorCreate);
                var fileStorageClient = provider.GetRequiredService<FileStorageClient>();
                // prepopulate models in CDF
                var revisionsRes = await SeedData.GetOrCreateSimulatorModelRevisions(cdf, fileStorageClient);
                var revisions = revisionsRes.TakeLast(1); // This test only works with the latest revision
                var revisionMap = revisions.ToDictionary(r => r.ExternalId, r => r);

                stateConfig = provider.GetRequiredService<StateStoreConfig>();
                using var source = new CancellationTokenSource();

                var lib = provider.GetRequiredService<ModeLibraryTest>();
                await lib.Init(source.Token);

                bool dirExists = Directory.Exists("./files");
                Assert.True(dirExists, "Should have created a directory for the files");

                var libState = (IReadOnlyDictionary<string, TestFileState>)lib._state;

                Assert.NotEmpty(lib._state);

                foreach (var revision in revisions)
                {
                    var modelInState = lib._state.GetValueOrDefault(revision.Id.ToString());
                    Assert.NotNull(modelInState);
                    Assert.Equal(revision.ExternalId, modelInState.ExternalId);
                    Assert.Equal(revision.ModelExternalId, modelInState.ModelExternalId);
                    Assert.Equal(SeedData.TestModelExternalId, modelInState.ModelExternalId);
                    Assert.Equal(revision.VersionNumber, modelInState.Version);
                    Assert.False(modelInState.Processed);
                }


                // Start the library update loop that download and parses the files, stop after 5 secs
                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                var linkedToken = linkedTokenSource.Token;
                linkedTokenSource.CancelAfter(TimeSpan.FromSeconds(5)); // should be enough time to download the file from CDF and parse it
                var modelLibTasks = lib.GetRunTasks(linkedToken);
                await modelLibTasks
                    .RunAll(linkedTokenSource)
;

                await sink.Flush(cdf.Alpha.Simulators, CancellationToken.None);

                foreach (var revision in revisions)
                {
                    var modelInState = lib._state.GetValueOrDefault(revision.Id.ToString());
                    Assert.NotNull(modelInState);
                    Assert.Equal(revision.ExternalId, modelInState.ExternalId);
                    Assert.True(modelInState.Processed);
                    Assert.False(string.IsNullOrEmpty(modelInState.FilePath));
                    Assert.True(System.IO.File.Exists(modelInState.FilePath));
                }

                var modelExternalIdV2 = $"{SeedData.TestModelExternalId}-2";
                var v2 = Assert.Contains(
                    revisionMap[modelExternalIdV2].Id.ToString(),
                    libState);
                var latest = await lib.GetModelRevision(modelExternalIdV2);
                Assert.NotNull(latest);
                Assert.Equal(v2, latest);

                var logv2 = await cdf.Alpha.Simulators.RetrieveSimulatorLogsAsync(
                    new List<Identity> { new Identity(v2.LogId) }, source.Token);

                var logv2Data = logv2.First().Data;
                var parsedModelEntry2 = logv2Data.Where(lg => lg.Message.StartsWith("Model revision parsed successfully"));
                Assert.Equal("Information", parsedModelEntry2.First().Severity);
            }
            finally
            {
                CleanUp(true, stateConfig);
                await SeedData.DeleteSimulator(cdf, SeedData.SimulatorCreate.ExternalId);
            }
        }

        // This tests the situation when the model is marked as parsed before the library starts
        // In such cases the library should normally not re-parse the model
        [Fact]
        public async Task TestModelLibraryWithReparse()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddHttpClient<FileStorageClient>();
            services.AddSingleton<ModeLibraryTest>();
            services.AddSingleton(SeedData.SimulatorCreate);
            services.AddSingleton<ModelParsingInfo>();
            services.AddSingleton<ScopedRemoteApiSink>();
            services.AddSingleton<DefaultConfig<AutomationConfig>>();
            StateStoreConfig stateConfig = null;
            using var provider = services.BuildServiceProvider();

            var cdf = provider.GetRequiredService<Client>();
            var sink = provider.GetRequiredService<ScopedRemoteApiSink>();

            try
            {
                await SeedData.GetOrCreateSimulator(cdf, SeedData.SimulatorCreate);
                var fileStorageClient = provider.GetRequiredService<FileStorageClient>();
                // prepopulate models in CDF
                var initialRevisionsRes = await SeedData.GetOrCreateSimulatorModelRevisions(cdf, fileStorageClient);
                var initialRevisions = initialRevisionsRes.TakeLast(1); // This test only works with the latest revision

                // set model revision to be be "pre-parsed"
                // those should not be parsed again
                var revisions = new List<SimulatorModelRevision>();
                foreach (var revision in initialRevisions)
                {
                    var res = await cdf.Alpha.Simulators.UpdateSimulatorModelRevisionParsingStatus(
                        revision.Id,
                        SimulatorModelRevisionStatus.failure,
                        token: CancellationToken.None);

                    revisions.Add(res);
                }
                var revisionMap = revisions.ToDictionary(r => r.ExternalId, r => r);

                stateConfig = provider.GetRequiredService<StateStoreConfig>();
                using var source = new CancellationTokenSource();

                var lib = provider.GetRequiredService<ModeLibraryTest>();
                await lib.Init(source.Token);

                var libState = (IReadOnlyDictionary<string, TestFileState>)lib._state;

                // Start the library update loop that download the files, should not parse them
                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                linkedTokenSource.CancelAfter(TimeSpan.FromSeconds(5)); // should be enough time to download the file from CDF
                await lib.GetRunTasks(linkedTokenSource.Token)
                    .RunAll(linkedTokenSource)
;

                foreach (var revision in revisions)
                {
                    var modelInState = lib._state.GetValueOrDefault(revision.Id.ToString());
                    Assert.NotNull(modelInState);
                    Assert.Equal(revision.ExternalId, modelInState.ExternalId);
                    // only true if the model was processed locally
                    // which doesn't happen in this test case since the model is marked as parsed before
                    Assert.False(modelInState.Processed);
                    Assert.False(string.IsNullOrEmpty(modelInState.FilePath));
                    Assert.True(System.IO.File.Exists(modelInState.FilePath));
                    Assert.Equal(1, modelInState.DownloadAttempts);

                    Assert.False(modelInState.IsExtracted); // this is only true if the file was parsed locally
                    Assert.False(modelInState.CanRead);
                    Assert.False(modelInState.ShouldProcess());
                    Assert.True(modelInState.ParsingInfo.Parsed);
                    Assert.Equal(SimulatorModelRevisionStatus.failure, modelInState.ParsingInfo.Status);
                    Assert.True(modelInState.ParsingInfo.Error);
                    // last updated time should not change
                    Assert.Equal(revision.LastUpdatedTime, modelInState.UpdatedTime);
                }

                var updatedRevisions = new List<SimulatorModelRevision>();
                foreach (var revision in revisions)
                {
                    var res = await cdf.Alpha.Simulators.UpdateSimulatorModelRevisionParsingStatus(
                        revision.Id,
                        SimulatorModelRevisionStatus.unknown,
                        token: CancellationToken.None);

                    updatedRevisions.Add(res);
                }
                revisions = updatedRevisions;
                revisionMap = revisions.ToDictionary(r => r.ExternalId, r => r);

                using var linkedTokenSource2 = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                linkedTokenSource2.CancelAfter(TimeSpan.FromSeconds(10));
                await lib.GetRunTasks(linkedTokenSource2.Token)
                    .RunAll(linkedTokenSource2)
;

                foreach (var revision in revisions)
                {
                    var modelInState = lib._state.GetValueOrDefault(revision.Id.ToString());
                    Assert.NotNull(modelInState);
                    Assert.Equal(revision.ExternalId, modelInState.ExternalId);
                    // only true if the model was processed locally
                    // which exactly what happens in this test case since the model is marked to be re-parsed
                    Assert.True(modelInState.Processed);
                    Assert.False(string.IsNullOrEmpty(modelInState.FilePath));
                    Assert.True(System.IO.File.Exists(modelInState.FilePath));
                    Assert.Equal(0, modelInState.DownloadAttempts); // This gets reset when the model is to set to be re-parsed

                    Assert.True(modelInState.IsExtracted); // this is only true if the file was parsed locally, which is the case here
                    Assert.True(modelInState.CanRead);
                    Assert.False(modelInState.ShouldProcess());
                    Assert.True(modelInState.ParsingInfo.Parsed);
                    Assert.Equal(SimulatorModelRevisionStatus.success, modelInState.ParsingInfo.Status);
                    Assert.False(modelInState.ParsingInfo.Error);
                    // last updated time should change since status is updated during the test
                    Assert.True(initialRevisions.First().LastUpdatedTime < modelInState.UpdatedTime);
                }
            }
            finally
            {
                CleanUp(true, stateConfig);
                await SeedData.DeleteSimulator(cdf, SeedData.SimulatorCreate.ExternalId);
            }
        }

        [Fact]
        public async Task TestModelLibraryHotReload()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddHttpClient<FileStorageClient>();
            services.AddSingleton<ModeLibraryTest>();
            services.AddSingleton<ModelParsingInfo>();
            services.AddSingleton<ScopedRemoteApiSink>();
            services.AddSingleton(SeedData.SimulatorCreate);
            services.AddSingleton<DefaultConfig<AutomationConfig>>();
            StateStoreConfig stateConfig = null;
            using var provider = services.BuildServiceProvider();

            var cdf = provider.GetRequiredService<Client>();
            var sink = provider.GetRequiredService<ScopedRemoteApiSink>();

            try
            {
                await SeedData.GetOrCreateSimulator(cdf, SeedData.SimulatorCreate);
                var fileStorageClient = provider.GetRequiredService<FileStorageClient>();
                // prepopulate models in CDF
                var revisionsRes = await SeedData.GetOrCreateSimulatorModelRevisions(cdf, fileStorageClient);
                var revisions = revisionsRes.TakeLast(1); // This test only works with the latest revision

                stateConfig = provider.GetRequiredService<StateStoreConfig>();
                using var source = new CancellationTokenSource();

                var lib = provider.GetRequiredService<ModeLibraryTest>();
                await lib.Init(source.Token);

                var libState = (IReadOnlyDictionary<string, TestFileState>)lib._state;
                Assert.NotEmpty(lib._state);

                // Start the library update loop that download and parses the files, stop after 5 secs
                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                linkedTokenSource.CancelAfter(TimeSpan.FromSeconds(5)); // should be enough time to download the file from CDF and parse it
                var modelLibTasks = lib.GetRunTasks(linkedTokenSource.Token);
                await modelLibTasks
                    .RunAll(linkedTokenSource)
;

                foreach (var revision in revisions)
                {
                    var modelInState = lib._state.GetValueOrDefault(revision.Id.ToString());
                    Assert.NotNull(modelInState);
                    Assert.True(modelInState.Processed);
                    Assert.False(string.IsNullOrEmpty(modelInState.FilePath));
                    Assert.True(System.IO.File.Exists(modelInState.FilePath));
                }

                // delete current models
                var modelExternalIdToDelete = revisions.First().ModelExternalId;
                await SeedData.DeleteSimulatorModel(cdf, modelExternalIdToDelete);

                var revisionNewCreate = SeedData.GenerateSimulatorModelRevisionCreate("hot-reload", version: 1);
                await SeedData.GetOrCreateSimulatorModelRevisionWithFile(cdf, fileStorageClient, SeedData.SimpleModelFileCreate, revisionNewCreate);

                var revisionNew = await SeedData.GetOrCreateSimulatorModelRevisionWithFile(cdf, fileStorageClient, SeedData.SimpleModelFileCreate, revisionNewCreate);

                var accessedRevision = await lib.GetModelRevision(revisionNew.ExternalId);

                var newModelState = lib._temporaryState.GetValueOrDefault(revisionNew.Id.ToString());

                // Accessed revision should be processed and have a file path
                if (accessedRevision.ExternalId == revisionNew.ExternalId)
                {
                    Assert.NotNull(newModelState);
                    Assert.True(newModelState.Processed);
                    Assert.False(string.IsNullOrEmpty(newModelState.FilePath));
                    Assert.True(System.IO.File.Exists(newModelState.FilePath));
                }
                Assert.NotEmpty(Directory.GetFiles($"./files/temp/{revisionNew.FileId}"));

                // cleanup temp state
                lib.WipeTemporaryModelFiles();
                Assert.Empty(Directory.GetDirectories($"./files/temp"));
                Assert.Empty(lib._temporaryState);
            }
            finally
            {
                CleanUp(true, stateConfig);
                await SeedData.DeleteSimulator(cdf, SeedData.SimulatorCreate.ExternalId);
            }
        }

        [Fact]
        public async Task TestRoutineLibrary()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddSingleton(SeedData.SimulatorCreate);
            services.AddHttpClient<FileStorageClient>();
            services.AddSingleton<RoutineLibraryTest>();
            StateStoreConfig stateConfig = null;
            using var provider = services.BuildServiceProvider();
            var testStart = DateTime.UtcNow;

            // prepopulate routine in CDF
            var cdf = provider.GetRequiredService<CogniteDestination>();
            try
            {

                var FileStorageClient = provider.GetRequiredService<FileStorageClient>();
                await SeedData.GetOrCreateSimulator(cdf.CogniteClient, SeedData.SimulatorCreate);
                await TestHelpers.SimulateASimulatorRunning(cdf.CogniteClient, SeedData.TestIntegrationExternalId);

                var revision = await SeedData.GetOrCreateSimulatorRoutineRevision(
                    cdf.CogniteClient,
                    FileStorageClient,
                    SeedData.SimulatorRoutineCreateWithTsAndExtendedIO,
                    SeedData.SimulatorRoutineRevisionWithTsAndExtendedIO
                );

                var revision2 = await SeedData.GetOrCreateSimulatorRoutineRevision(
                    cdf.CogniteClient,
                    FileStorageClient,
                    SeedData.SimulatorRoutineCreateScheduled,
                    SeedData.SimulatorRoutineRevisionCreateScheduled
                );

                stateConfig = provider.GetRequiredService<StateStoreConfig>();
                using var source = new CancellationTokenSource();
                var lib = provider.GetRequiredService<RoutineLibraryTest>();

                // Creating 2 revisions and setting the limit to 1 to test cursor pagination
                lib.PaginationLimit = 1;
                await lib.Init(source.Token);

                Assert.Contains(revision.Id.ToString(), lib.RoutineRevisions);
                Assert.Contains(revision2.Id.ToString(), lib.RoutineRevisions);
                Assert.Equal(2, lib.RoutineRevisions.Count);

                // Start the library update loop that download and parses the files, stop after 5 secs
                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                var linkedToken = linkedTokenSource.Token;
                linkedTokenSource.CancelAfter(TimeSpan.FromSeconds(5)); // should be enough time to download the file from CDF and parse it
                var routineLibTasks = lib.GetRunTasks(linkedToken);
                await routineLibTasks
                    .RunAll(linkedTokenSource)
;

                var libRange = lib.LibraryState.DestinationExtractedRange;
                Assert.True(libRange.Before(testStart));
                Assert.True(libRange.After(DateTime.UtcNow));

                var simConf = await lib.GetRoutineRevision(revision.ExternalId);
                Assert.NotNull(simConf);
                Assert.Equal(SeedData.TestRoutineExternalIdWithTs, simConf.RoutineExternalId);
                foreach (var input in simConf.Configuration.Inputs)
                {
                    Assert.True(input.IsTimeSeries);
                    Assert.NotNull(input.Name);
                    Assert.NotNull(input.SourceExternalId);
                }
            }
            finally
            {
                CleanUp(false, stateConfig);
                await SeedData.DeleteSimulator(cdf.CogniteClient, SeedData.SimulatorCreate.ExternalId);
            }
        }

        [Fact]
        public async Task TestRoutineLibraryWithExtendedIO()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddSingleton(SeedData.SimulatorCreate);
            services.AddHttpClient<FileStorageClient>();
            services.AddSingleton<RoutineLibraryTest>();

            StateStoreConfig stateConfig = null;
            using var provider = services.BuildServiceProvider();
            // prepopulate routine in CDF
            var cdf = provider.GetRequiredService<CogniteDestination>();
            try
            {

                var fileStorageClient = provider.GetRequiredService<FileStorageClient>();

                await SeedData.GetOrCreateSimulator(cdf.CogniteClient, SeedData.SimulatorCreate);

                await TestHelpers.SimulateASimulatorRunning(cdf.CogniteClient, SeedData.TestIntegrationExternalId);
                var revision = await SeedData.GetOrCreateSimulatorRoutineRevision(
                    cdf.CogniteClient,
                    fileStorageClient,
                    SeedData.SimulatorRoutineCreateWithExtendedIO,
                    SeedData.SimulatorRoutineRevisionWithExtendedIO
                );

                stateConfig = provider.GetRequiredService<StateStoreConfig>();
                using var source = new CancellationTokenSource();
                var lib = provider.GetRequiredService<RoutineLibraryTest>();
                await lib.Init(source.Token);


                // Start the library update loop that download and parses the files, stop after 5 secs
                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                var linkedToken = linkedTokenSource.Token;
                linkedTokenSource.CancelAfter(TimeSpan.FromSeconds(5)); // should be enough time to download the file from CDF and parse it
                var routineLibTasks = lib.GetRunTasks(linkedToken);
                await routineLibTasks
                    .RunAll(linkedTokenSource)
;

                var routineRevision = await lib.GetRoutineRevision(revision.ExternalId);
                var simConf = routineRevision.Configuration;
                Assert.NotNull(simConf);

                Assert.Equal(SeedData.TestRoutineExternalId, routineRevision.RoutineExternalId);
                Assert.Equal($"{SeedData.TestRoutineExternalId} - 1", routineRevision.ExternalId);

                Assert.NotEmpty(simConf.Inputs);
                foreach (var input in simConf.Inputs)
                {
                    Assert.NotNull(input.Value);
                    Assert.NotNull(input.ReferenceId);
                    Assert.True(input.IsConstant);
                    Assert.NotNull(input.Name);
                    Assert.StartsWith(SeedData.TestRoutineTsPrefix + "-IC", input.SaveTimeseriesExternalId);
                }
            }
            finally
            {
                CleanUp(false, stateConfig);
                await SeedData.DeleteSimulator(cdf.CogniteClient, SeedData.SimulatorCreate.ExternalId);

            }
        }
    }

    /// <summary>
    /// Simple test state to keep track of files that have been processed
    /// </summary>
    public class TestFileState : ModelStateBase
    {
        public bool Processed { get; set; }

        public override bool IsExtracted => Processed;

        public TestFileState() : base()
        {
        }

    }

    public class DefaultAutomationConfig : AutomationConfig
    {

    }

    /// <summary>
    /// File library is abstract. Implement a simple mock library
    /// to test the base functionality
    /// </summary>
    public class ModeLibraryTest : ModelLibraryBase<DefaultAutomationConfig, TestFileState, ModelStateBasePoco, ModelParsingInfo>
    {
        private ILogger<ModeLibraryTest> _logger;
        public ModeLibraryTest(
            CogniteDestination cdf,
            ILogger<ModeLibraryTest> logger,
            FileStorageClient downloadClient,
            SimulatorCreate simulatorDefinition,
            IExtractionStateStore store = null) :
            base(
                new ModelLibraryConfig
                {
                    FilesDirectory = "./files",
                    FilesTable = "LibraryFiles",
                    LibraryId = "LibraryState",
                    LibraryTable = "Library",
                    LibraryUpdateInterval = 2, // Update every 2 seconds
                },
                simulatorDefinition,
                cdf,
                logger,
                downloadClient,
                store)
        {
            _logger = logger;
        }

        protected override Task ExtractModelInformation(TestFileState modelState, CancellationToken token)
        {
            return Task.Run(() =>
            {
                // make sure file exists
                if (System.IO.File.Exists(modelState.FilePath))
                {
                    var fileBytes = System.IO.File.ReadAllBytes(modelState.FilePath);
                    // test file is a single byte with value 42
                    if (fileBytes.Length == 1 && fileBytes[0] == 42)
                    {
                        _logger.LogInformation("Model revision parsed successfully {ExternalId}", modelState.ExternalId);
                        modelState.ParsingInfo.SetSuccess();
                        modelState.Processed = true;
                        return;
                    }
                }
                modelState.ParsingInfo.SetFailure();
            }, token);
        }

        protected override TestFileState StateFromModelRevision(SimulatorModelRevision modelRevision)
        {
            if (modelRevision == null)
            {
                throw new ArgumentNullException(nameof(modelRevision));
            }

            return new TestFileState()
            {
                Id = modelRevision.Id.ToString(),
                CdfId = modelRevision.FileId,
                DataSetId = modelRevision.DataSetId,
                CreatedTime = modelRevision.CreatedTime,
                UpdatedTime = modelRevision.LastUpdatedTime,
                ModelExternalId = modelRevision.ModelExternalId,
                Source = modelRevision.SimulatorExternalId,
                Processed = false,
                Version = modelRevision.VersionNumber,
                ExternalId = modelRevision.ExternalId,
                LogId = modelRevision.LogId,
            };
        }
    }

    public class RoutineLibraryTest :
        RoutineLibraryBase<SimulatorRoutineRevision>
    {
        public RoutineLibraryTest(
            CogniteDestination cdf,
            SimulatorCreate simulatorDefinition,
            ILogger<RoutineLibraryTest> logger) :
            base(
                new RoutineLibraryConfig
                {
                    LibraryUpdateInterval = 2, // Update every 2 seconds
                },
                simulatorDefinition,
                cdf, logger)
        {
        }

        public BaseExtractionState LibraryState => LibState;
    }
}
