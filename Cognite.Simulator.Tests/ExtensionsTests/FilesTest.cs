using System;

using Cognite.Simulator.Extensions;

using CogniteSdk;


using Xunit;

namespace Cognite.Simulator.Tests.ExtensionsTests
{
    public class FilesTests
    {
        [Theory]
        [InlineData("test.txt", "txt")]
        [InlineData("file.PDF", "pdf")]
        [InlineData("path/to/file.DOC", "doc")]
        [InlineData("multi.part.name.XML", "xml")]
        [InlineData("file.with.dots.in.name.json", "json")]
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
            Assert.Contains("File name cannot be null or empty. File ID: 123", exception.Message);
        }

        [Theory]
        [InlineData("noextension")]
        [InlineData("ends.with.")]
        [InlineData(".startswithdot")]
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
    }
}
