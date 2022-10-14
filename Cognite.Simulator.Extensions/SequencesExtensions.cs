using Cognite.Extensions;
using Cognite.Extractor.Common;
using CogniteSdk;
using CogniteSdk.Resources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cognite.Simulator.Extensions
{
    /// <summary>
    /// Class containing extensions to the CDF Sequences resource with utility methods
    /// for simulator integrations
    /// </summary>
    public static class SequencesExtensions
    {
        /// <summary>
        /// For the specified <paramref name="modelName"/> and <paramref name="simulator"/>, 
        /// find the sequence rows with the mapping of the model boundary conditions to
        /// time series external ids.
        /// </summary>
        /// <param name="sequences">CDF sequences resource</param>
        /// <param name="simulator">Simulator name</param>
        /// <param name="modelName">Model name</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Sequence data (rows) containing the boundary conditions map</returns>
        /// <exception cref="BoundaryConditionsMapNotFoundException">Thrown when a sequence containing
        /// the boundary conditons map cannot be found</exception>
        public static async Task<SequenceData> FindModelBoundaryConditions(
            this SequencesResource sequences,
            string simulator,
            string modelName,
            CancellationToken token)
        {
            var bcSeqs = await sequences.ListAsync(new SequenceQuery
            {
                Filter = new SequenceFilter
                {
                    ExternalIdPrefix = $"{simulator}-BC-",
                    Metadata = new Dictionary<string, string>
                    {
                        { ModelMetadata.NameKey, modelName },
                        { BaseMetadata.DataTypeKey, BoundaryConditionsMapMetadata.DataType.MetadataValue()}
                    }
                },
            }, token).ConfigureAwait(false);
            if (!bcSeqs.Items.Any())
            {
                // No boundary conditions map defined yet
                throw new BoundaryConditionsMapNotFoundException(
                    "Sequence mapping boundary conditions to time series not found in CDF");
            }

            var bcSeq = bcSeqs.Items.First();

            return await sequences.ListRowsAsync(new SequenceRowQuery
            {
                ExternalId = bcSeq.ExternalId
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// For each simulator in <paramref name="simulators"/>, retrieve or create 
        /// a simulator integration sequence in CDF
        /// </summary>
        /// <param name="sequences">CDF sequences resource</param>
        /// <param name="connectorName">Name of the connector associated with the integration</param>
        /// <param name="simulators">Dictionary with (simulator name, data set id) pairs</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Retrieved or created sequences</returns>
        /// <exception cref="SimulatorIntegrationSequenceException">Thrown when one or more sequences
        /// could not be created. The exception contatins the list of errors</exception>
        public static async Task<IEnumerable<Sequence>> GetOrCreateSimulatorIntegrations(
            this SequencesResource sequences,
            string connectorName,
            Dictionary<string, long> simulators,
            CancellationToken token)
        {
            var result = new List<Sequence>();
            if (simulators == null || !simulators.Any())
            {
                return result;
            }

            var toCreate = new Dictionary<string, SequenceCreate>();
            foreach (var simulator in simulators)
            {
                var metadata = new Dictionary<string, string>()
                {
                    { BaseMetadata.DataTypeKey, SimulatorIntegrationMetadata.DataType.MetadataValue() },
                    { BaseMetadata.SimulatorKey, simulator.Key },
                    { SimulatorIntegrationMetadata.ConnectorNameKey, connectorName }
                };
                var query = new SequenceQuery
                {
                    Filter = new SequenceFilter
                    {
                        Metadata = metadata
                    }
                };
                var seqs = await sequences.ListAsync(query, token).ConfigureAwait(false);
                if (seqs.Items.Any())
                {
                    result.Add(seqs.Items.First());
                }
                else
                {
                    metadata.Add(BaseMetadata.DataModelVersionKey, BaseMetadata.DataModelVersionValue);
                    var createObj = new SequenceCreate
                    {
                        Name = $"{simulator.Key} Simulator Integration",
                        ExternalId = $"{simulator.Key}-INTEGRATION-{DateTime.UtcNow.ToUnixTimeMilliseconds()}",
                        Description = $"Details about {simulator.Key} integration",
                        DataSetId = simulator.Value,
                        Metadata = metadata,
                        Columns = new List<SequenceColumnWrite> {
                            new SequenceColumnWrite {
                                Name = KeyValuePairSequenceColumns.KeyName,
                                ExternalId = KeyValuePairSequenceColumns.Key,
                                ValueType = MultiValueType.STRING,
                            },
                            new SequenceColumnWrite {
                                Name = KeyValuePairSequenceColumns.ValueName,
                                ExternalId = KeyValuePairSequenceColumns.Value,
                                ValueType = MultiValueType.STRING
                            }
                        }
                    };
                    toCreate.Add(createObj.ExternalId, createObj);
                }
            }
            var createdSequences = await sequences.GetOrCreateAsync(
                toCreate.Keys,
                (ids) => toCreate
                    .Where(o => ids.Contains(o.Key))
                    .Select(o => o.Value).ToList(),
                chunkSize: 10,
                throttleSize: 1,
                RetryMode.None,
                SanitationMode.None,
                token).ConfigureAwait(false);

            if (!createdSequences.IsAllGood)
            {
                throw new SimulatorIntegrationSequenceException("Could not find or create simulator integration sequence", createdSequences.Errors);
            }
            result.AddRange(createdSequences.Results);
            return result;
        }

        /// <summary>
        /// For each simulator in <paramref name="simulators"/>, update the simulator integration
        /// sequence with the connector heartbeat (last time seen)
        /// </summary>
        /// <param name="sequences">CDF Sequences resource</param>
        /// <param name="init">If true, the data set id and connector version rows are also 
        /// updated. Else, only the heartbeat row is updated</param>
        /// <param name="connectorVersion">Version of the deployed connector</param>
        /// <param name="simulators">Dictionary with (simulator integration sequence external id, 
        /// data set id) pairs</param>
        /// <param name="token">Cancellation token</param>
        /// <exception cref="SimulatorIntegrationSequenceException">Thrown when one or more sequences
        /// rows could not be updated. The exception contatins the list of errors</exception>
        public static async Task UpdateSimulatorIntegrationsHeartbeat(
            this SequencesResource sequences,
            bool init,
            string connectorVersion,
            Dictionary<string, long> simulators,
            CancellationToken token)
        {
            if (simulators == null || !simulators.Any())
            {
                return;
            }

            var rowsToCreate = new List<SequenceDataCreate>();
            foreach (var simulator in simulators)
            {
                var rows = new List<SequenceRow> {
                    new SequenceRow {
                        RowNumber = 0,
                        Values = new List<MultiValue> {
                            MultiValue.Create(SimulatorIntegrationSequenceRows.Heartbeat),
                            MultiValue.Create($"{DateTime.UtcNow.ToUnixTimeMilliseconds()}"),
                        }
                    }
                };
                if (init)
                {
                    // Data set and version could only have changed on connector restart
                    rows.Add(new SequenceRow
                    {
                        RowNumber = 1,
                        Values = new List<MultiValue> {
                            MultiValue.Create(SimulatorIntegrationSequenceRows.DataSetId),
                            MultiValue.Create($"{simulator.Value}")
                        }
                    });
                    rows.Add(new SequenceRow
                    {
                        RowNumber = 2,
                        Values = new List<MultiValue> {
                            MultiValue.Create(SimulatorIntegrationSequenceRows.ConnectorVersion),
                            MultiValue.Create(connectorVersion),
                        }
                    });
                }
                var rowCreate = new SequenceDataCreate
                {
                    ExternalId = simulator.Key,
                    Columns = new List<string> {
                            KeyValuePairSequenceColumns.Key,
                            KeyValuePairSequenceColumns.Value
                        },
                    Rows = rows
                };
                rowsToCreate.Add(rowCreate);
            }
            var result = await sequences.InsertAsync(
                rowsToCreate,
                keyChunkSize: 10,
                valueChunkSize: 100,
                sequencesChunk: 10,
                throttleSize: 1,
                RetryMode.None,
                SanitationMode.None,
                token).ConfigureAwait(false);
            if (!result.IsAllGood)
            {
                throw new SimulatorIntegrationSequenceException("Could not update simulator integration sequence", result.Errors);
            }
        }

        /// <summary>
        /// Store tabular simulation results as sequences
        /// </summary>
        /// <param name="sequences">CDF Sequence resource</param>
        /// <param name="externalId">External id of the sequence. Set to <c>null</c> to generate a new external id</param>
        /// <param name="rowStart">Write rows starting from this index</param>
        /// <param name="dataSetId">Data set id</param>
        /// <param name="results">Simulation results object</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>The sequence containing the simulation results</returns>
        /// <exception cref="SimulationTabularResultsException">Thrown when it is not possible to store the results</exception>
        public static async Task<Sequence> StoreSimulationResults(
            this SequencesResource sequences,
            string externalId,
            int rowStart,
            long? dataSetId,
            SimulationTabularResults results,
            CancellationToken token)
        {
            if (results == null || results.Columns == null || !results.Columns.Any())
            {
                return null;
            }

            // Count the number of rows
            var test = results.Columns
                .Select(r =>
                {
                    if (r.Value is SimulationNumericResultColumn numCol)
                    {
                        return numCol.Rows.Count();
                    }
                    else if (r.Value is SimulationStringResultColumn strCol)
                    {
                        return strCol.Rows.Count();
                    }
                    throw new SimulationTabularResultsException($"Invalid type for result column {r.Key}");
                });

            var rowCount = test.First();
            // Verify that all results have the same number of rows
            if (test.Where(r => r != rowCount).Any())
            {
                throw new SimulationTabularResultsException(
                    "All simulation result columns should contain the same number of rows");
            }

            if (string.IsNullOrEmpty(externalId))
            {
                rowStart = 0;
                var calcTypeForId = GetCalcTypeForIds(results.CalculationType, results.CalculationTypeUserDefined);
                var modelNameForId = results.ModelName.ReplaceSpecialCharacters("_");
                externalId = $"{results.Simulator}-OUTPUT-{calcTypeForId}-{results.ResultType}-{modelNameForId}-{DateTime.UtcNow.ToUnixTimeMilliseconds()}";
            }

            var sequenceCreate = BuildResultsSequence(
                externalId,
                dataSetId,
                results);
            var createdSequences = await sequences.GetOrCreateAsync(
                new List<string> { sequenceCreate.ExternalId },
                (ids) => new List<SequenceCreate> { sequenceCreate },
                chunkSize: 1,
                throttleSize: 1,
                RetryMode.None,
                SanitationMode.None,
                token).ConfigureAwait(false);

            if (!createdSequences.IsAllGood)
            {
                throw new SimulationTabularResultsException(
                    "Could not find or create simulation results sequence", createdSequences.Errors);
            }

            // Check for changes in column properties or metadata and force a new
            // sequence creation if anything changed
            var sequence = createdSequences.Results.First();
            bool columnsMatch = sequence.Columns.Count() == sequenceCreate.Columns.Count() &&
                sequenceCreate.Columns
                .All(c1 => sequence.Columns
                .Any(c2 =>
                {
                    bool match = c2.ExternalId == c1.ExternalId && c2.Name == c1.Name && c2.ValueType == c1.ValueType;
                    if (match && c1.Metadata != null && c1.Metadata.Any())
                    {
                        if (c2.Metadata == null || c2.Metadata.Count != c1.Metadata.Count)
                        {
                            return false;
                        }
                        foreach (var pair in c1.Metadata)
                        {
                            bool mdMatch = c2.Metadata.TryGetValue(pair.Key, out var mdValue) && mdValue == pair.Value;
                            if (!mdMatch)
                            {
                                match = false;
                                break;
                            }
                        }
                    }
                    return match;
                }));
            if (!columnsMatch)
            {
                // If there are any updates to be done, force a new sequences creation
                // (A null externalId will force the creation of a new sequence)
                return await sequences.StoreSimulationResults(null, 0, dataSetId, results, token).ConfigureAwait(false);
            }

            var rows = new List<SequenceRow>();
            for (int i = 0; i < rowCount; ++i)
            {
                var rowValues = new List<MultiValue>();
                foreach (var v in results.Columns)
                {
                    if (v.Value is SimulationNumericResultColumn numCol)
                    {
                        rowValues.Add(MultiValue.Create(numCol.Rows.ElementAt(i)));
                    }
                    else if (v.Value is SimulationStringResultColumn strCol)
                    {
                        rowValues.Add(MultiValue.Create(strCol.Rows.ElementAt(i)));
                    }
                }
                var seqRow = new SequenceRow
                {
                    RowNumber = rowStart + i,
                    Values = rowValues
                };
                rows.Add(seqRow);
            }
            var seqData = new SequenceDataCreate
            {
                ExternalId = sequenceCreate.ExternalId,
                Columns = sequenceCreate.Columns.Select(c => c.ExternalId).ToList(),
                Rows = rows
            };

            var result = await sequences.InsertAsync(
                new List<SequenceDataCreate> { seqData },
                keyChunkSize: 10,
                valueChunkSize: 100,
                sequencesChunk: 10,
                throttleSize: 1,
                RetryMode.None,
                SanitationMode.None,
                token).ConfigureAwait(false);
            if (!result.IsAllGood)
            {
                throw new SimulationTabularResultsException($"Could not save simulation tabular results", result.Errors);
            }
            return sequence;
        }

        private static SequenceCreate BuildResultsSequence(
            string externalId,
            long? dataSet,
            SimulationTabularResults results)
        {
            var sequenceCreate = GetSequenceCreatePrototype(
                externalId,
                results.Simulator,
                results.ModelName,
                results.CalculationType,
                results.CalculationName,
                dataSet);
            if (results.CalculationType == "UserDefined" && !string.IsNullOrEmpty(results.CalculationTypeUserDefined))
            {
                sequenceCreate.Metadata.Add(CalculationMetadata.UserDefinedTypeKey, results.CalculationTypeUserDefined);
            }

            sequenceCreate.Name = $"{results.ResultName.ReplaceSlashAndBackslash("_")} " +
                $"- {results.CalculationName.ReplaceSlashAndBackslash("_")} - {results.ModelName.ReplaceSlashAndBackslash("_")}";
            sequenceCreate.Description = $"Calculation result for {results.CalculationName} - {results.ModelName}";
            sequenceCreate.Metadata.Add(CalculationMetadata.ResultTypeKey, results.ResultType);
            sequenceCreate.Metadata.Add(CalculationMetadata.ResultNameKey, results.ResultName);
            sequenceCreate.Columns = results.Columns.Select(oc =>
            {
                var col = new SequenceColumnWrite
                {
                    Name = oc.Key,
                    ExternalId = oc.Key,
                    ValueType = oc.Value is SimulationNumericResultColumn ? MultiValueType.DOUBLE : MultiValueType.STRING
                };
                if (oc.Value.Metadata != null && oc.Value.Metadata.Any())
                {
                    col.Metadata = oc.Value.Metadata;
                }
                return col;
            });

            return sequenceCreate;
        }

        private static string GetCalcTypeForIds(string calcType, string calcTypeUserDefined)
        {
            if (calcType == "UserDefined" && !string.IsNullOrEmpty(calcTypeUserDefined))
            {
                return $"{calcType.ReplaceSpecialCharacters("_")}-{calcTypeUserDefined.ReplaceSpecialCharacters("_")}";
            }
            return calcType.ReplaceSpecialCharacters("_");
        }

        private static SequenceCreate GetSequenceCreatePrototype(
            string externalId,
            string simulator,
            string modelName,
            string calculationType,
            string calculationName,
            long? dataSet)
        {
            var seqCreate = new SequenceCreate
            {
                ExternalId = externalId,
                Metadata = GetCommonMetadata(simulator, modelName, calculationType, calculationName)
            };
            if (dataSet.HasValue)
            {
                seqCreate.DataSetId = dataSet.Value;
            }
            return seqCreate;
        }

        private static Dictionary<string, string> GetCommonMetadata(
            string simulator,
            string modelName,
            string calculationType,
            string calculationName)
        {
            return new Dictionary<string, string>()
            {
                { BaseMetadata.DataModelVersionKey, BaseMetadata.DataModelVersionValue },
                { BaseMetadata.SimulatorKey, simulator },
                { ModelMetadata.NameKey, modelName },
                { CalculationMetadata.TypeKey, calculationType },
                { CalculationMetadata.NameKey, calculationName },
                { BaseMetadata.DataTypeKey, SimulatorDataType.SimulationOutput.MetadataValue() }
            };
        }


        /// <summary>
        /// Read the values of a <see cref="SequenceRow"/> and returns
        /// it as a string array
        /// </summary>
        /// <param name="row">CDF Sequence row</param>
        /// <returns>Array containg the row values</returns>
        public static string[] GetStringValues(this SequenceRow row)
        {
            var result = new List<string>();
            foreach (var val in row.Values)
            {
                if (val != null && val.Type == MultiValueType.STRING)
                {
                    result.Add(((MultiValue.String)val).Value);
                }
                else
                {
                    result.Add(null);
                }
            }
            return result.ToArray();
        }

    }

    /// <summary>
    /// Represents simulation tabular results as columns and rows
    /// </summary>
    public class SimulationTabularResults
    {
        /// <summary>
        /// Result type (e.g. SystemCurves)
        /// </summary>
        public string ResultType { get; set; }
        
        /// <summary>
        /// Result name (e.g. System Curves)
        /// </summary>
        public string ResultName { get; set; }
        
        /// <summary>
        /// Simulator name
        /// </summary>
        public string Simulator { get; set; }
        
        /// <summary>
        /// Model name
        /// </summary>
        public string ModelName { get; set; }
        
        /// <summary>
        /// Calculation type (e.g. IPR/VLP)
        /// </summary>
        public string CalculationType { get; set; }
        
        /// <summary>
        /// Calculation type - user defined (e.g. CustomIprVlp)
        /// </summary>
        public string CalculationTypeUserDefined { get; set; }

        /// <summary>
        /// Calculation name (e.g. Rate by Nodal Analysis)
        /// </summary>
        public string CalculationName { get; set; }
        
        /// <summary>
        /// Columns with simulation results. The dictionary key
        /// represents the column header
        /// </summary>
        public IDictionary<string, SimulationResultColumn> Columns { get; set; }
    }

    /// <summary>
    /// Represents a simulation result column
    /// </summary>
    public abstract class SimulationResultColumn
    {
        /// <summary>
        /// Metadata to be atached to the column
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Represents a numeric simulation result column
    /// </summary>
    public class SimulationNumericResultColumn : SimulationResultColumn
    {
        /// <summary>
        /// Numeric row values
        /// </summary>
        public IEnumerable<double> Rows { get; set; }
    }

    /// <summary>
    /// Represents a string simulation result column
    /// </summary>
    public class SimulationStringResultColumn : SimulationResultColumn
    {
        /// <summary>
        /// String row values
        /// </summary>
        public IEnumerable<string> Rows { get; set; }
    }

    /// <summary>
    /// Represent errors related to read/write simulator integration sequences in CDF
    /// </summary>
    public class SimulatorIntegrationSequenceException : Exception
    {
        /// <summary>
        /// Errors that triggered this exception
        /// </summary>
        public IEnumerable<CogniteError> CogniteErrors { get; private set; }

        /// <summary>
        /// Create a new exception containing the provided <paramref name="errors"/> and <paramref name="message"/>
        /// </summary>
        public SimulatorIntegrationSequenceException(string message, IEnumerable<CogniteError> errors)
            : base(message)
        {
            CogniteErrors = errors;
        }
    }

    /// <summary>
    /// Represents errors related to read/write tabular simulation results
    /// </summary>
    public class SimulationTabularResultsException : Exception
    {
        /// <summary>
        /// Errors that triggered this exception
        /// </summary>
        public IEnumerable<CogniteError> CogniteErrors { get; private set; }

        /// <summary>
        /// Create a new exception containing the provided <paramref name="errors"/> and <paramref name="message"/>
        /// </summary>
        public SimulationTabularResultsException(string message, IEnumerable<CogniteError> errors)
            : base(message)
        {
            CogniteErrors = errors;
        }

        /// <summary>
        /// Creates a new exception containing the provided <paramref name="message"/>
        /// </summary>
        /// <param name="message"></param>
        public SimulationTabularResultsException(string message) : base(message)
        {
            CogniteErrors = new List<CogniteError>();
        }
    }

    /// <summary>
    /// Represent errors related to reading boundary conditions sequences in CDF
    /// </summary>
    public class BoundaryConditionsMapNotFoundException : Exception
    {
        /// <summary>
        /// Creates a new exception containing the provided <paramref name="message"/>
        /// </summary>
        public BoundaryConditionsMapNotFoundException(string message) : base(message)
        {
        }
    }
}
