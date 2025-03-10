
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cognite.Extractor.Utils;
using Cognite.Simulator.Utils.Automation;
using CogniteSdk.Alpha;
using Microsoft.Extensions.Logging;

namespace Cognite.Simulator.Utils
{
    public class DefaultConnector<TAutomationConfig,TModelState,TModelStateBasePoco> : ConnectorBase<DefaultConfig<TAutomationConfig>>  
        where TAutomationConfig : AutomationConfig, new()
         where TModelState: ModelStateBase, new()
         where TModelStateBasePoco: ModelStateBasePoco
 
    {
        private readonly DefaultConfig<TAutomationConfig> _config;
        private readonly ILogger<DefaultConnector<TAutomationConfig,TModelState,TModelStateBasePoco>> _logger;
        private readonly DefaultModelLibrary<TAutomationConfig,TModelState,TModelStateBasePoco> _modelLibrary;
        private readonly DefaultRoutineLibrary<TAutomationConfig> _routineLibrary;
        private readonly DefaultSimulationRunner<TAutomationConfig,TModelState,TModelStateBasePoco> _simulationRunner;
        private readonly DefaultSimulationScheduler<TAutomationConfig> _scheduler;
        private readonly ExtractionPipeline _pipeline;
        private readonly string _version;
        private readonly ScopedRemoteApiSink _sink;

        private ISimulatorClient<TModelState, SimulatorRoutineRevision> _simulatorClient;

        public DefaultConnector(
            CogniteDestination cdf,
            DefaultConfig<TAutomationConfig> config,
            DefaultModelLibrary<TAutomationConfig,TModelState,TModelStateBasePoco> modelLibrary,
            DefaultRoutineLibrary<TAutomationConfig> routineLibrary,
            DefaultSimulationRunner<TAutomationConfig,TModelState,TModelStateBasePoco> runner,
            DefaultSimulationScheduler<TAutomationConfig> scheduler,
            ExtractionPipeline pipeline,
            SimulatorCreate simulatorDefinition,
            ILogger<DefaultConnector<TAutomationConfig,TModelState,TModelStateBasePoco>> logger,
            RemoteConfigManager<DefaultConfig<TAutomationConfig>> remoteConfigManager,
            ISimulatorClient<TModelState, SimulatorRoutineRevision> simulatorClient,
            ScopedRemoteApiSink sink)
            : base(cdf, config.Connector, simulatorDefinition, logger, remoteConfigManager, sink)
        {
            _config = config;
            _logger = logger;
            _sink = sink;
            _modelLibrary = modelLibrary;
            _routineLibrary = routineLibrary;
            _simulationRunner = runner;
            _scheduler = scheduler;
            _pipeline = pipeline;
            _simulatorClient = simulatorClient;

        
            _version = Extractor.Metrics.Version.GetVersion(
                    Assembly.GetExecutingAssembly(),
                    "0.0.1");
        }

        public override string GetConnectorVersion(CancellationToken token)
        {
            return _simulatorClient.GetConnectorVersion(token);
        }

        public override string GetSimulatorVersion(string simulator, CancellationToken token)
        {
            return _simulatorClient.GetSimulatorVersion(token);
        }

        public override async Task Init(CancellationToken token)
        {
            await _simulatorClient.TestConnection(token).ConfigureAwait(false);
            await _pipeline.Init(_config.Connector, token).ConfigureAwait(false);
            await InitRemoteSimulatorIntegration(token).ConfigureAwait(false);
            if(RemoteSimulatorIntegration != null){
                _sink.SetDefaultLogId(RemoteSimulatorIntegration.LogId);
            }
            await UpdateRemoteSimulatorIntegration(true, token).ConfigureAwait(false);
            await _modelLibrary.Init(token).ConfigureAwait(false);
            await _routineLibrary.Init(token).ConfigureAwait(false);
        }

        private Task RunAllTasks(CancellationTokenSource linkedTokenSource) {
            var linkedToken = linkedTokenSource.Token;
            var modelLibTasks = _modelLibrary.GetRunTasks(linkedToken);
            var routineLibTasks = _routineLibrary.GetRunTasks(linkedToken);
            var taskList = new List<Task> { HeartbeatLoop(linkedToken) };
            taskList.AddRange(modelLibTasks);
            taskList.AddRange(routineLibTasks);
            taskList.Add(_simulationRunner.Run(linkedToken));
            taskList.Add(_scheduler.Run(linkedToken));
            taskList.Add(_pipeline.PipelineUpdate(linkedToken));
            taskList.Add(RestartOnNewRemoteConfigLoop(linkedToken));
            return taskList.RunAll(linkedTokenSource);
        }

        /// <summary>
        /// Run the main loop of the connector
        /// </summary>
        public override async Task Run(CancellationToken token)
        {
            _logger.LogInformation("Connector started, sending status to CDF every {Interval} seconds",
                _config.Connector.StatusInterval);
                
            try
            {
                using (var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    await RunAllTasks(linkedTokenSource).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Cancellation requested. Exiting the connector");
            }
            catch (AggregateException aggregateException)
            {
                foreach (var innerEx in aggregateException.InnerExceptions)
                {
                    if (innerEx is NewConfigDetected)
                    {
                        // this type of exception needs to be bubbled up 
                        // so we can restart the connector
                        throw innerEx;
                    }
                    _logger.LogError(innerEx, "An error occurred during task execution");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during task execution");
                throw;
            }
        }   
    }
}