using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Interface for abstracting file system operations
    /// </summary>
    public interface IFileSystem
    {
        /// <summary>
        /// Gets all files in the immediate subfolders of a specified root path
        /// </summary>
        /// <param name="rootPath">The root directory path</param>
        /// <returns>A hash set containing paths of all files in the immediate subfolders</returns>
        HashSet<string> GetFilesInSubfolders(string rootPath);
    }

    /// <summary>
    /// Default implementation of IFileSystem using System.IO
    /// </summary>
    public class FileSystem : IFileSystem
    {
        /// <inheritdoc/>
        public HashSet<string> GetFilesInSubfolders(string rootPath)
        {
            return [.. Directory.EnumerateDirectories(rootPath).SelectMany(Directory.EnumerateFiles)];
        }
    }
}
