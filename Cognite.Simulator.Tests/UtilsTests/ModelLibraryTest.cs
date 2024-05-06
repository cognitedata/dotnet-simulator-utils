using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Utils;
using CogniteSdk;
using CogniteSdk.Alpha;

namespace Cognite.Simulator.Tests.UtilsTests
{
    [Collection(nameof(SequentialTestCollection))]
    public class ModelLibraryTest
    {
        [Fact]
        public async Task TestModelLibrary()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddHttpClient<FileStorageClient>();
            services.AddSingleton<ModeLibraryTest>();
            services.AddSingleton<ModelParsingInfo>();
            StateStoreConfig stateConfig = null;
            using var provider = services.BuildServiceProvider();

            var cdf = provider.GetRequiredService<Client>();
            var sink = provider.GetRequiredService<ScopedRemoteApiSink>();

            try
            {
                await SeedData.GetOrCreateSimulator(cdf, SeedData.SimulatorCreate).ConfigureAwait(false);
                var fileStorageClient = provider.GetRequiredService<FileStorageClient>();
                // prepopulate models in CDF
                var revisions = await SeedData.GetOrCreateSimulatorModelRevisions(cdf, fileStorageClient).ConfigureAwait(false);
                var revisionMap = revisions.ToDictionary(r => r.ExternalId, r => r);

                stateConfig = provider.GetRequiredService<StateStoreConfig>();
                using var source = new CancellationTokenSource();

                var lib = provider.GetRequiredService<ModeLibraryTest>();
                await lib.Init(source.Token).ConfigureAwait(false);

                bool dirExists = Directory.Exists("./files");
                Assert.True(dirExists, "Should have created a directory for the files");

                var libState = (IReadOnlyDictionary<string, TestFileState>)lib.State;

                Assert.NotEmpty(lib.State);

                foreach (var revision in revisions)
                {
                    var modelInState = lib.State.GetValueOrDefault(revision.Id.ToString());
                    Assert.NotNull(modelInState);
                    Assert.Equal(revision.ExternalId, modelInState.ExternalId);
                    Assert.Equal("Connector Test Model", modelInState.ModelName);
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
                    .ConfigureAwait(false);

                await sink.Flush(cdf.Alpha.Simulators, CancellationToken.None).ConfigureAwait(false);

                foreach (var revision in revisions)
                {
                    var modelInState = lib.State.GetValueOrDefault(revision.Id.ToString());
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
                var latest = await lib.GetModelRevision(modelExternalIdV2).ConfigureAwait(false);
                Assert.NotNull(latest);
                Assert.Equal(v2, latest);

                var logv2 = await cdf.Alpha.Simulators.RetrieveSimulatorLogsAsync(
                    new List<Identity> { new Identity(v2.LogId) }, source.Token).ConfigureAwait(false);

                var logv2Data = logv2.First().Data;
                var parsedModelEntry2 = logv2Data.Where(lg => lg.Message.StartsWith("Model revision parsed successfully"));
                Assert.Equal("Information", parsedModelEntry2.First().Severity);
            }
            finally
            {
                if (Directory.Exists("./files"))
                {
                    Directory.Delete("./files", true);
                }
                if (stateConfig != null)
                {
                    StateUtils.DeleteLocalFile(stateConfig.Location);
                }
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
            StateStoreConfig stateConfig = null;
            using var provider = services.BuildServiceProvider();

            var cdf = provider.GetRequiredService<Client>();
            var sink = provider.GetRequiredService<ScopedRemoteApiSink>();

            try
            {
                await SeedData.GetOrCreateSimulator(cdf, SeedData.SimulatorCreate).ConfigureAwait(false);
                var fileStorageClient = provider.GetRequiredService<FileStorageClient>();
                // prepopulate models in CDF
                var revisions = await SeedData.GetOrCreateSimulatorModelRevisions(cdf, fileStorageClient).ConfigureAwait(false);

                stateConfig = provider.GetRequiredService<StateStoreConfig>();
                using var source = new CancellationTokenSource();

                var lib = provider.GetRequiredService<ModeLibraryTest>();
                await lib.Init(source.Token).ConfigureAwait(false);

                var libState = (IReadOnlyDictionary<string, TestFileState>)lib.State;
                Assert.NotEmpty(lib.State);

                // Start the library update loop that download and parses the files, stop after 5 secs
                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                linkedTokenSource.CancelAfter(TimeSpan.FromSeconds(5)); // should be enough time to download the file from CDF and parse it
                var modelLibTasks = lib.GetRunTasks(linkedTokenSource.Token);
                await modelLibTasks
                    .RunAll(linkedTokenSource)
                    .ConfigureAwait(false);

                foreach (var revision in revisions)
                {
                    var modelInState = lib.State.GetValueOrDefault(revision.Id.ToString());
                    Assert.NotNull(modelInState);
                    Assert.True(modelInState.Processed);
                    Assert.False(string.IsNullOrEmpty(modelInState.FilePath));
                    Assert.True(System.IO.File.Exists(modelInState.FilePath));
                }

                // delete current models
                var modelExternalIdToDelete = revisions.First().ModelExternalId;
                await SeedData.DeleteSimulatorModel(cdf, modelExternalIdToDelete).ConfigureAwait(false);

                var revisionNewCreate = SeedData.GenerateSimulatorModelRevisionCreate("hot-reload", version: 1);
                await SeedData.GetOrCreateSimulatorModelRevisionWithFile(cdf, fileStorageClient, SeedData.SimpleModelFileCreate, revisionNewCreate).ConfigureAwait(false);

                var revisionNew = await SeedData.GetOrCreateSimulatorModelRevisionWithFile(cdf, fileStorageClient, SeedData.SimpleModelFileCreate, revisionNewCreate).ConfigureAwait(false);

                var accessedRevision = await lib.GetModelRevision(revisionNew.ExternalId).ConfigureAwait(false);

                var newModelState = lib.State.GetValueOrDefault(revisionNew.Id.ToString());

                // Accessed revision should be processed and have a file path
                if (accessedRevision.ExternalId == revisionNew.ExternalId)
                {
                    Assert.NotNull(newModelState);
                    Assert.True(newModelState.Processed); 
                    Assert.False(string.IsNullOrEmpty(newModelState.FilePath));
                    Assert.True(System.IO.File.Exists(newModelState.FilePath));
                }

                // cleanup
                // Assert.Empty(lib.State); TODO revision cleanup logic
            }
            finally
            {
                if (Directory.Exists("./files"))
                {
                    Directory.Delete("./files", true);
                }
                if (stateConfig != null)
                {
                    StateUtils.DeleteLocalFile(stateConfig.Location);
                }
                await SeedData.DeleteSimulator(cdf, SeedData.SimulatorCreate.ExternalId);
            }
        }

        [Fact]
        public async Task TestRoutineLibrary()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddHttpClient<FileStorageClient>();
            services.AddSingleton<RoutineLibraryTest>();

            StateStoreConfig stateConfig = null;
            using var provider = services.BuildServiceProvider();

            // prepopulate routine in CDF
            var cdf = provider.GetRequiredService<CogniteDestination>();
            try
            {

                var FileStorageClient = provider.GetRequiredService<FileStorageClient>();
                await SeedData.GetOrCreateSimulator(cdf.CogniteClient, SeedData.SimulatorCreate).ConfigureAwait(false);
                await TestHelpers.SimulateASimulatorRunning(cdf.CogniteClient, SeedData.TestIntegrationExternalId).ConfigureAwait(false);
                var revision = await SeedData.GetOrCreateSimulatorRoutineRevision(
                    cdf.CogniteClient,
                    FileStorageClient,
                    SeedData.SimulatorRoutineCreateWithTsAndExtendedIO,
                    SeedData.SimulatorRoutineRevisionWithTsAndExtendedIO
                ).ConfigureAwait(false);

                stateConfig = provider.GetRequiredService<StateStoreConfig>();
                using var source = new CancellationTokenSource();
                var lib = provider.GetRequiredService<RoutineLibraryTest>();
                await lib.Init(source.Token).ConfigureAwait(false);

                // Start the library update loop that download and parses the files, stop after 5 secs
                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                var linkedToken = linkedTokenSource.Token;
                linkedTokenSource.CancelAfter(TimeSpan.FromSeconds(5)); // should be enough time to download the file from CDF and parse it
                var modelLibTasks = lib.GetRunTasks(linkedToken);
                await modelLibTasks
                    .RunAll(linkedTokenSource)
                    .ConfigureAwait(false);

                var simConf = lib.GetRoutineRevision(revision.ExternalId);
                Assert.NotNull(simConf);
                Assert.Equal(SeedData.TestRoutineExternalIdWithTs, simConf.RoutineExternalId);
                foreach (var input in simConf.Configuration.Inputs)
                {
                    Assert.True(input.IsTimeSeries);
                    Assert.NotNull(input.Name);
                    Assert.NotNull(input.SourceExternalId);
                    Assert.NotNull(input.SaveTimeseriesExternalId);
                }
            }
            finally
            {
                if (stateConfig != null)
                {
                    StateUtils.DeleteLocalFile(stateConfig.Location);
                }
                await SeedData.DeleteSimulator(cdf.CogniteClient, SeedData.SimulatorCreate.ExternalId);
            }
        }

        [Fact]
        public async Task TestRoutineLibraryWithExtendedIO()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddHttpClient<FileStorageClient>();
            services.AddSingleton<RoutineLibraryTest>();

            StateStoreConfig stateConfig = null;
            using var provider = services.BuildServiceProvider();
            // prepopulate routine in CDF
            var cdf = provider.GetRequiredService<CogniteDestination>();
            try
            {  
                
                var fileStorageClient = provider.GetRequiredService<FileStorageClient>();
                
                await SeedData.GetOrCreateSimulator(cdf.CogniteClient, SeedData.SimulatorCreate).ConfigureAwait(false);

                await TestHelpers.SimulateASimulatorRunning(cdf.CogniteClient, SeedData.TestIntegrationExternalId).ConfigureAwait(false);
                var revision = await SeedData.GetOrCreateSimulatorRoutineRevision(
                    cdf.CogniteClient,
                    fileStorageClient,
                    SeedData.SimulatorRoutineCreateWithExtendedIO,
                    SeedData.SimulatorRoutineRevisionWithExtendedIO
                ).ConfigureAwait(false);

                stateConfig = provider.GetRequiredService<StateStoreConfig>();
                using var source = new CancellationTokenSource();
                var lib = provider.GetRequiredService<RoutineLibraryTest>();
                await lib.Init(source.Token).ConfigureAwait(false);


                // Start the library update loop that download and parses the files, stop after 5 secs
                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                var linkedToken = linkedTokenSource.Token;
                linkedTokenSource.CancelAfter(TimeSpan.FromSeconds(5)); // should be enough time to download the file from CDF and parse it
                var modelLibTasks = lib.GetRunTasks(linkedToken);
                await modelLibTasks
                    .RunAll(linkedTokenSource)
                    .ConfigureAwait(false);

                var routineRevision = lib.GetRoutineRevision(revision.ExternalId);
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
                    Assert.StartsWith("SimConnect-IntegrationTests-IC", input.SaveTimeseriesExternalId);
                }
            }
            finally
            {
                if (stateConfig != null)
                {
                    StateUtils.DeleteLocalFile(stateConfig.Location);
                }
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

        public TestFileState(string id) : base(id)
        {
        }
        public override string GetExtension()
        {
            return "out";
        }

    }

    /// <summary>
    /// File library is abstract. Implement a simple mock library
    /// to test the base functionality
    /// </summary>
    public class ModeLibraryTest : ModelLibraryBase<TestFileState, ModelStateBasePoco, ModelParsingInfo>
    {
        private ILogger<ModeLibraryTest> _logger;
        public ModeLibraryTest(
            CogniteDestination cdf,
            ILogger<ModeLibraryTest> logger,
            FileStorageClient downloadClient,
            IExtractionStateStore store = null) :
            base(
                new FileLibraryConfig
                {
                    FilesDirectory = "./files",
                    FilesTable = "LibraryFiles",
                    LibraryId = "LibraryState",
                    LibraryTable = "Library",
                    LibraryUpdateInterval = 2, // Update every 2 seconds
                    StateStoreInterval = 2, // Save state every 2 seconds
                },
                new List<SimulatorConfig>
                {
                    new SimulatorConfig
                    {
                        Name = SeedData.TestSimulatorExternalId,
                        DataSetId = CdfTestClient.TestDataset
                    }
                },
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
                _logger.LogInformation("Model revision parsed successfully {ExternalId}", modelState.ExternalId);
                modelState.ParsingInfo.SetSuccess();
                modelState.Processed = true;
            }, token);
        }

        protected override TestFileState StateFromModelRevision(SimulatorModelRevision modelRevision, SimulatorModel model)
        {
            if (modelRevision == null)
            {
                throw new ArgumentNullException(nameof(modelRevision));
            }

            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            return new TestFileState(modelRevision.Id.ToString())
            {
                CdfId = modelRevision.FileId,
                DataSetId = modelRevision.DataSetId,
                CreatedTime = modelRevision.CreatedTime,
                UpdatedTime = modelRevision.LastUpdatedTime,
                ModelName = model.Name,
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
            ILogger<RoutineLibraryTest> logger) : 
            base(
                new RoutineLibraryConfig
                {
                    LibraryUpdateInterval = 2, // Update every 2 seconds
                },
                new List<SimulatorConfig>
                {
                    new SimulatorConfig
                    {
                        Name = SeedData.TestSimulatorExternalId,
                        DataSetId = CdfTestClient.TestDataset
                    }
                },
                cdf, logger)
        {
        }
    }
}
