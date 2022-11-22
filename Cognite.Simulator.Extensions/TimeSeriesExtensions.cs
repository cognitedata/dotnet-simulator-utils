using CogniteSdk.Resources;
using Cognite.Extensions;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using CogniteSdk;

namespace Cognite.Simulator.Extensions
{
    public static class TimeSeriesExtensions
    {

        public static async Task<IEnumerable<TimeSeries>> CreateBoundaryConditions(
            this TimeSeriesResource timeSeries,
            Dictionary<string, BoundaryCondition> boundaryConditions,
            CancellationToken token)
        {
            if (boundaryConditions == null)
            {
                throw new ArgumentNullException(nameof(boundaryConditions));
            }
            if (boundaryConditions.Count == 0)
            {
                return Enumerable.Empty<TimeSeries>();
            }
            var result = await timeSeries.GetOrCreateTimeSeriesAsync(
                boundaryConditions.Keys,
                ids =>
                {
                    return ids.Select(id =>
                    {
                        var bc = boundaryConditions[id];
                        return new TimeSeriesCreate
                        {
                            ExternalId = id,
                            Name = $"{bc.Model.Name.ReplaceSlashAndBackslash("_")} - {bc.Name.ReplaceSlashAndBackslash("_")}",
                            IsStep = true,
                            Description = $"Boundary condition for model {bc.Model.Name}",
                            Unit = bc.Unit,
                            DataSetId = bc.DataSetId,
                            IsString = false,
                            Metadata = new Dictionary<string, string>()
                            {
                                { "simulator", bc.Model.Simulator },
                                { "dataType", SimulatorDataType.BoundaryCondition.MetadataValue() },
                                { "variableType", bc.Key },
                                { "variableName", bc.Name },
                                { "modelName", bc.Model.Name }
                            }
                        };
                    });
                },
                100,
                5,
                RetryMode.None,
                SanitationMode.None,
                token).ConfigureAwait(false);
            if (!result.IsAllGood)
            {
                throw new BoundaryConditionsCreationException(
                    "Cannot create boundary conditions",
                    result.Errors);
            }
            return result.Results;
        }
    }

    /// <summary>
    /// Represent errors related to read/write boundary conditions in CDF
    /// </summary>
    public class BoundaryConditionsCreationException : CogniteException
    {
        /// <summary>
        /// Create a new exception containing the provided <paramref name="errors"/> and <paramref name="message"/>
        /// </summary>
        public BoundaryConditionsCreationException(string message, IEnumerable<CogniteError> errors)
            : base(message, errors)
        {
        }
    }

}
