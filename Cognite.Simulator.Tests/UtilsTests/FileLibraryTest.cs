using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using Cognite.Simulator.Utils;
using CogniteSdk;
using CogniteSdk.Alpha;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Cognite.Simulator.Tests.UtilsTests
{
    [Collection(nameof(SequentialTestCollection))]
    public class FileLibraryTest
    {
        [Fact]
        public async Task TestModelLibrary()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddHttpClient<FileStorageClient>();
            services.AddSingleton<ModeLibraryTest>();
            services.AddSingleton<StagingArea<ModelParsingInfo>>();
            StateStoreConfig stateConfig = null;

            try
            {
                using var provider = services.BuildServiceProvider();

                // prepopulate models in CDF
                var cdf = provider.GetRequiredService<Client>();
                var fileStorageClient = provider.GetRequiredService<FileStorageClient>();
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
                var v1 = Assert.Contains(
                    revisionMap["PROSPER-Connector_Test_Model-1"].Id.ToString(), // This this revision should exist in CDF
                    libState);
                Assert.Equal("PROSPER", v1.Source);
                Assert.Equal("Connector Test Model", v1.ModelName);
                Assert.Equal("PROSPER-Connector_Test_Model", v1.ModelExternalId);
                Assert.Equal(1, v1.Version);
                Assert.False(v1.Processed);

                var v2 = Assert.Contains(
                    revisionMap["PROSPER-Connector_Test_Model-2"].Id.ToString(), // This this revision should exist in CDF
                    libState);
                Assert.Equal("PROSPER", v2.Source);
                Assert.Equal("Connector Test Model", v2.ModelName);
                Assert.Equal("PROSPER-Connector_Test_Model", v2.ModelExternalId);
                Assert.Equal(2, v2.Version);
                Assert.False(v2.Processed);

                // Start the library update loop that download and parses the files, stop after 5 secs
                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                var linkedToken = linkedTokenSource.Token;
                linkedTokenSource.CancelAfter(TimeSpan.FromSeconds(15)); // should be enough time to download the file from CDF and parse it
                var modelLibTasks = lib.GetRunTasks(linkedToken);
                await modelLibTasks
                    .RunAll(linkedTokenSource)
                    .ConfigureAwait(false);

                // Verify that the files were downloaded and processed
                Assert.True(v1.Processed);
                Assert.False(string.IsNullOrEmpty(v1.FilePath));
                Assert.False(System.IO.File.Exists(v1.FilePath)); // Should only have the latest version
                Assert.True(v2.Processed);
                Assert.False(string.IsNullOrEmpty(v2.FilePath));
                Assert.True(System.IO.File.Exists(v2.FilePath));

                var latest = lib.GetLatestModelVersion(v1.Source, v1.ModelName);
                Assert.NotNull(latest);
                Assert.Equal(v2, latest);

                var log1 = await lib.StagingArea.GetEntry(v1.Id, source.Token);
                Assert.NotNull(log1);
                Assert.True(log1.Parsed);
                Assert.False(log1.Error);
                Assert.Equal(ParsingStatus.success, log1.Status);

                var log2 = await lib.StagingArea.GetEntry(v2.Id, source.Token);
                Assert.NotNull(log2);
                Assert.True(log2.Parsed);
                Assert.False(log2.Error);
                Assert.Equal(ParsingStatus.success, log2.Status);
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
            }
        }

        [Fact]
        public async Task TestConfigurationLibrary()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddHttpClient<FileStorageClient>();
            services.AddSingleton<ConfigurationLibraryTest>();

            StateStoreConfig stateConfig = null;

            try
            {
                using var provider = services.BuildServiceProvider();

                // prepopulate routine in CDF
                var cdf = provider.GetRequiredService<CogniteDestination>();
                var FileStorageClient = provider.GetRequiredService<FileStorageClient>();
                var revision = await SeedData.GetOrCreateSimulatorRoutineRevision(
                    cdf.CogniteClient,
                    FileStorageClient,
                    SeedData.SimulatorRoutineCreateWithTsAndExtendedIO,
                    SeedData.SimulatorRoutineRevisionWithTsAndExtendedIO
                ).ConfigureAwait(false);

                stateConfig = provider.GetRequiredService<StateStoreConfig>();
                using var source = new CancellationTokenSource();
                var lib = provider.GetRequiredService<ConfigurationLibraryTest>();
                await lib.Init(source.Token).ConfigureAwait(false);

                // Start the library update loop that download and parses the files, stop after 5 secs
                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                var linkedToken = linkedTokenSource.Token;
                linkedTokenSource.CancelAfter(TimeSpan.FromSeconds(5)); // should be enough time to download the file from CDF and parse it
                var modelLibTasks = lib.GetRunTasks(linkedToken);
                await modelLibTasks
                    .RunAll(linkedTokenSource)
                    .ConfigureAwait(false);

                Assert.NotEmpty(lib.State);
               var state = Assert.Contains(
                    revision.Id.ToString(), // This simulator configuration should exist in CDF
                    (IReadOnlyDictionary<string, TestConfigurationState>)lib.State);
                Assert.Equal("PROSPER", state.Source);
                Assert.Equal("PROSPER-Connector_Test_Model", state.ModelName);

                var simConf = lib.GetSimulationConfiguration(revision.ExternalId);
                Assert.NotNull(simConf);
                Assert.Equal("Test Routine with Input TS and extended IO", simConf.RoutineExternalId);
                foreach (var input in simConf.Configuration.Inputs)
                {
                    Assert.True(input.IsTimeSeries);
                    Assert.NotNull(input.Name);
                    Assert.NotNull(input.SourceExternalId);
                    Assert.NotNull(input.SaveTimeseriesExternalId);
                }
                var simConfState = lib.GetSimulationConfigurationState(revision.ExternalId);
                Assert.NotNull(simConfState);
                Assert.Equal(state, simConfState);
            }
            finally
            {
                if (stateConfig != null)
                {
                    StateUtils.DeleteLocalFile(stateConfig.Location);
                }
            }
        }

        [Fact]
        public async Task TestConfigurationLibraryWithExtendedIO()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddHttpClient<FileStorageClient>();
            services.AddSingleton<ConfigurationLibraryTest>();

            StateStoreConfig stateConfig = null;

            try
            {
                using var provider = services.BuildServiceProvider();
                
                // prepopulate routine in CDF
                var cdf = provider.GetRequiredService<CogniteDestination>();
                var fileStorageClient = provider.GetRequiredService<FileStorageClient>();
                await TestHelpers.SimulateProsperRunningAsync(cdf.CogniteClient, "integration-tests-connector").ConfigureAwait(false);
                var revision = await SeedData.GetOrCreateSimulatorRoutineRevision(
                    cdf.CogniteClient,
                    fileStorageClient,
                    SeedData.SimulatorRoutineCreateWithExtendedIO,
                    SeedData.SimulatorRoutineRevisionWithExtendedIO
                ).ConfigureAwait(false);

                stateConfig = provider.GetRequiredService<StateStoreConfig>();
                using var source = new CancellationTokenSource();

                var lib = provider.GetRequiredService<ConfigurationLibraryTest>();
                await lib.Init(source.Token).ConfigureAwait(false);


                // Start the library update loop that download and parses the files, stop after 5 secs
                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                var linkedToken = linkedTokenSource.Token;
                linkedTokenSource.CancelAfter(TimeSpan.FromSeconds(5)); // should be enough time to download the file from CDF and parse it
                var modelLibTasks = lib.GetRunTasks(linkedToken);
                await modelLibTasks
                    .RunAll(linkedTokenSource)
                    .ConfigureAwait(false);

                Assert.NotEmpty(lib.State);
                var state = Assert.Contains(
                    revision.Id.ToString(), // This simulator configuration should exist in CDF
                    (IReadOnlyDictionary<string, TestConfigurationState>)lib.State);
                Assert.Equal("PROSPER", state.Source);
                Assert.Equal("PROSPER-Connector_Test_Model", state.ModelName);

                var routineRevision = lib.GetSimulationConfiguration(revision.ExternalId);
                var simConf = routineRevision.Configuration;
                Assert.NotNull(simConf);

                Assert.Equal("Test Routine with extended IO", routineRevision.RoutineExternalId);
                Assert.Equal("Test Routine with extended IO - 1", routineRevision.ExternalId);
                
                Assert.NotEmpty(simConf.Inputs);
                foreach (var input in simConf.Inputs)
                {
                    Assert.NotNull(input.Value);
                    Assert.NotNull(input.ReferenceId);
                    Assert.True(input.IsConstant);
                    Assert.NotNull(input.Name);
                    Assert.StartsWith("SimConnect-IntegrationTests-IC", input.SaveTimeseriesExternalId);
                }

                var simConfState = lib.GetSimulationConfigurationState(revision.ExternalId);
                Assert.NotNull(simConfState);
                Assert.Equivalent(state, simConfState, true);
            }
            finally
            {
                if (stateConfig != null)
                {
                    StateUtils.DeleteLocalFile(stateConfig.Location);
                }
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
        public ModeLibraryTest(
            CogniteDestination cdf,
            ILogger<ModeLibraryTest> logger,
            FileStorageClient downloadClient,
            StagingArea<ModelParsingInfo> staging,
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
                        Name = "PROSPER",
                        DataSetId = CdfTestClient.TestDataset
                    }
                },
                cdf,
                logger,
                downloadClient,
                staging,
                store)
        {
        }

        public StagingArea<ModelParsingInfo> StagingArea => Staging;

        protected override Task ExtractModelInformation(IEnumerable<TestFileState> modelStates, CancellationToken token)
        {
            return Task.Run(() =>
            {
                foreach (var state in modelStates)
                {
                    state.ParsingInfo.SetSuccess("Model parsed successfully");
                    state.Processed = true;
                }
            }, token);
        }

        protected override TestFileState StateFromModelRevision(SimulatorModelRevision modelRevision, CogniteSdk.Alpha.SimulatorModel model)
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
            };
        }
    }

    public class TestConfigurationState : ConfigurationStateBase
    {
        public TestConfigurationState(string id) : base(id)
        {
        }
    }

    public class ConfigurationLibraryTest :
        ConfigurationLibraryBase<TestConfigurationState, FileStatePoco, SimulatorRoutineRevision>
    {
        public ConfigurationLibraryTest(
            CogniteDestination cdf, 
            ILogger<ConfigurationLibraryTest> logger, 
            IExtractionStateStore store = null) : 
            base(
                new FileLibraryConfig
                {
                    FilesTable = "ConfigurationFiles",
                    LibraryId = "ConfigurationState",
                    LibraryTable = "Library",
                    LibraryUpdateInterval = 2, // Update every 2 seconds
                    StateStoreInterval = 2, // Save state every 2 seconds
                },
                new List<SimulatorConfig>
                {
                    new SimulatorConfig
                    {
                        Name = "PROSPER",
                        DataSetId = CdfTestClient.TestDataset
                    }
                },
                cdf, logger, store)
        {
        }

        protected override TestConfigurationState StateFromRoutineRevision(SimulatorRoutineRevision routineRevision)
        {
            return new TestConfigurationState(routineRevision.Id.ToString())
            {
                CdfId = routineRevision.Id,
                DataSetId = routineRevision.DataSetId,
                CreatedTime = routineRevision.CreatedTime,
                UpdatedTime = routineRevision.CreatedTime,
                ModelName = routineRevision.ModelExternalId,
                ModelExternalId = routineRevision.ModelExternalId,
                Source = routineRevision.SimulatorExternalId,
                Deserialized = false,
                ExternalId = routineRevision.ExternalId,
            };
        }
    }
}
