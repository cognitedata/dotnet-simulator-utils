using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cognite.Extensions;
using Cognite.Extractor.StateStorage;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Utility methods for managing state
    /// </summary>
    public static class StateUtils
    {
        internal static void DeleteFileAndDirectory(string filePath, bool isInDirectory)
        {
            if (isInDirectory)
            {
                var dirPath = Path.GetDirectoryName(filePath);
                DeleteLocalDirectory(dirPath);
            }
            else
            {
                DeleteLocalFile(filePath);
            }
        }

        internal static void RemoveUnusedState(
            this LiteDBStateStore store,
            string tableName,
            HashSet<string> idsToKeep)
        {
            var col = store.Database.GetCollection<FileStatePoco>(tableName);
            var stateToDelete = col
                .Find(s => !idsToKeep.Contains(s.Id))
                .ToList();
            var filesInUseMap = col
                    .Find(f => !string.IsNullOrEmpty(f.FilePath) && idsToKeep.Contains(f.Id))
                    .ToDictionarySafe(f => f.FilePath, f => true);

            foreach (var state in stateToDelete)
            {
                if (state.FilePath != null && !filesInUseMap.ContainsKey(state.FilePath))
                {
                    DeleteFileAndDirectory(state.FilePath, state.IsInDirectory);
                }
                col.Delete(state.Id);
            }
        }

        internal static async Task RemoveFileStates<FileStateType>(
            this IExtractionStateStore store,
            string tableName,
            IEnumerable<(FileStateType, bool)> states,
            CancellationToken token)
            where FileStateType : FileState
        {
            if (!states.Any())
            {
                return;
            }
            await store.DeleteExtractionState(states.Select(s => s.Item1), tableName, token)
                .ConfigureAwait(false);
            foreach (var (state, withFile) in states)
            {
                if (withFile)
                {
                    DeleteFileAndDirectory(state.FilePath, state.IsInDirectory);
                }
            }
        }

        /// <summary>
        /// Delete a file stored locally
        /// </summary>
        /// <param name="path">Path to the file</param>
        public static void DeleteLocalFile(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// Delete a directory stored locally
        /// </summary>
        /// <param name="path">Path to the directory</param>
        public static void DeleteLocalDirectory(string path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }
}
