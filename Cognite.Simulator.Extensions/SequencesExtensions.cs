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
        /// For the specified <paramref name="model"/>, 
        /// find the sequence rows with the mapping of the model boundary conditions to
        /// time series external ids.
        /// </summary>
        /// <param name="sequences">CDF sequences resource</param>
        /// <param name="model">Simulator model</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Sequence data (rows) containing the boundary conditions map</returns>
        /// <exception cref="BoundaryConditionsMapNotFoundException">Thrown when a sequence containing
        /// the boundary conditions map cannot be found</exception>
        public static async Task<SequenceData> FindModelBoundaryConditions(
            this SequencesResource sequences,
            SimulatorModel model,
            CancellationToken token)
        {
            if (model == null || string.IsNullOrEmpty(model.Name) || string.IsNullOrEmpty(model.Simulator))
            {
                throw new ArgumentException("Simulator model should have valid name and simulator properties", nameof(model));
            }
            var bcSeqs = await sequences.ListAsync(new SequenceQuery
            {
                Filter = new SequenceFilter
                {
                    ExternalIdPrefix = $"{model.Simulator}-BC-",
                    Metadata = new Dictionary<string, string>
                    {
                        { ModelMetadata.NameKey, model.Name },
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
        /// For each simulator in <paramref name="integrations"/>, retrieve or create 
        /// a simulator integration sequence in CDF
        /// </summary>
        /// <param name="sequences">CDF sequences resource</param>
        /// <param name="integrations">List of simulation integration details</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Retrieved or created sequences</returns>
        /// <exception cref="SimulatorIntegrationSequenceException">Thrown when one or more sequences
        /// could not be created. The exception contains the list of errors</exception>
        public static async Task<IEnumerable<Sequence>> GetOrCreateSimulatorIntegrations(
            this SequencesResource sequences,
            IEnumerable<SimulatorIntegration> integrations,
            CancellationToken token)
        {
            var result = new List<Sequence>();
            if (integrations == null || !integrations.Any())
            {
                return result;
            }

            var toCreate = new Dictionary<string, SequenceCreate>();
            foreach (var integration in integrations)
            {
                var metadata = new Dictionary<string, string>()
                {
                    { BaseMetadata.DataTypeKey, SimulatorIntegrationMetadata.DataType.MetadataValue() },
                    { BaseMetadata.SimulatorKey, integration.Simulator },
                    { SimulatorIntegrationMetadata.ConnectorNameKey, integration.ConnectorName }
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
                        Name = $"{integration.Simulator} Simulator Integration",
                        ExternalId = $"{integration.Simulator}-INTEGRATION-{DateTime.UtcNow.ToUnixTimeMilliseconds()}",
                        Description = $"Details about {integration.Simulator} integration",
                        Metadata = metadata,
                        Columns = GetKeyValueColumnWrite()
                    };
                    if (integration.DataSetId.HasValue)
                    {
                        createObj.DataSetId = integration.DataSetId.Value;
                    }
                    toCreate.Add(createObj.ExternalId, createObj);
                }
            }
            if (toCreate.Any())
            {
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
                if (createdSequences.Results != null)
                {
                    result.AddRange(createdSequences.Results);
                }
            }
            return result;
        }

        /// <summary>
        /// Update the simulator integration sequence with the connector heartbeat (last time seen)
        /// </summary>
        /// <param name="sequences">CDF Sequences resource</param>
        /// <param name="init">If true, the data set id, connector version and extra information rows are also 
        /// updated. Else, only the heartbeat row is updated</param>
        /// <param name="sequenceExternalId">Simulator integration sequence external id</param>
        /// <param name="update">Data to be updated, if init is set to true</param>
        /// <param name="token">Cancellation token</param>
        /// <param name="lastLicenseCheckTimestamp">Timestamp of the last license check</param>
        /// <param name="lastLicenseCheckResult">Result of the last license check</param>
        /// <exception cref="SimulatorIntegrationSequenceException">Thrown when one or more sequences
        /// rows could not be updated. The exception contains the list of errors</exception>
        public static async Task UpdateSimulatorIntegrationsData(
            this SequencesResource sequences,
            string sequenceExternalId,
            bool init,
            SimulatorIntegrationUpdate update,
            CancellationToken token,
            long lastLicenseCheckTimestamp,
            string lastLicenseCheckResult)
        {
            var rowsToCreate = new List<SequenceDataCreate>();
            var rowData = new Dictionary<string, string>();

            rowData.Add(SimulatorIntegrationSequenceRows.Heartbeat, $"{DateTime.UtcNow.ToUnixTimeMilliseconds()}");
            rowData.Add(SimulatorIntegrationSequenceRows.LicenseTimestamp, $"{lastLicenseCheckTimestamp}");
            rowData.Add(SimulatorIntegrationSequenceRows.LicenseStatus, lastLicenseCheckResult);

            if (init)
            {
                if (update == null)
                {
                    throw new ArgumentNullException(nameof(update));
                }
                // Data set and version could only have changed on connector restart
                if (update.DataSetId.HasValue)
                {
                    rowData.Add(SimulatorIntegrationSequenceRows.DataSetId, $"{update.DataSetId.Value}");
                }
                rowData.Add(SimulatorIntegrationSequenceRows.ConnectorVersion, $"{update.ConnectorVersion}");
                rowData.Add(SimulatorIntegrationSequenceRows.SimulatorVersion, $"{update.SimulatorVersion}");
                rowData.Add(SimulatorIntegrationSequenceRows.SimulatorsApiEnabled, $"{update.SimulatorApiEnabled}");
                if (update.ExtraInformation != null && update.ExtraInformation.Any())
                {
                    update.ExtraInformation.ToList().ForEach(i => rowData.Add(i.Key, i.Value));
                }
            }
            
            var rowCreate = ToSequenceData(rowData, sequenceExternalId, 0);
            rowsToCreate.Add(rowCreate);
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
            long rowStart,
            long? dataSetId,
            SimulationTabularResults results,
            CancellationToken token)
        {
            if (results == null || results.Columns == null || !results.Columns.Any())
            {
                return null;
            }

            var rowCount = results.MaxNumOfRows();
            // Verify that all results have the same number of rows
            if (results.Columns.Where(c => c.Value.NumOfRows() != rowCount).Any())
            {
                throw new SimulationTabularResultsException(
                    "All simulation result columns should contain the same number of rows");
            }

            if (string.IsNullOrEmpty(externalId))
            {
                rowStart = 0;
                var calcTypeForId = results.Calculation.GetCalcTypeForIds();
                var modelNameForId = results.Calculation.Model.Name.ReplaceSpecialCharacters("_");
                externalId = $"{results.Calculation.Model.Simulator}-OUTPUT-{calcTypeForId}-{results.Type}-{modelNameForId}-{DateTime.UtcNow.ToUnixTimeMilliseconds()}";
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
                keyChunkSize: 100,
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

        /// <summary>
        /// Store the simulation run configuration.
        /// The sequence rows contains be key/value pair. A pair can be a calculation configuration property that
        /// may change from run to run
        /// </summary>
        /// <param name="sequences">CDF Sequence resource</param>
        /// <param name="externalId">External id of the sequence. Set to <c>null</c> to generate a new external id</param>
        /// <param name="rowStart">Write rows starting from this index</param>
        /// <param name="dataSetId">Data set id</param>
        /// <param name="calculation">Calculation object</param>
        /// <param name="runConfiguration">Dictionary containing the key/value pairs to add</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>The sequence containing the run configuration</returns>
        /// <exception cref="SimulationRunConfigurationException">Thrown when it is not possible to store the run  configuration</exception>
        public static async Task<Sequence> StoreRunConfiguration(
            this SequencesResource sequences,
            string externalId,
            long rowStart,
            long? dataSetId,
            SimulatorCalculation calculation,
            Dictionary<string,string> runConfiguration,
            CancellationToken token)
        {
            if (calculation == null || runConfiguration == null || !runConfiguration.Any())
            {
                return null;
            }

            if (string.IsNullOrEmpty(externalId))
            {
                var calcTypeForId = calculation.GetCalcTypeForIds();
                var modelNameForId = calculation.Model.Name.ReplaceSpecialCharacters("_");
                externalId = $"{calculation.Model.Simulator}-RC-{calcTypeForId}-{modelNameForId}-{DateTime.UtcNow.ToUnixTimeMilliseconds()}";
                rowStart = 0;
            }

            var sequenceCreate = BuildRunConfigurationSequence(
                externalId,
                dataSetId,
                calculation);
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
                throw new SimulationRunConfigurationException(
                    "Could not find or create simulation run configuration sequence", createdSequences.Errors);
            }

            var rows = new List<SequenceDataCreate>
            {
                ToSequenceData(runConfiguration, externalId, rowStart)
            };

            var result = await sequences.InsertAsync(
                rows,
                keyChunkSize: 100,
                valueChunkSize: 100,
                sequencesChunk: 10,
                throttleSize: 1,
                RetryMode.None,
                SanitationMode.None,
                token).ConfigureAwait(false);
            if (!result.IsAllGood)
            {
                throw new SimulationRunConfigurationException($"Could not save simulation run configuration", result.Errors);
            }
            return createdSequences.Results.First();
        }

        private static SequenceCreate BuildRunConfigurationSequence(
            string externalId,
            long? dataSet,
            SimulatorCalculation calculation)
        {
            var sequenceCreate = GetSequenceCreatePrototype(
                externalId,
                calculation,
                SimulatorDataType.SimulationRunConfiguration,
                dataSet);

            sequenceCreate.Name = $"Run Configuration - {calculation.Name.ReplaceSlashAndBackslash("_")} - {calculation.Model.Name.ReplaceSlashAndBackslash("_")}";
            sequenceCreate.Description = $"Simulation run configuration details for {calculation.Name} - {calculation.Model.Name}";
            sequenceCreate.Columns = GetKeyValueColumnWrite();

            return sequenceCreate;
        }

        private static IEnumerable<SequenceColumnWrite> GetKeyValueColumnWrite()
        {
            return new List<SequenceColumnWrite> {
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
            };
        }

        private static SequenceDataCreate ToSequenceData(this Dictionary<string, string> rows, string sequenceId, long startIndex)
        {
            var sequenceRows = new List<SequenceRow>();
            var index = startIndex;
            foreach (var kvp in rows)
            {
                var sequenceRow = new SequenceRow
                {
                    RowNumber = index,
                    Values = new List<MultiValue> {
                        MultiValue.Create(kvp.Key),
                        MultiValue.Create(kvp.Value)
                    }
                };
                sequenceRows.Add(sequenceRow);
                index++;
            }
            SequenceDataCreate sequenceData = new SequenceDataCreate
            {
                ExternalId = sequenceId,
                Columns = new List<string> {
                            KeyValuePairSequenceColumns.Key,
                            KeyValuePairSequenceColumns.Value
                        },
                Rows = sequenceRows
            };
            return sequenceData;
        }

        private static SequenceCreate BuildResultsSequence(
            string externalId,
            long? dataSet,
            SimulationTabularResults results)
        {
            var sequenceCreate = GetSequenceCreatePrototype(
                externalId,
                results.Calculation,
                SimulatorDataType.SimulationOutput,
                dataSet);

            sequenceCreate.Name = $"{results.Name.ReplaceSlashAndBackslash("_")} " +
                $"- {results.Calculation.Name.ReplaceSlashAndBackslash("_")} - {results.Calculation.Model.Name.ReplaceSlashAndBackslash("_")}";
            sequenceCreate.Description = $"Calculation result for {results.Calculation.Name} - {results.Calculation.Model.Name}";
            sequenceCreate.Metadata.Add(CalculationMetadata.ResultTypeKey, results.Type);
            sequenceCreate.Metadata.Add(CalculationMetadata.ResultNameKey, results.Name);
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

        private static SequenceCreate GetSequenceCreatePrototype(
            string externalId,
            SimulatorCalculation calculation,
            SimulatorDataType dataType,
            long? dataSet)
        {
            var seqCreate = new SequenceCreate
            {
                ExternalId = externalId,
                Metadata = calculation.GetCommonMetadata(dataType)
            };
            if (dataSet.HasValue)
            {
                seqCreate.DataSetId = dataSet.Value;
            }
            return seqCreate;
        }

        /// <summary>
        /// Read the values of a <see cref="SequenceRow"/> and returns
        /// it as a string array
        /// </summary>
        /// <param name="row">CDF Sequence row</param>
        /// <returns>Array containing the row values</returns>
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

        /// <summary>
        /// Simply upserts a key value pair into a key value pair based sequence
        /// </summary>
        /// <param name="sequences">CDF Sequence resource</param>
        /// <param name="sequenceExternalId">External id of the sequence.</param>
        /// <param name="updateKey">Key in sequence to update</param>
        /// <param name="updateValue">Value to update the Key value pair to</param>
        /// <param name="token">The cancellation token</param>
        /// <returns>Nothing</returns>
        public static async 
        Task UpsertItemInKVPSequence(this SequencesResource sequences, string sequenceExternalId, string updateKey, string updateValue, CancellationToken token) {
            if (sequenceExternalId == "") {
                throw new Exception("External Id is required to upsert a sequence");
            }
            SequenceRowQuery query = new SequenceRowQuery();
            query.ExternalId = sequenceExternalId;
            var sequenceList = await sequences.ListRowsAsync(query, token).ConfigureAwait(false);;
            var rows = sequenceList.Rows;
            var columns = sequenceList.Columns;
            var newRows = rows.Select(r => new SequenceRow
                {
                    RowNumber = r.RowNumber,
                    Values = r.Values
                }).ToList();
            // Checking to see if the key alread exists
            var foundIndex = newRows.FindIndex(0, newRows.Count, (item => item.Values.FirstOrDefault().ToString().Contains(updateKey) ));
            if (foundIndex == -1){
                newRows.Insert(newRows.Count , new SequenceRow{
                    RowNumber = newRows.Count,
                    Values = new List<MultiValue> {
                        MultiValue.Create(updateKey),
                        MultiValue.Create(updateValue)
                    }
                });
            } else {
                newRows[foundIndex].Values = new List<MultiValue> {
                    MultiValue.Create(updateKey),
                    MultiValue.Create(updateValue)
                };
            }

            SequenceDataCreate sequenceData = new SequenceDataCreate
            {
                ExternalId = sequenceExternalId,
                Columns = new List<string> {
                            KeyValuePairSequenceColumns.Key,
                            KeyValuePairSequenceColumns.Value
                        },
                Rows = newRows
            };
            var rowsToCreate = new List<SequenceDataCreate>();
            rowsToCreate.Add(sequenceData);
            var result = await sequences.InsertAsync(
                rowsToCreate,
                keyChunkSize: 10,
                valueChunkSize: 100,
                sequencesChunk: 10,
                throttleSize: 1,
                RetryMode.None,
                SanitationMode.None,
                token
            ).ConfigureAwait(false);
                
            if (!result.IsAllGood)
            {
                Console.WriteLine("Unable to update integration sequence");
                // print all result.errors
                foreach (var error in result.Errors)
                {
                    Console.WriteLine(error.Message);
                }

                throw new SimulatorIntegrationSequenceException("Could not update simulator integration sequence", result.Errors);
            }
        }

        /// <summary>
        /// Gets the externalId of a SimulatorIntegrations sequence (This function can throw an exception)
        /// </summary>
        /// <param name="sequences">CDF Sequence resource</param>
        /// <param name="simulatorName">The simulator name.</param>
        /// <param name="simulatorDataSetId">The simulator datasetId</param>
        /// <param name="connectorName">The connector Name</param>
        /// <param name="token">The cancellation token</param>
        /// <returns>externalId string</returns>
        public static async Task<string> GetSequenceExternalId(this SequencesResource sequences, string simulatorName, long simulatorDataSetId, string connectorName, CancellationToken token) {
            var simulatorSequenceIds = new Dictionary<string, string>();
            var simulatorsDict = new SimulatorIntegration
                {
                    Simulator = simulatorName,
                    DataSetId = simulatorDataSetId,
                    ConnectorName = connectorName
                };
            
            var integrations = await sequences.GetOrCreateSimulatorIntegrations(
                    new List<SimulatorIntegration> { simulatorsDict },
                    token).ConfigureAwait(false);
            foreach (var integration in integrations)
            {
                simulatorSequenceIds.Add(
                    integration.Metadata[BaseMetadata.SimulatorKey],
                    integration.ExternalId);
            }
            return simulatorSequenceIds[simulatorName];                
        }
    }

    /// <summary>
    /// Represent errors related to read/write simulator integration sequences in CDF
    /// </summary>
    public class SimulatorIntegrationSequenceException : CogniteException
    {
        /// <summary>
        /// Create a new exception containing the provided <paramref name="errors"/> and <paramref name="message"/>
        /// </summary>
        public SimulatorIntegrationSequenceException(string message, IEnumerable<CogniteError> errors)
            : base(message, errors)
        {
        }
    }

    /// <summary>
    /// Represent errors related to read/write simulation run configuration sequences in CDF
    /// </summary>
    public class SimulationRunConfigurationException : CogniteException
    {
        /// <summary>
        /// Create a new exception containing the provided <paramref name="errors"/> and <paramref name="message"/>
        /// </summary>
        public SimulationRunConfigurationException(string message, IEnumerable<CogniteError> errors)
            : base(message, errors)
        {
        }
    }

    /// <summary>
    /// Represents errors related to read/write tabular simulation results
    /// </summary>
    public class SimulationTabularResultsException : CogniteException
    {

        /// <summary>
        /// Create a new exception containing the provided <paramref name="errors"/> and <paramref name="message"/>
        /// </summary>
        public SimulationTabularResultsException(string message, IEnumerable<CogniteError> errors)
            : base(message, errors)
        {
        }

        /// <summary>
        /// Creates a new exception containing the provided <paramref name="message"/>
        /// </summary>
        /// <param name="message"></param>
        public SimulationTabularResultsException(string message) : base(message, new List<CogniteError>())
        {
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
