using CogniteSdk;
using Cognite.Simulator.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CogniteSdk.Resources;
using System.Linq;
using System;

namespace Cognite.Simulator.Tests
{
    public class FilesTest
    {
        [Fact]
        public async Task TestFindSimulatorFiles()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();

            using var provider = services.BuildServiceProvider();
            var cdf = provider.GetRequiredService<Client>();
            var files = cdf.Files;

            // The sources argument should be mandatory 
            Task testCall() => files.FindSimulatorFiles(
                SimulatorDataType.ModelFile,
                new Dictionary<string, long?>() {},
                null,
                token: CancellationToken.None);
            await Assert.ThrowsAsync<ArgumentException>(testCall).ConfigureAwait(false);

            // Assumes there are some dummy model files in the integration test project
            var simFiles = await files.FindSimulatorFiles(
                SimulatorDataType.ModelFile,
                new Dictionary<string, long?>()
                {
                    { "PROSPER", null }
                },
                null,
                token: CancellationToken.None).ConfigureAwait(false);
            Assert.NotNull(simFiles);
            Assert.NotEmpty(simFiles);
            foreach (var model in simFiles)
            {
                Assert.Equal("PROSPER", model.Metadata["simulator"]);
                Assert.Equal(ModelMetadata.DataType.MetadataValue(), model.Metadata[BaseMetadata.DataTypeKey]);
            }
        }

    }
}