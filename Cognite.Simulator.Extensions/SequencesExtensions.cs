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

        public static async Task<IEnumerable<Sequence>> GetOrCreateSimulatorIntegration(
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
                        Name = $"{simulator.Key} Simulator Integrations",
                        ExternalId = $"{simulator.Key}-INTEGRATION-{DateTime.UtcNow.ToUnixTimeMilliseconds()}",
                        Description = $"Details about {simulator.Key} integration",
                        DataSetId = simulator.Value,
                        Metadata = metadata,
                        Columns = new List<SequenceColumnWrite> {
                            new SequenceColumnWrite {
                                Name = "Key",
                                ExternalId = "key",
                                ValueType = MultiValueType.STRING,
                            },
                            new SequenceColumnWrite {
                                Name = "Value",
                                ExternalId = "value",
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
                10,
                1,
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
    }

    public class SimulatorIntegrationSequenceException : Exception
    {
        public IEnumerable<CogniteError<SequenceCreate>> CogniteErrors { get; private set; }

        public SimulatorIntegrationSequenceException(string message, IEnumerable<CogniteError<SequenceCreate>> errors)
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
