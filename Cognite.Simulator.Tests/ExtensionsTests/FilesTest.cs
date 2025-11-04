using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cognite.Simulator.Extensions;

using CogniteSdk;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cognite.Simulator.Tests.ExtensionsTests
{
    public class FilesTests
    {
        [Theory]
        [InlineData("test.txt", "txt")]
        [InlineData("file.PDF", "pdf")]
        [InlineData("path/to/file.DOC", "doc")]
        [InlineData("back\\slash\\file.txt", "txt")]
        [InlineData("multi.part.name.XML", "xml")]
        [InlineData("file.with.dots.in.name.json", "json")]
        [InlineData(".startswithdot", "startswithdot")]
        public void GetExtension_ValidFileName_ReturnsLowercaseExtension(string fileName, string expectedExtension)
        {
            // Arrange
            var file = new File
            {
                Name = fileName,
                Id = 123
            };

            // Act
            var extension = file.GetExtension();

            // Assert
            Assert.Equal(expectedExtension, extension);
        }

        [Fact]
        public void GetExtension_NullFile_ThrowsArgumentNullException()
        {
            // Arrange
            File? file = null;
            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => file.GetExtension());
            Assert.Equal("File cannot be null. (Parameter 'file')", exception.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void GetExtension_NullOrEmptyFileName_ThrowsArgumentException(string? fileName)
        {
            // Arrange
            var file = new File
            {
                Name = fileName,
                Id = 123
            };

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => file.GetExtension());
            Assert.Contains("File name cannot be null or empty.", exception.Message);
        }

        [Theory]
        [InlineData("noextension")]
        [InlineData("ends.with.")]
        [InlineData("   ")]
        public void GetExtension_InvalidFileName_ThrowsArgumentException(string fileName)
        {
            // Arrange
            var file = new File
            {
                Name = fileName,
                Id = 123
            };

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => file.GetExtension());
            Assert.Contains("File name does not contain a valid extension", exception.Message);
        }

        [Fact]
        public async Task TestRetrieveBatchAsync()
        {
            var services = new ServiceCollection();
            services.AddCogniteTestClient();

            using var provider = services.BuildServiceProvider();
            var cdf = provider.GetRequiredService<Client>();

            var filesListRes = await cdf.Files.ListAsync(new FileQuery() { Limit = 20 }, token: CancellationToken.None);

            Assert.True(filesListRes.Items.Count() > 1);

            var filesRes = await cdf.Files.RetrieveBatchAsync(
                filesListRes.Items.Select(f => f.Id).ToList(),
                1 // 1 to force multiple batches
            );

            Assert.Equivalent(filesListRes.Items, filesRes);
        }
    }
}
