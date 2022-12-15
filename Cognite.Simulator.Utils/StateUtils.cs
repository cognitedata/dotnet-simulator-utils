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
    internal static class StateUtils
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
                    DeleteLocalFile(state.FilePath);
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
                DeleteLocalFile(state.FilePath);
            }
        }

        internal static void DeleteLocalFile(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
