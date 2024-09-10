using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using CogniteSdk.Alpha;
using Cognite.Simulator.Utils;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Cognite.Extractor.Utils;
using Microsoft.Extensions.Logging;

using Cognite.Simulator.Tests.UtilsTests;

namespace Cognite.Simulator.Tests
{
    [Collection(nameof(SequentialTestCollection))]
    public class TestRoutineLibrary
    {
        [Fact]
        public async Task TestSimulatorCursor()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();
            services.AddHttpClient<FileStorageClient>();
            services.AddSingleton(new ConnectorConfig
            {
                NamePrefix = SeedData.TestIntegrationExternalId,
                AddMachineNameSuffix = false,
            });

            using var provider = services.BuildServiceProvider();
            var cdf = provider.GetRequiredService<CogniteDestination>();
            var logger = provider.GetRequiredService<ILogger<RoutineLibraryTest>>();

            var routineLibrary = new RoutineLibraryTest(cdf, logger);
            var FileStorageClient = provider.GetRequiredService<FileStorageClient>();
            using var source = new CancellationTokenSource();
            await SeedData.GetOrCreateSimulator(cdf.CogniteClient, SeedData.SimulatorCreate).ConfigureAwait(false);
            await TestHelpers.SimulateASimulatorRunning(cdf.CogniteClient, SeedData.TestIntegrationExternalId).ConfigureAwait(false);


            var rev1 = await SeedData.GetOrCreateSimulatorRoutineRevision(
                cdf.CogniteClient,
                FileStorageClient,
                SeedData.SimulatorRoutineCreateWithTsAndExtendedIO,
                SeedData.SimulatorRoutineRevisionWithTsAndExtendedIO
            ).ConfigureAwait(false);

            var rev2 = await SeedData.GetOrCreateSimulatorRoutineRevision(
                cdf.CogniteClient,
                FileStorageClient,
                SeedData.SimulatorRoutineCreateScheduled,
                SeedData.SimulatorRoutineRevisionCreateScheduled
            ).ConfigureAwait(false);

            routineLibrary.PaginationLimit = 1;
            await routineLibrary.Init(source.Token).ConfigureAwait(false);

            Console.WriteLine("RoutineLibraryTest: TestSimulatorCursor");

            Assert.Contains(rev1.Id.ToString(), routineLibrary.RoutineRevisions);
            Assert.Contains(rev2.Id.ToString(), routineLibrary.RoutineRevisions);
            Assert.Equal(2, routineLibrary.RoutineRevisions.Count);
        }
    }
}