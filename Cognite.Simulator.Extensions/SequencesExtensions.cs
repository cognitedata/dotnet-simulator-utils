using Cognite.Extensions;
using Cognite.Extractor.Common;
using CogniteSdk;
using CogniteSdk.Resources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cognite.Simulator.Extensions
{
    public static class SequencesExtensions
    {
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
                    metadata.Add(BaseMetadata.DataModelVersionKey, BaseMetadata.DataModelVersion);
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
                throw new SimulatorIntegrationSequenceException("Could not find or create Simulator Integrations sequence", createdSequences.Errors);
            }
            result.AddRange(createdSequences.Results);
            return result;
        }

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
                throw new SimulatorIntegrationSequenceException("Failed to update Simulator Integrations sequence", result.Errors);
            }
        }

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

    public class SimulatorIntegrationSequenceException : Exception
    {
        public IEnumerable<CogniteError> CogniteErrors { get; private set; }

        public SimulatorIntegrationSequenceException(string message, IEnumerable<CogniteError> errors)
            : base(message)
        {
            CogniteErrors = errors;
        }
    }

    public class BoundaryConditionsMapNotFoundException : Exception
    {
        public BoundaryConditionsMapNotFoundException(string message) : base(message)
        {
        }
    }
}
