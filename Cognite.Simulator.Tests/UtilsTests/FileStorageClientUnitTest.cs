using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
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
        public async Task TestFileStorageClientDeletesPartialFileOnIOException()
        {
            // Arrange
            var tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".zip");
            var endpointMockTemplates = new List<SimpleRequestMocker>
            {
                new SimpleRequestMocker(uri => uri.Contains("/files/download"), MockDownloadWithIOException, 1)
            };

            var mockHttpClientFactory = GetMockedHttpClientFactory(MockRequestsAsync(endpointMockTemplates));
            var mockedLogger = new Mock<ILogger<FileStorageClient>>();

            var httpClient = mockHttpClientFactory.Object.CreateClient("");
            var client = new FileStorageClient(httpClient, mockedLogger.Object);

            try
            {
                // Act
                var downloaded = await client.DownloadFileAsync(new Uri("http://localhost/files/download"), tempFilePath);

                // Assert
                Assert.False(downloaded);
                Assert.False(File.Exists(tempFilePath), "Partial file must be deleted after IOException");
                VerifyLog(mockedLogger, LogLevel.Error, "I/O error occurred while saving the file into", Times.Once(), true);
            }
            finally
            {
                StateUtils.DeleteLocalFile(tempFilePath);
            }
        }

        private static HttpResponseMessage MockDownloadWithIOException()
        {
            var content = new StreamContent(new ThrowingOnReadStream());
            content.Headers.ContentLength = 100;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }

        private sealed class ThrowingOnReadStream : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => 0; set { } }
            public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Simulated I/O error");
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                => Task.FromException<int>(new IOException("Simulated I/O error during stream read"));
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        [Fact]
        public async Task TestFileStorageClientFailMaxSizeFiles()
        {

            var endpointMockTemplates = new List<SimpleRequestMocker>
            {
                new SimpleRequestMocker(uri => uri.Contains("/files/download"), MockFilesDownloadEndpointMaxSize, 1)
            };

            var services = new ServiceCollection();
            var mockHttpClientFactory = GetMockedHttpClientFactory(MockRequestsAsync(endpointMockTemplates));
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
