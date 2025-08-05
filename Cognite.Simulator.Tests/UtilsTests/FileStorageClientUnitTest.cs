using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

using Cognite.Simulator.Utils;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

using static Cognite.Simulator.Tests.UtilsTests.TestUtilities;

namespace Cognite.Simulator.Tests.UtilsTests
{
    [Collection(nameof(SequentialTestCollection))]
    public class FileStorageCLientUnitTest
    {
        HttpResponseMessage MockFilesDownloadEndpointMaxSize()
        {
            return MockFilesDownloadEndpoint(FileStorageClient.MaxFileDownloadSize + 1);
        }

        [Fact]
        public async Task TestFileStorageClientFailMaxSizeFiles()
        {

            var endpointMockTemplates = new List<SimpleRequestMocker>
            {
                new SimpleRequestMocker(uri => uri.Contains("/files/download"), MockFilesDownloadEndpointMaxSize, 1)
            };

            var services = new ServiceCollection();
            var httpMocks = GetMockedHttpClientFactory(MockRequestsAsync(endpointMockTemplates));
            var mockHttpClientFactory = httpMocks.factory;
            var mockedLogger = new Mock<ILogger<FileStorageClient>>();

            services.AddSingleton(mockHttpClientFactory.Object);
            services.AddSingleton<FileStorageClient>();
            services.AddCogniteTestClient();
            services.AddSingleton(mockedLogger.Object);

            var client = services.BuildServiceProvider().GetRequiredService<FileStorageClient>();

            var downloaded = await client.DownloadFileAsync(new Uri("http://localhost/files/download"), "test.txt");

            Assert.False(downloaded);
            VerifyLog(mockedLogger, LogLevel.Error, $"File size exceeds the maximum allowed size: {FileStorageClient.MaxFileDownloadSize} bytes, actual size: {FileStorageClient.MaxFileDownloadSize + 1}", Times.Once(), true);
        }
    }
}
