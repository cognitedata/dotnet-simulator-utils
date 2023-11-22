using Cognite.Extractor.StateStorage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// Utility methods for managing state
    /// </summary>
    public static class StateUtils
    {
        internal static void RemoveUnusedState(
            this LiteDBStateStore store,
            string tableName,
            HashSet<string> idsToKeep)
        {    
            var col = store.Database.GetCollection<FileStatePoco>(tableName);
            var stateToDelete = col
                .Find(s => !idsToKeep.Contains(s.Id))
                .ToList();
            if (stateToDelete.Any())
            {
                foreach(var state in stateToDelete)
                {
                    if (state.IsInDirectory) {
                        // get directory path from file path 
                        // (file path is the directory path + file name)
                        var dirPath = Path.GetDirectoryName(state.FilePath);
                        DeleteLocalDirectory(dirPath);
                    } else {
                        DeleteLocalFile(state.FilePath);
                    }
                    col.Delete(state.Id);
                }
            }
        }

        internal static async Task RemoveFileStates(
            this IExtractionStateStore store,
            string tableName,
            IEnumerable<FileState> states,
            CancellationToken token)
        {
            if (!states.Any())
            {
                return;
            }
            await store.DeleteExtractionState(states, tableName, token)
                .ConfigureAwait(false);
            foreach (var state in states)
            {
                if (state.IsInDirectory)
                {
                    var dirPath = Path.GetDirectoryName(state.FilePath);
                    DeleteLocalDirectory(dirPath);
                } else {
                    DeleteLocalFile(state.FilePath);
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
