using System.Collections.Generic;
using System.IO;

using Cognite.Simulator.Utils;

using Moq;

using Xunit;

namespace Cognite.Simulator.Tests.UtilsTests;

public class StateUtilsTest
{
    [Fact]
    public void GetLocalFilesCache_WithSimpleStructure_ReturnsExpectedFiles()
    {
        // Arrange
        var rootPath = "/test/root";

        // Create test file states
        var state = new Dictionary<string, FileState>();
        var fileStatePocos = new[]
        {
            new FileStatePoco { Id = "1", CdfId = 1, FilePath = Path.Combine(rootPath, "folder1", "file1.txt") },
            new FileStatePoco { Id = "3", CdfId = 3, FilePath = Path.Combine(rootPath, "folder3", "file3.txt") }
        };

        foreach (var poco in fileStatePocos)
        {
            var newState = new FileState();
            newState.Init(poco);
            state.Add(poco.Id, newState);
        }

        var mockFileSystem = new Mock<IFileSystem>();
        mockFileSystem.Setup(fs => fs.GetFilesInSubfolders(rootPath))
            .Returns(new HashSet<string>
            {
                Path.Combine(rootPath, "folder1", "file1.txt"),
                Path.Combine(rootPath, "folder1", "file1.1.txt"),
                Path.Combine(rootPath, "folder2", "file2.txt"),
            });

        // Act
        var result = StateUtils.GetLocalFilesCache(mockFileSystem.Object, state, rootPath);

        // Assert
        Assert.Single(result);
        Assert.True(result.ContainsKey(1));
        Assert.Equal(Path.Combine(rootPath, "folder1", "file1.txt"), result[1]);
    }
}

