using Cognite.Extractor.StateStorage;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Extensions;
using CogniteSdk;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cognite.Simulator.Utils
{
    public abstract class ConfigurationLibraryBase<T, U, V> : FileLibrary<T, U>
        where T : ConfigurationStateBase
        where U : FileStatePoco
        where V : SimulationConfigurationBase
    {
        public Dictionary<string, V> SimulationConfigurations { get; }

        public ConfigurationLibraryBase(
            FileLibraryConfig config, 
            IList<SimulatorConfig> simulators, 
            CogniteDestination cdf, 
            ILogger logger, 
            FileDownloadClient downloadClient, 
            IExtractionStateStore store = null) : 
            base(SimulatorDataType.SimulationConfiguration, config, simulators, cdf, logger, downloadClient, store)
        {
            SimulationConfigurations = new Dictionary<string, V>();
        }

        public V GetSimulationConfiguration(
            string simulator,
            string modelName,
            string calcType,
            string calcTypeUserDefined)
        {
            var calcConfigs = SimulationConfigurations.Values
                .Where(c => c.Simulator == simulator &&
                    c.ModelName == modelName &&
                    c.CalculationType == calcType &&
                    (string.IsNullOrEmpty(calcTypeUserDefined) || c.CalcTypeUserDefined == calcTypeUserDefined));
            if (calcConfigs.Any())
            {
                return calcConfigs.First();
            }
            return null;
        }

        public T GetSimulationConfigurationState(
            string simulator,
            string modelName,
            string calcType,
            string calcTypeUserDefined)
        {
            var calcConfigs = SimulationConfigurations
                .Where(c => c.Value.Simulator == simulator &&
                    c.Value.ModelName == modelName &&
                    c.Value.CalculationType == calcType &&
                    (string.IsNullOrEmpty(calcTypeUserDefined) || c.Value.CalcTypeUserDefined == calcTypeUserDefined));
            if (calcConfigs.Any())
            {
                var id = calcConfigs.First().Key;
                if (State.TryGetValue(id, out var configState))
                {
                    return configState;
                }
            }
            return null;
        }

        protected override void ProcessDownloadedFiles(CancellationToken token)
        {
            Task.Run(() => ReadConfigurations(), token).Wait(token);
        }

        private void ReadConfigurations()
        {
            var files = State.Values
                .Where(f => !string.IsNullOrEmpty(f.FilePath) && !f.Deserialized).ToList();
            foreach (var file in files)
            {
                try
                {
                    var json = JsonConvert.DeserializeObject<V>(
                        System.IO.File.ReadAllText(file.FilePath),
                        new JsonSerializerSettings
                        {
                            ContractResolver = new DefaultContractResolver
                            {
                                NamingStrategy = new CamelCaseNamingStrategy()
                            },
                            Converters = new List<JsonConverter>()
                            {
                                new Newtonsoft.Json.Converters.StringEnumConverter()
                            }
                        });
                    if (!SimulationConfigurations.ContainsKey(file.Id))
                    {
                        SimulationConfigurations.Add(file.Id, json);
                    }
                    else
                    {
                        SimulationConfigurations[file.Id] = json;
                    }
                    file.Deserialized = true;
                }
                catch (Exception e)
                {
                    Logger.LogError("Could not parse simulation configuration for model {ModelName}: {Error}", file.ModelName, e.Message);
                }
            }
        }
    }

    public class SimulationConfigurationBase
    {
        public string Simulator { get; set; }
        public string ModelName { get; set; }
        public string CalculationName { get; set; }
        public string CalculationType { get; set; }
        public string CalcTypeUserDefined { get; set; } = "";
        public string Connector { get; set; }
        public string UserEmail { get; set; } = "";

        public SimulatorCalculation Calculation => new SimulatorCalculation
        {
            Model = new SimulatorModel
            {
                Name = ModelName,
                Simulator = Simulator
            },
            Name = CalculationName,
            Type = CalculationType,
            UserDefinedType = CalcTypeUserDefined
        };
    }

    public class ConfigurationStateBase : FileState
    {
        public bool Deserialized { get; set; }

        public ConfigurationStateBase(string id) : base(id)
        {
        }
        
        /// <summary>
        /// Data type of the file. For simulation configuration files, this is <see cref="SimulatorDataType.SimulationConfiguration"/> 
        /// </summary>
        /// <returns>String representation of <see cref="SimulatorDataType.SimulationConfiguration"/></returns>
        public override string GetDataType()
        {
            return SimulatorDataType.SimulationConfiguration.MetadataValue();
        }

        public override string GetExtension()
        {
            return "json";
        }

        /// <summary>
        /// Initialize this simulation configuration state using a data object from the state store
        /// </summary>
        /// <param name="poco">Data object</param>
        public override void Init(FileStatePoco poco)
        {
            base.Init(poco);
        }
        
        /// <summary>
        /// Get the data object with the simulation configuration state properties to be persisted by
        /// the state store
        /// </summary>
        /// <returns>File data object</returns>
        public override FileStatePoco GetPoco()
        {
            return new ModelStateBasePoco
            {
                Id = Id,
                ModelName = ModelName,
                Source = Source,
                DataSetId = DataSetId,
                FilePath = FilePath,
                CreatedTime = CreatedTime,
                CdfId = CdfId,
            };
        }
    }
}
