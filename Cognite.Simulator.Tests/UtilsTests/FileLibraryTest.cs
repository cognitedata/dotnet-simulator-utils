﻿using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using Cognite.Simulator.Utils;
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
            services.AddHttpClient<FileDownloadClient>();
            services.AddSingleton<ModeLibraryTest>();
            StateStoreConfig stateConfig = null;

            try
            {
                using var provider = services.BuildServiceProvider();
                stateConfig = provider.GetRequiredService<StateStoreConfig>();
                using var source = new CancellationTokenSource();

                var lib = provider.GetRequiredService<ModeLibraryTest>();
                await lib.Init(source.Token).ConfigureAwait(false);

                bool dirExists = Directory.Exists("./files");
                Assert.True(dirExists, "Should have created a directory for the files");

                Assert.NotEmpty(lib.State);
                var v1 = Assert.Contains(
                    "PROSPER-Connector_Test_Model-1", // This file should exist in CDF
                    (IReadOnlyDictionary<string, TestFileState>)lib.State);
                Assert.Equal("PROSPER", v1.Source);
                Assert.Equal("Connector Test Model", v1.ModelName);
                Assert.Equal(1, v1.Version);
                Assert.False(v1.Processed);

                var v2 = Assert.Contains(
                    "PROSPER-Connector_Test_Model-2", // This file should exist in CDF
                    (IReadOnlyDictionary<string, TestFileState>)lib.State);
                Assert.Equal("PROSPER", v2.Source);
                Assert.Equal("Connector Test Model", v2.ModelName);
                Assert.Equal(2, v2.Version);
                Assert.False(v2.Processed);

                // Start the library update loop that download and parses the files, stop after 5 secs
                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                var linkedToken = linkedTokenSource.Token;
                linkedTokenSource.CancelAfter(TimeSpan.FromSeconds(5)); // should be enough time to download the file from CDF and parse it
                var modelLibTasks = lib.GetRunTasks(linkedToken);
                await modelLibTasks
                    .RunAll(linkedTokenSource)
                    .ConfigureAwait(false);

                // Verify that the files were downloaded and processed
                Assert.True(v1.Processed);
                Assert.False(string.IsNullOrEmpty(v1.FilePath));
                Assert.False(File.Exists(v1.FilePath)); // Should only have the latest version
                Assert.True(v2.Processed);
                Assert.False(string.IsNullOrEmpty(v2.FilePath));
                Assert.True(File.Exists(v2.FilePath));

                var latest = lib.GetLatestModelVersion(v1.Source, v1.ModelName);
                Assert.NotNull(latest);
                Assert.Equal(v2, latest);
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
        public async Task TesConfigurationLibrary()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddHttpClient<FileDownloadClient>();
            services.AddSingleton<ConfigurationLibraryTest>();

            StateStoreConfig stateConfig = null;

            try
            {
                using var provider = services.BuildServiceProvider();
                stateConfig = provider.GetRequiredService<StateStoreConfig>();
                using var source = new CancellationTokenSource();

                var lib = provider.GetRequiredService<ConfigurationLibraryTest>();
                await lib.Init(source.Token).ConfigureAwait(false);

                bool dirExists = Directory.Exists("./configurations");
                Assert.True(dirExists, "Should have created a directory for the files");

                Assert.NotEmpty(lib.State);
                var state = Assert.Contains(
                    "PROSPER-SC-IPR_VLP-Connector_Test_Model", // This simulator configuration should exist in CDF
                    (IReadOnlyDictionary<string, TestConfigurationState>)lib.State);
                Assert.Equal("PROSPER", state.Source);
                Assert.Equal("Connector Test Model", state.ModelName);

                // Start the library update loop that download and parses the files, stop after 5 secs
                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                var linkedToken = linkedTokenSource.Token;
                linkedTokenSource.CancelAfter(TimeSpan.FromSeconds(5)); // should be enough time to download the file from CDF and parse it
                var modelLibTasks = lib.GetRunTasks(linkedToken);
                await modelLibTasks
                    .RunAll(linkedTokenSource)
                    .ConfigureAwait(false);

                // Verify that the files were downloaded and processed
                Assert.True(state.Deserialized);
                Assert.False(string.IsNullOrEmpty(state.FilePath));
                Assert.True(File.Exists(state.FilePath));

                var simConf = lib.GetSimulationConfiguration(
                    "PROSPER", "Connector Test Model", "IPR/VLP", null);
                Assert.NotNull(simConf);
                Assert.Equal("IPR/VLP", simConf.CalculationType);

                var simConfState = lib.GetSimulationConfigurationState(
                    "PROSPER", "Connector Test Model", "IPR/VLP", null);
                Assert.NotNull(simConfState);
                Assert.Equal(state, simConfState);
            }
            finally
            {
                if (Directory.Exists("./configurations"))
                {
                    Directory.Delete("./configurations", true);
                }
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
    public class ModeLibraryTest : ModelLibraryBase<TestFileState, ModelStateBasePoco>
    {
        public ModeLibraryTest(
            CogniteDestination cdf,
            ILogger<ModeLibraryTest> logger,
            FileDownloadClient downloadClient,
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
                store)
        {
        }

        protected override Task ExtractModelInformation(IEnumerable<TestFileState> modelStates, CancellationToken token)
        {
            return Task.Run(() =>
            {
                foreach (var state in modelStates)
                {
                    state.Processed = true;
                }
            }, token);
        }

        protected override TestFileState StateFromFile(CogniteSdk.File file)
        {
            return new TestFileState(file.ExternalId)
            {
                CdfId = file.Id,
                DataSetId = file.DataSetId,
                CreatedTime = file.CreatedTime,
                UpdatedTime = file.LastUpdatedTime,
                ModelName = file.Metadata[ModelMetadata.NameKey],
                Source = file.Source,
                Processed = false,
                Version = int.Parse(file.Metadata[ModelMetadata.VersionKey])
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
        ConfigurationLibraryBase<TestConfigurationState, FileStatePoco, SimulationConfigurationWithDataSampling>
    {
        public ConfigurationLibraryTest(
            CogniteDestination cdf, 
            ILogger<ConfigurationLibraryTest> logger, 
            FileDownloadClient downloadClient, 
            IExtractionStateStore store = null) : 
            base(
                new FileLibraryConfig
                {
                    FilesDirectory = "./configurations",
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
                cdf, logger, downloadClient, store)
        {
        }

        protected override TestConfigurationState StateFromFile(CogniteSdk.File file)
        {
            return new TestConfigurationState(file.ExternalId)
            {
                CdfId = file.Id,
                DataSetId = file.DataSetId,
                CreatedTime = file.CreatedTime,
                UpdatedTime = file.LastUpdatedTime,
                ModelName = file.Metadata[ModelMetadata.NameKey],
                Source = file.Source,
                Deserialized = false
            };
        }
    }
}