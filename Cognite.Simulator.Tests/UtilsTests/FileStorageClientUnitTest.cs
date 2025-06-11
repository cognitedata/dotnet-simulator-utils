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
        HttpResponseMessage MockFilesDownloadEndpoint(long size)
        {
            var response = new HttpResponseMessage() { Content = new ByteArrayContent(new byte[1]) };
            response.Content.Headers.Add("Content-Length", size.ToString());
            return response;
        }

        HttpResponseMessage MockFilesDownloadEndpointMaxSize()
        {
            return MockFilesDownloadEndpoint(FileStorageClient.MaxFileDownloadSize + 1);
        }

        HttpResponseMessage MockFilesDownloadEndpointLarge()
        {
            return MockFilesDownloadEndpoint(FileStorageClient.LargeFileSize + 1);
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

        [Fact]
        public async Task TestFileStorageClientFailLargeFiles()
        {
            var endpointMockTemplates = new List<SimpleRequestMocker>
            {
                new SimpleRequestMocker(uri => uri.Contains("/files/download"), MockFilesDownloadEndpointLarge, 1)
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

            await Assert.ThrowsAsync<ConnectorException>(async () => await client.DownloadFileAsync(new Uri("http://localhost/files/download"), "test.txt"));

            VerifyLog(mockedLogger, LogLevel.Warning, $"File size exceeds the maximum allowed size to be downloded now, but can still be downloaded later: {FileStorageClient.LargeFileSize} bytes, actual size: {FileStorageClient.LargeFileSize + 1}", Times.Once(), true);
        }
    }
}
