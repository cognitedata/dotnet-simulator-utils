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
