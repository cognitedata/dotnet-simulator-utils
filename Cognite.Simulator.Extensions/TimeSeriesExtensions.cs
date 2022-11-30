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
                        var metadata = bc.Model.GetCommonMetadata(BoundaryConditionMetadata.DataType);
                        metadata.AddRange(
                            new Dictionary<string, string>()
                            {
                                { BoundaryConditionMetadata.VariableTypeKey, bc.Key },
                                { BoundaryConditionMetadata.VariableNameKey, bc.Name },
                            });
                        return new TimeSeriesCreate
                        {
                            ExternalId = id,
                            Name = $"{bc.Model.Name.ReplaceSlashAndBackslash("_")} - {bc.Name.ReplaceSlashAndBackslash("_")}",
                            IsStep = true,
                            Description = $"Boundary condition for model {bc.Model.Name}",
                            Unit = bc.Unit,
                            DataSetId = bc.DataSetId,
                            IsString = false,
                            Metadata = metadata
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

        public static async Task<TimeSeries> GetOrCreateSimulationModelVersion(
            this TimeSeriesResource timeSeries,
            SimulatorCalculation calculation,
            long? dataSetId,
            CancellationToken token)
        {
            if (calculation == null)
            {
                throw new ArgumentNullException(nameof(calculation));
            }

            var externalId = $"{calculation.Model.Simulator}-MV-{calculation.GetCalcTypeForIds()}-{calculation.Model.GetModelNameForIds()}";
            var create = GetTimeSeriesCreatePrototype(externalId, SimulatorDataType.SimulationModelVersion, calculation, dataSetId, true);
            create.Name = $"{calculation.GetCalcTypeForNames()} - {calculation.Model.GetModelNameForNames()} model version";
            create.Description = $"Version of model {calculation.Model.Name} used in {calculation.Type} calculations";

            var ts = await timeSeries.GetOrCreateTimeSeriesAsync(
                new List<string> { externalId },
                (ids) => new List<TimeSeriesCreate> { create },
                100,
                5,
                RetryMode.None,
                SanitationMode.None,
                token).ConfigureAwait(false);
            if (!ts.IsAllGood)
            {
                throw new SimulationModelVersionCreationException($"Could not create simulation model version time series in CDF", ts.Errors);
            }
            return ts.Results.First();
        }

        public static async Task<IEnumerable<TimeSeries>> GetOrCreateSimulationInputs(
            this TimeSeriesResource timeSeries,
            IEnumerable<SimulationInput> inputs,
            long? dataSetId,
            CancellationToken token)
        {
            return await timeSeries.GetOrCreateSimulationTimeSeries(
                inputs,
                dataSetId,
                true,
                token).ConfigureAwait(false);
        }

        public static async Task<IEnumerable<TimeSeries>> GetOrCreateSimulationOutputs(
            this TimeSeriesResource timeSeries,
            IEnumerable<SimulationOutput> outputs,
            long? dataSetId,
            CancellationToken token)
        {
            return await timeSeries.GetOrCreateSimulationTimeSeries(
                outputs,
                dataSetId,
                false,
                token).ConfigureAwait(false);
        }

        private static async Task<IEnumerable<TimeSeries>> GetOrCreateSimulationTimeSeries(
            this TimeSeriesResource timeSeries,
            IEnumerable<SimulationTimeSeries> simTimeSeries,
            long? dataSetId,
            bool isInput,
            CancellationToken token)
        {
            var dataType = isInput ? SimulatorDataType.SimulationInput : SimulatorDataType.SimulationOutput;
            if (simTimeSeries == null)
            {
                throw new ArgumentNullException(nameof(simTimeSeries));
            }

            var tsToCreate = new Dictionary<string, TimeSeriesCreate>();
            foreach (var simTs in simTimeSeries)
            {
                var tsCreate = GetTimeSeriesCreatePrototype(simTs.TimeSeriesExternalId, dataType, simTs.Calculation, dataSetId);
                tsCreate.Name = simTs.TimeSeriesName;
                tsCreate.Description = simTs.TimeSeriesDescription;
                tsCreate.Unit = simTs.Unit;
                tsCreate.Metadata.Add(SimulationVariableMetadata.VariableTypeKey, simTs.Type);
                tsCreate.Metadata.Add(SimulationVariableMetadata.VariableNameKey, simTs.Name);

                if (simTs.Metadata != null)
                {
                    tsCreate.Metadata.AddRange(simTs.Metadata);
                }
                tsToCreate.Add(simTs.TimeSeriesExternalId, tsCreate);
            }
            if (!tsToCreate.Any())
            {
                return Enumerable.Empty<TimeSeries>();
            }
            var ts = await timeSeries.GetOrCreateTimeSeriesAsync(
                tsToCreate.Keys,
                (ids) => ids.Select(id => tsToCreate[id]),
                100,
                5,
                RetryMode.None,
                SanitationMode.None,
                token).ConfigureAwait(false);
            if (!ts.IsAllGood)
            {
                throw new SimulationTimeSeriesCreationException($"Could not create {dataType.MetadataValue()} time series in CDF", ts.Errors);
            }
            return ts.Results;
        }

        private static TimeSeriesCreate GetTimeSeriesCreatePrototype(
            string externalId,
            SimulatorDataType dataType,
            SimulatorCalculation calc,
            long? dataSet,
            bool isStep = false)
        {
            var tsCreate = new TimeSeriesCreate
            {
                ExternalId = externalId,
                IsStep = isStep,
                Metadata = calc.GetCommonMetadata(dataType)
            };
            if (dataSet.HasValue)
            {
                tsCreate.DataSetId = dataSet.Value;
            }
            return tsCreate;
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

    /// <summary>
    /// Represent errors related to read/write simulation inputs in CDF
    /// </summary>
    public class SimulationTimeSeriesCreationException : CogniteException
    {
        /// <summary>
        /// Create a new exception containing the provided <paramref name="errors"/> and <paramref name="message"/>
        /// </summary>
        public SimulationTimeSeriesCreationException(string message, IEnumerable<CogniteError> errors)
            : base(message, errors)
        {
        }
    }

    /// <summary>
    /// Represent errors related to read/write simulation model version time series in CDF
    /// </summary>
    public class SimulationModelVersionCreationException : CogniteException
    {
        /// <summary>
        /// Create a new exception containing the provided <paramref name="errors"/> and <paramref name="message"/>
        /// </summary>
        public SimulationModelVersionCreationException(string message, IEnumerable<CogniteError> errors)
            : base(message, errors)
        {
        }
    }
}
