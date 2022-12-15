using Cognite.Extractor.StateStorage;
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
    public class FileLibraryTest
    {
        [Fact]
        public async Task TestFileLibrary()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddHttpClient<FileDownloadClient>();
            services.AddTransient<TestLibrary>();

            try
            {
                using var provider = services.BuildServiceProvider();
                using var source = new CancellationTokenSource();

                var lib = provider.GetRequiredService<TestLibrary>();
                await lib.Init(source.Token).ConfigureAwait(false);

                bool dirExists = Directory.Exists("./files");
                Assert.True(dirExists, "Should have created a directory for the files");

                Assert.NotEmpty(lib.State);
                var v1 = Assert.Contains(
                    "PROSPER-Connector_Test_Model-1", // This file should exist in CDF
                    (IReadOnlyDictionary<string, TestFileState>)lib.State);
                Assert.Equal("PROSPER", v1.Source);
                Assert.Equal("Connector Test Model", v1.ModelName);
                Assert.False(v1.Processed);

                var v2 = Assert.Contains(
                    "PROSPER-Connector_Test_Model-2", // This file should exist in CDF
                    (IReadOnlyDictionary<string, TestFileState>)lib.State);
                Assert.Equal("PROSPER", v2.Source);
                Assert.Equal("Connector Test Model", v2.ModelName);
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
                Assert.True(File.Exists(v1.FilePath));
                Assert.True(v2.Processed);
                Assert.False(string.IsNullOrEmpty(v2.FilePath));
                Assert.True(File.Exists(v2.FilePath));
            }
            finally
            {
                if (Directory.Exists("./files"))
                {
                    Directory.Delete("./files", true);
                }
                if (File.Exists(CdfTestClient._statePath))
                {
                    File.Delete(CdfTestClient._statePath);
                }
            }
        }
    }

    /// <summary>
    /// Simple test state to keep track of files that have been processed
    /// </summary>
    public class TestFileState : FileState
    {
        public bool Processed { get; set; }
        public TestFileState(string id) : base(id)
        {
        }
        public override string GetExtension()
        {
            return "out";
        }

        public override string GetDataType()
        {
            return SimulatorDataType.ModelFile.MetadataValue();
        }
    }

    /// <summary>
    /// File library is abstract. Implement a simple mock library
    /// to test the base functionality
    /// </summary>
    public class TestLibrary : FileLibrary<TestFileState, FileStatePoco>
    {
        public TestLibrary(
            CogniteDestination cdf,
            ILogger<TestLibrary> logger,
            FileDownloadClient downloadClient,
            IExtractionStateStore store = null) :
            base(
                SimulatorDataType.ModelFile,
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
                        DataSetId = 7900866844615420
                    }
                },
                cdf,
                logger,
                downloadClient,
                store)
        {
        }

        protected override void ProcessDownloadedFiles(CancellationToken token)
        {
            var toProcess = _state.Values
                .Where(s => !string.IsNullOrEmpty(s.FilePath))
                .ToList();
            foreach(var state in toProcess)
            {
                state.Processed = true;
            }
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
            };
        }
    }
}
