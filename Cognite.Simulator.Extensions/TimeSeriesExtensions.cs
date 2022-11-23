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
    /// <summary>
    /// Class containing extensions to the CDF Time Series resource with utility methods
    /// for simulator integrations
    /// </summary>
    public static class TimeSeriesExtensions
    {
        /// <summary>
        /// Created time series in CDF that represent the boundary conditions of a simulator
        /// model. If the time series already exists, it is returned instead
        /// </summary>
        /// <param name="timeSeries">CDF time series resource</param>
        /// <param name="boundaryConditions">Dictionary of (time series external id, boundary condition) pairs</param>
        /// <param name="token"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="BoundaryConditionsCreationException"></exception>
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
