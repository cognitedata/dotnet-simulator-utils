using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CogniteSdk;
using CogniteSdk.Alpha;

namespace Cognite.Simulator.Tests
{
    public class SeedData
    {

        public static async Task<SimulatorModel> GetOrCreateSimulatorModel(Client sdk, SimulatorModelCreate model)
        {
            var models = await sdk.Alpha.Simulators.ListSimulatorModelsAsync(
                new SimulatorModelQuery
                {
                    Filter = new SimulatorModelFilter
                    {
                        SimulatorExternalIds = new List<string> { model.SimulatorExternalId },
                    },
                }).ConfigureAwait(false);

            var modelRes = models.Items.Where(m => m.ExternalId == model.ExternalId);
            if (modelRes.Count() > 0)
            {
                return modelRes.First();
            }

            var res = await sdk.Alpha.Simulators.CreateSimulatorModelsAsync(
                new List<SimulatorModelCreate>
                {
                    model
                }).ConfigureAwait(false);
            return res.First();
        }

        public static async Task<SimulatorModelRevision> GetOrCreateSimulatorModelRevision(Client sdk, SimulatorModelCreate model, SimulatorModelRevisionCreate revision)
        {
            var modelRes = await GetOrCreateSimulatorModel(sdk, model).ConfigureAwait(false);

            var revisions = await sdk.Alpha.Simulators.ListSimulatorModelRevisionsAsync(
                new SimulatorModelRevisionQuery
                {
                    Filter = new SimulatorModelRevisionFilter
                    {
                        ModelExternalIds = new List<string> { modelRes.ExternalId },
                    },
                }).ConfigureAwait(false);

            var revisionRes = revisions.Items.Where(r => r.ExternalId == revision.ExternalId);
            if (revisionRes.Count() > 0)
            {
                return revisionRes.First();
            }

            var res = await sdk.Alpha.Simulators.CreateSimulatorModelRevisionsAsync(
                new List<SimulatorModelRevisionCreate>
                {
                    revision
                }).ConfigureAwait(false);
            return res.First();
        }

        public static async Task<List<SimulatorModelRevision>> GetOrCreateSimulatorModelRevisions(Client sdk) {
            var rev1 = await GetOrCreateSimulatorModelRevision(sdk, SimulatorModelCreate, SimulatorModelRevisionCreateV1).ConfigureAwait(false);
            var rev2 = await GetOrCreateSimulatorModelRevision(sdk, SimulatorModelCreate, SimulatorModelRevisionCreateV2).ConfigureAwait(false);
            return new List<SimulatorModelRevision> { rev1, rev2 };
        }

        public static async Task<SimulatorRoutine> GetOrCreateSimulatorRoutine(Client sdk, SimulatorRoutineCreateCommandItem routine)
        {
            var routines = await sdk.Alpha.Simulators.ListSimulatorRoutinesAsync(
                new SimulatorRoutineQuery
                {
                    Filter = new SimulatorRoutineFilter
                    {
                        ModelExternalIds = new List<string> { routine.ModelExternalId },
                    },
                }).ConfigureAwait(false);

            var routineRes = routines.Items.Where(r => r.ExternalId == routine.ExternalId);
            if (routineRes.Count() > 0)
            {
                return routineRes.First();
            }

            var res = await sdk.Alpha.Simulators.CreateSimulatorRoutinesAsync(
                new List<SimulatorRoutineCreateCommandItem> { routine }).ConfigureAwait(false);

            return res.First();
        }


        public static async Task<SimulatorRoutineRevision> GetOrCreateSimulatorRoutineRevision(Client sdk, SimulatorRoutineCreateCommandItem routineToCreate, SimulatorRoutineRevisionCreate revisionToCreate)
        {
            await GetOrCreateSimulatorModelRevisions(sdk).ConfigureAwait(false);
            var routine = await GetOrCreateSimulatorRoutine(sdk, routineToCreate).ConfigureAwait(false);

            var routineRevisions = await sdk.Alpha.Simulators.ListSimulatorRoutineRevisionsAsync(
                new SimulatorRoutineRevisionQuery
                {
                    Filter = new SimulatorRoutineRevisionFilter
                    {
                        RoutineExternalIds = new List<string> { routine.ExternalId },
                    },
                }).ConfigureAwait(false);

            var routineRevisionsFiltered = routineRevisions.Items.Where(r => r.ExternalId == revisionToCreate.ExternalId);
            if (routineRevisionsFiltered.Count() > 0)
            {
                return routineRevisionsFiltered.First();
            }

            var revisionRes = await sdk.Alpha.Simulators.CreateSimulatorRoutineRevisionsAsync(
                new List<SimulatorRoutineRevisionCreate>
                {
                    revisionToCreate
                }).ConfigureAwait(false);
            return revisionRes.First();
        }

        public static SimulatorRoutineRevisionCreate SimulatorRoutineRevisionCreateScheduled = new SimulatorRoutineRevisionCreate()
        {
            Configuration = new SimulatorRoutineRevisionConfiguration()
            {
                Schedule = new SimulatorRoutineRevisionSchedule()
                {
                    Enabled = true,
                    StartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 10000,
                    Repeat = "5s",
                },
                DataSampling = new SimulatorRoutineRevisionDataSampling()
                {
                    ValidationWindow = 1440,
                    SamplingWindow = 60,
                    Granularity = 1,
                    ValidationEndOffset = "10m"
                },
                LogicalCheck = new SimulatorRoutineRevisionLogicalCheck()
                {
                    Enabled = false,
                },
                SteadyStateDetection = new SimulatorRoutineRevisionSteadyStateDetection()
                {
                    Enabled = false,
                },
                InputConstants = new List<InputConstants>(),
                InputTimeseries = new List<SimulatorRoutineRevisionInputTimeseries>(),
                OutputTimeseries = new List<SimulatorRoutineRevisionOutputTimeseries>(),
            },
            ExternalId = "Test Scheduled Routine - 2",
            RoutineExternalId = $"Test Scheduled Routine - 1",
            Script = new List<SimulatorRoutineRevisionStage>(),
        };

        public static SimulatorRoutineCreateCommandItem SimulatorRoutineCreateScheduled = new SimulatorRoutineCreateCommandItem()
        {
            ExternalId = "Test Scheduled Routine - 1",
            ModelExternalId = "PROSPER-Connector_Test_Model",
            SimulatorIntegrationExternalId = "scheduler-test-connector",
            Name = "Simulation Runner Scheduled Routine",
        };

        public static SimulatorRoutineRevisionCreate SimulatorRoutineRevisionWithInputConstants = new SimulatorRoutineRevisionCreate()
        {
            Configuration = new SimulatorRoutineRevisionConfiguration()
            {
                Schedule = new SimulatorRoutineRevisionSchedule()
                {
                    Enabled = false,
                },
                DataSampling = new SimulatorRoutineRevisionDataSampling()
                {
                    ValidationWindow = 1440,
                    SamplingWindow = 60,
                    Granularity = 1,
                    ValidationEndOffset = "10m"
                },
                LogicalCheck = new SimulatorRoutineRevisionLogicalCheck()
                {
                    Enabled = true,
                    TimeseriesExternalId = "SimConnect-IntegrationTests-OnOffValues",
                    Aggregate = "stepInterpolation",
                    Operator = "eq",
                    Value = 1,
                },
                SteadyStateDetection = new SimulatorRoutineRevisionSteadyStateDetection()
                {
                    Enabled = true,
                    TimeseriesExternalId = "SimConnect-IntegrationTests-SsdSensorData",
                    Aggregate = "average",
                    MinSectionSize = 60,
                    VarThreshold = 1.0,
                    SlopeThreshold = -3.0,
                },
                // TODO rename type to SimulatorRoutineRevisionInputConstant type
                InputConstants = new List<InputConstants>() {
                    new InputConstants() {
                        SaveTimeseriesExternalId = "SimConnect-IntegrationTests-IC1-SampledSsd",
                        Value = "42",
                        Unit = "STB/MMscf",
                        UnitType = "LiqRate/GasRate",
                        Name = "Input Constant 1",
                        ReferenceId = "IC1",
                    },
                    new InputConstants() {
                        SaveTimeseriesExternalId = "SimConnect-IntegrationTests-IC2-SampledSsd",
                        Value = "100",
                        Unit = "STB/MMscf",
                        UnitType = "LiqRate/GasRate",
                        Name = "Input Constant 2",
                        ReferenceId = "IC2",
                    },
                },
                OutputTimeseries = new List<SimulatorRoutineRevisionOutputTimeseries>() {
                    new SimulatorRoutineRevisionOutputTimeseries() {
                        Name = "Output Test 1",
                        ReferenceId = "OT1",
                        Unit = "STB/MMscf",
                        UnitType = "LiqRate/GasRate",
                        SaveTimeseriesExternalId = "SimConnect-IntegrationTests-OT1-Output",
                    },
                },
                InputTimeseries = new List<SimulatorRoutineRevisionInputTimeseries>(),
            },
            ExternalId = "Test Routine with Input Constants - 1",
            RoutineExternalId = "Test Routine with Input Constants",
            Script = new List<SimulatorRoutineRevisionStage>() {
                new SimulatorRoutineRevisionStage() {
                    Order = 1,
                    Description = "Set simulation inputs",
                    Steps = new List<SimulatorRoutineRevisionScriptStep>() {
                        new SimulatorRoutineRevisionScriptStep() {
                            Order = 1,
                            StepType = "Set",
                            Arguments = new Dictionary<string, string>() {
                                { "argumentType", "inputConstant" },
                                { "referenceId", "IC1" },
                            },
                        },
                        new SimulatorRoutineRevisionScriptStep() {
                            Order = 1,
                            StepType = "Set",
                            Arguments = new Dictionary<string, string>() {
                                { "argumentType", "inputConstant" },
                                { "referenceId", "IC2" },
                            },
                        },
                    },
                },
                new SimulatorRoutineRevisionStage() {
                    Order = 2,
                    Description = "Perform simulation",
                    Steps = new List<SimulatorRoutineRevisionScriptStep>() {
                        new SimulatorRoutineRevisionScriptStep() {
                            Order = 1,
                            StepType = "Command",
                            Arguments = new Dictionary<string, string>() {
                                { "argumentType", "Simulate" },
                            },
                        },
                    },
                },
                new SimulatorRoutineRevisionStage() {
                    Order = 3,
                    Description = "Get output time series",
                    Steps = new List<SimulatorRoutineRevisionScriptStep>() {
                        new SimulatorRoutineRevisionScriptStep() {
                            Order = 1,
                            StepType = "Get",
                            Arguments = new Dictionary<string, string>() {
                                { "argumentType", "outputTimeSeries" },
                                { "referenceId", "OT1" },
                            },
                        },
                    },
                },
            },
        };
        public static SimulatorRoutineCreateCommandItem SimulatorRoutineCreateWithInputConstants = new SimulatorRoutineCreateCommandItem()
        {
            ExternalId = SimulatorRoutineRevisionWithInputConstants.RoutineExternalId,
            ModelExternalId = "PROSPER-Connector_Test_Model",
            SimulatorIntegrationExternalId = "integration-tests-connector",
            Name = "Simulation Runner Test With Constant Inputs",
        };

        public static SimulatorRoutineRevisionCreate SimulatorRoutineRevision = new SimulatorRoutineRevisionCreate()
        {
            Configuration = new SimulatorRoutineRevisionConfiguration()
            {
                Schedule = new SimulatorRoutineRevisionSchedule()
                {
                    Enabled = false,
                },
                DataSampling = new SimulatorRoutineRevisionDataSampling()
                {
                    ValidationWindow = 1440,
                    SamplingWindow = 60,
                    Granularity = 1,
                    ValidationEndOffset = "0s"
                },
                LogicalCheck = new SimulatorRoutineRevisionLogicalCheck()
                {
                    Enabled = true,
                    TimeseriesExternalId = "SimConnect-IntegrationTests-OnOffValues",
                    Aggregate = "stepInterpolation",
                    Operator = "eq",
                    Value = 1,
                },
                SteadyStateDetection = new SimulatorRoutineRevisionSteadyStateDetection()
                {
                    Enabled = true,
                    TimeseriesExternalId = "SimConnect-IntegrationTests-SsdSensorData",
                    Aggregate = "average",
                    MinSectionSize = 60,
                    VarThreshold = 1.0,
                    SlopeThreshold = -3.0,
                },
                InputConstants = new List<InputConstants>(),
                OutputTimeseries = new List<SimulatorRoutineRevisionOutputTimeseries>() {
                    new SimulatorRoutineRevisionOutputTimeseries() {
                        Name = "Output Test 1",
                        ReferenceId = "OT1",
                        Unit = "STB/MMscf",
                        UnitType = "LiqRate/GasRate",
                        SaveTimeseriesExternalId = "SimConnect-IntegrationTests-OT1-Output",
                    },
                },
                InputTimeseries = new List<SimulatorRoutineRevisionInputTimeseries>() {
                    new SimulatorRoutineRevisionInputTimeseries() {
                        Name = "Input Test 1",
                        ReferenceId = "IT1",
                        Unit = "STB/MMscf",
                        UnitType = "LiqRate/GasRate",
                        Aggregate = "average",
                        SaveTimeseriesExternalId = "SimConnect-IntegrationTests-IT1-SampledSsd",
                        SourceExternalId = "SimConnect-IntegrationTests-SsdSensorData",
                    },
                
                },
            },
            ExternalId = "Test Routine with Input Constants - 1",
            RoutineExternalId = "Test Routine with Input Constants",
            Script = new List<SimulatorRoutineRevisionStage>() {
                new SimulatorRoutineRevisionStage() {
                    Order = 1,
                    Description = "Set simulation inputs",
                    Steps = new List<SimulatorRoutineRevisionScriptStep>() {
                        new SimulatorRoutineRevisionScriptStep() {
                            Order = 1,
                            StepType = "Set",
                            Arguments = new Dictionary<string, string>() {
                                { "argumentType", "inputTimeSeries" },
                                { "referenceId", "IT1" },
                            },
                        },
                    },
                },
                new SimulatorRoutineRevisionStage() {
                    Order = 2,
                    Description = "Perform simulation",
                    Steps = new List<SimulatorRoutineRevisionScriptStep>() {
                        new SimulatorRoutineRevisionScriptStep() {
                            Order = 1,
                            StepType = "Command",
                            Arguments = new Dictionary<string, string>() {
                                { "argumentType", "Simulate" },
                            },
                        },
                    },
                },
                new SimulatorRoutineRevisionStage() {
                    Order = 3,
                    Description = "Get output time series",
                    Steps = new List<SimulatorRoutineRevisionScriptStep>() {
                        new SimulatorRoutineRevisionScriptStep() {
                            Order = 1,
                            StepType = "Get",
                            Arguments = new Dictionary<string, string>() {
                                { "argumentType", "outputTimeSeries" },
                                { "referenceId", "OT1" },
                            },
                        },
                    },
                },
            },
        };

        public static SimulatorRoutineCreateCommandItem SimulatorRoutineCreate = new SimulatorRoutineCreateCommandItem()
        {
            ExternalId = SimulatorRoutineRevision.RoutineExternalId,
            ModelExternalId = "PROSPER-Connector_Test_Model",
            SimulatorIntegrationExternalId = "integration-tests-connector",
            Name = "Simulation Runner Test",
        };

        public static SimulatorModelCreate SimulatorModelCreate = new SimulatorModelCreate()
        {
            ExternalId = "PROSPER-Connector_Test_Model",
            Name = "Connector Test Model",
            Description = "PROSPER-Connector Test Model",
            DataSetId = 7900866844615420,
            SimulatorExternalId = "PROSPER",
        };

        public static SimulatorModelRevisionCreate SimulatorModelRevisionCreateV1 = new SimulatorModelRevisionCreate()
        {
            ExternalId = "PROSPER-Connector_Test_Model-1",
            ModelExternalId = SimulatorModelCreate.ExternalId,
            FileId = 2583813271697095,
            Description = "integration test. can be deleted at any time. the test will recreate it.",
        };

        public static SimulatorModelRevisionCreate SimulatorModelRevisionCreateV2 = new SimulatorModelRevisionCreate()
        {
            ExternalId = "PROSPER-Connector_Test_Model-2",
            ModelExternalId = SimulatorModelCreate.ExternalId,
            FileId = 4435244413333137,
            Description = "integration test. can be deleted at any time. the test will recreate it.",
        };
    }
}