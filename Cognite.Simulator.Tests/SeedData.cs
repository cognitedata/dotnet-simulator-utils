using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Linq;
using System.Threading.Tasks;

using CogniteSdk;
using CogniteSdk.Alpha;
using Com.Cognite.V1.Timeseries.Proto;

using Cognite.Simulator.Tests.DataProcessingTests;
using Cognite.Simulator.Utils;
using Cognite.Extractor.Common;

namespace Cognite.Simulator.Tests
{
    public class SeedData
    {
        private long Now;
        public readonly static string TestSimulatorExternalIdPrefix = "UTILS_TEST_SIMULATOR_";
        public readonly string TestSimulatorExternalId;
        public readonly string TestIntegrationExternalId;
        public readonly string TestModelExternalId;
        public readonly string TestRoutineExternalId;
        public readonly string TestScheduledRoutineExternalId;
        public readonly string TestRoutineExternalIdWithTs;
        public readonly string TestRoutineExternalIdWithTsNoDataSampling;
        public static long TestDataSetId = 386820206154952;

        private readonly Client _sdk;
        private readonly FileStorageClient _fileStorageClient;

        public SeedData(Client sdk, FileStorageClient fileStorageClient = null)
        {
            if (sdk == null)
            {
                throw new ArgumentNullException(nameof(sdk));
            }
            _fileStorageClient = fileStorageClient;
            _sdk = sdk;

            Now = DateTime.UtcNow.ToUnixTimeMilliseconds();
            TestSimulatorExternalId = TestSimulatorExternalIdPrefix + Now;
            TestIntegrationExternalId = "utils-integration-tests-connector-" + Now;
            TestModelExternalId = "Utils-Connector_Test_Model_" + Now;
            TestRoutineExternalId = "Test Routine with extended IO " + Now;
            TestScheduledRoutineExternalId = "Test Scheduled Routine " + Now;
            TestRoutineExternalIdWithTs = "Test Routine with Input TS and extended IO " + Now;
            TestRoutineExternalIdWithTsNoDataSampling = "Test Routine with no data sampling " + Now;


            SimulatorCreate = new SimulatorCreate()
            {
                ExternalId = TestSimulatorExternalId,
                Name =  TestSimulatorExternalId,
                FileExtensionTypes= new List<string> { "out" },
                StepFields = new List<SimulatorStepField> {
                    new SimulatorStepField {
                        StepType = "get/set",
                        Fields = new List<SimulatorStepFieldParam> {
                            new SimulatorStepFieldParam {
                                Name = "address",
                                Label = "OpenServer Address",
                                Info = "Enter the address of the PROSPER variable, i.e. PROSPER.ANL. SYS. Pres",
                            },
                        },
                    },
                    new SimulatorStepField {
                        StepType = "command",
                        Fields = new List<SimulatorStepFieldParam> {
                            new SimulatorStepFieldParam {
                                Name = "command",
                                Label = "OpenServer Command",
                                Info = "Enter the PROSPER command",
                            },
                        },
                    },

                },
                UnitQuantities = new List<SimulatorUnitQuantity> {
                    new SimulatorUnitQuantity {
                        Name = "LiqRate/GasRate",
                        Label = "Liquid Gas Rate",
                        Units = new List<SimulatorUnitEntry> {
                            new SimulatorUnitEntry {
                                Label = "STB/MMscf",
                                Name = "STB/MMscf",
                            },
                            new SimulatorUnitEntry {
                                Label = "Sm³/Sm³",
                                Name = "Sm3/Sm3",
                            },
                            new SimulatorUnitEntry {
                                Label = "m³/m³",
                                Name = "m3/m3",
                            },
                            new SimulatorUnitEntry {
                                Label = "m³/m³Vn",
                                Name = "m3/m3Vn",
                            },
                            new SimulatorUnitEntry {
                                Label = "STB/m³Vn",
                                Name = "STB/m3Vn",
                            },
                            new SimulatorUnitEntry {
                                Label = "Sm³/kSm³",
                                Name = "Sm3/kSm3",
                            },
                            new SimulatorUnitEntry {
                                Label = "Sm³/MSm³",
                                Name = "Sm3/MSm3",
                            },
                        },
                    },
                },
                ModelTypes = new List<SimulatorModelType> {
                    new SimulatorModelType {
                        Name = "Oil and Water Well",
                        Key = "OilWell",
                    },
                    new SimulatorModelType {
                        Name = "Dry and Wet Gas Well",
                        Key = "GasWell",
                    },
                    new SimulatorModelType {
                        Name = "Retrograde Condensate Well",
                        Key = "RetrogradeWell",
                    },
                },
            };

            SimulatorRoutineRevisionCreateScheduled = new SimulatorRoutineRevisionCreate()
            {
                Configuration = new SimulatorRoutineRevisionConfiguration()
                {
                    Schedule = new SimulatorRoutineRevisionSchedule()
                    {
                        Enabled = true,
                        CronExpression = "*/5 * * * *",
                    },
                    DataSampling = new SimulatorRoutineRevisionDataSampling()
                    {
                        Enabled = false,
                    },
                    LogicalCheck = new List<SimulatorRoutineRevisionLogicalCheck>(),
                    SteadyStateDetection = new List<SimulatorRoutineRevisionSteadyStateDetection>(),
                    Inputs = new List<SimulatorRoutineRevisionInput>(),
                    Outputs = new List<SimulatorRoutineRevisionOutput>(),
                },
                ExternalId = $"{TestScheduledRoutineExternalId} - 2",
                RoutineExternalId = $"{TestScheduledRoutineExternalId} - 1",
                Script = new List<SimulatorRoutineRevisionScriptStage>() {
                    new SimulatorRoutineRevisionScriptStage() {
                        Order = 1,
                        Description = "Perform simulation",
                        Steps = new List<SimulatorRoutineRevisionScriptStep>() {
                            new SimulatorRoutineRevisionScriptStep() {
                                Order = 1,
                                StepType = "Command",
                                Arguments = new Dictionary<string, string>() {
                                    { "command", "Simulate" },
                                },
                            },
                        },
                    },
                }
            };
            SimulatorRoutineCreateScheduled = new SimulatorRoutineCreateCommandItem()
            {
                ExternalId = SimulatorRoutineRevisionCreateScheduled.RoutineExternalId,
                ModelExternalId = TestModelExternalId,
                SimulatorIntegrationExternalId = TestIntegrationExternalId,
                Name = "Simulation Runner Scheduled Routine",
            };

           
            SimulatorRoutineRevisionWithExtendedIO = new SimulatorRoutineRevisionCreate()
            {
                Configuration = new SimulatorRoutineRevisionConfiguration()
                {
                    Schedule = new SimulatorRoutineRevisionSchedule()
                    {
                        Enabled = false,
                    },
                    DataSampling = new SimulatorRoutineRevisionDataSampling()
                    {
                        Enabled = true,
                        ValidationWindow = 1440,
                        SamplingWindow = 60,
                        Granularity = 1,
                    },
                    LogicalCheck = new List<SimulatorRoutineRevisionLogicalCheck>()
                    {
                        new SimulatorRoutineRevisionLogicalCheck
                        {
                            Enabled = true,
                            TimeseriesExternalId = "SimConnect-IntegrationTests-OnOffValues",
                            Aggregate = "stepInterpolation",
                            Operator = "eq",
                            Value = 1
                        }
                    },
                    SteadyStateDetection = new List<SimulatorRoutineRevisionSteadyStateDetection>()
                    {
                        new SimulatorRoutineRevisionSteadyStateDetection
                        {
                            Enabled = true,
                            TimeseriesExternalId = "SimConnect-IntegrationTests-SsdSensorData",
                            Aggregate = "average",
                            MinSectionSize = 60,
                            VarThreshold = 1.0,
                            SlopeThreshold = -3.0,
                        }
                    },
                    Inputs = new List<SimulatorRoutineRevisionInput>() {
                        new SimulatorRoutineRevisionInput() {
                            Name = "Input Constant 1",
                            ReferenceId = "IC1",
                            ValueType = SimulatorValueType.DOUBLE,
                            Value = SimulatorValue.Create(42),
                            Unit = new SimulatorValueUnit() {
                                Name = "STB/MMscf",
                                Quantity = "LiqRate/GasRate",
                            },
                            SaveTimeseriesExternalId = "SimConnect-IntegrationTests-IC1-SampledSsd",
                        },
                        new SimulatorRoutineRevisionInput() {
                            Name = "Input Constant 2",
                            ReferenceId = "IC2",
                            ValueType = SimulatorValueType.DOUBLE,
                            Value = SimulatorValue.Create(100),
                            Unit = new SimulatorValueUnit() {
                                Name = "STB/MMscf",
                                Quantity = "LiqRate/GasRate",
                            },
                            SaveTimeseriesExternalId = "SimConnect-IntegrationTests-IC2-SampledSsd",
                        },
                    },
                    Outputs = new List<SimulatorRoutineRevisionOutput>() {
                        new SimulatorRoutineRevisionOutput() {
                            Name = "Output Test 1",
                            ReferenceId = "OT1",
                            ValueType = SimulatorValueType.DOUBLE,
                            Unit = new SimulatorValueUnit() {
                                Name = "STB/MMscf",
                                Quantity = "LiqRate/GasRate",
                            },
                            SaveTimeseriesExternalId = "SimConnect-IntegrationTests-OT1-Output",
                        },
                    },
                },
                ExternalId = $"{TestRoutineExternalId} - 1",
                RoutineExternalId = TestRoutineExternalId,
                Script = new List<SimulatorRoutineRevisionScriptStage>() {
                    new SimulatorRoutineRevisionScriptStage() {
                        Order = 1,
                        Description = "Set simulation inputs",
                        Steps = new List<SimulatorRoutineRevisionScriptStep>() {
                            new SimulatorRoutineRevisionScriptStep() {
                                Order = 1,
                                StepType = "Set",
                                Arguments = new Dictionary<string, string>() {
                                    { "referenceId", "IC1" },
                                    { "address", "42" },
                                },
                            },
                            new SimulatorRoutineRevisionScriptStep() {
                                Order = 1,
                                StepType = "Set",
                                Arguments = new Dictionary<string, string>() {
                                    { "referenceId", "IC2" },
                                    { "address", "42" },
                                },
                            },
                        },
                    },
                    new SimulatorRoutineRevisionScriptStage() {
                        Order = 2,
                        Description = "Perform simulation",
                        Steps = new List<SimulatorRoutineRevisionScriptStep>() {
                            new SimulatorRoutineRevisionScriptStep() {
                                Order = 1,
                                StepType = "Command",
                                Arguments = new Dictionary<string, string>() {
                                    { "command", "Simulate" },
                                },
                            },
                        },
                    },
                    new SimulatorRoutineRevisionScriptStage() {
                        Order = 3,
                        Description = "Get output time series",
                        Steps = new List<SimulatorRoutineRevisionScriptStep>() {
                            new SimulatorRoutineRevisionScriptStep() {
                                Order = 1,
                                StepType = "Get",
                                Arguments = new Dictionary<string, string>() {
                                    { "referenceId", "OT1" },
                                    { "address", "42" },
                                },
                            },
                        },
                    },
                },
            };
            SimulatorRoutineCreateWithExtendedIO = new SimulatorRoutineCreateCommandItem()
            {
                ExternalId = SimulatorRoutineRevisionWithExtendedIO.RoutineExternalId,
                ModelExternalId = TestModelExternalId,
                SimulatorIntegrationExternalId = TestIntegrationExternalId,
                Name = "Simulation Runner Test With Extended IO",
            };

            SimulatorRoutineRevisionWithStringsIO = new SimulatorRoutineRevisionCreate()
            {
                Configuration = new SimulatorRoutineRevisionConfiguration()
                {
                    Schedule = new SimulatorRoutineRevisionSchedule()
                    {
                        Enabled = false,
                    },
                    DataSampling = new SimulatorRoutineRevisionDataSampling()
                    {
                        Enabled = true,
                        ValidationWindow = null,
                        SamplingWindow = 60,
                        Granularity = 1,
                    },
                    LogicalCheck = new List<SimulatorRoutineRevisionLogicalCheck>(),
                    SteadyStateDetection = new List<SimulatorRoutineRevisionSteadyStateDetection>(),
                    Inputs = new List<SimulatorRoutineRevisionInput>() {
                        new SimulatorRoutineRevisionInput() {
                            Name = "Input Constant 1",
                            ReferenceId = "IC1",
                            ValueType = SimulatorValueType.STRING,
                            Value = SimulatorValue.Create("40"),
                        },
                        new SimulatorRoutineRevisionInput() {
                            Name = "Input Constant 2",
                            ReferenceId = "IC2",
                            ValueType = SimulatorValueType.STRING,
                            Value = SimulatorValue.Create("2"),
                        },
                    },
                    Outputs = new List<SimulatorRoutineRevisionOutput>() {
                        new SimulatorRoutineRevisionOutput() {
                            Name = "Output Test 1",
                            ReferenceId = "OT1",
                            ValueType = SimulatorValueType.STRING,
                        },
                    },
                },
                ExternalId = $"{TestRoutineExternalId} - 1",
                RoutineExternalId = TestRoutineExternalId,
                Script = new List<SimulatorRoutineRevisionScriptStage>() {
                    new SimulatorRoutineRevisionScriptStage() {
                        Order = 1,
                        Description = "Set simulation inputs",
                        Steps = new List<SimulatorRoutineRevisionScriptStep>() {
                            new SimulatorRoutineRevisionScriptStep() {
                                Order = 1,
                                StepType = "Set",
                                Arguments = new Dictionary<string, string>() {
                                    { "referenceId", "IC1" },
                                    { "address", "42" },
                                },
                            },
                            new SimulatorRoutineRevisionScriptStep() {
                                Order = 1,
                                StepType = "Set",
                                Arguments = new Dictionary<string, string>() {
                                    { "referenceId", "IC2" },
                                    { "address", "42" },
                                },
                            },
                        },
                    },
                    new SimulatorRoutineRevisionScriptStage() {
                        Order = 2,
                        Description = "Perform simulation",
                        Steps = new List<SimulatorRoutineRevisionScriptStep>() {
                            new SimulatorRoutineRevisionScriptStep() {
                                Order = 1,
                                StepType = "Command",
                                Arguments = new Dictionary<string, string>() {
                                    { "command", "Simulate" },
                                },
                            },
                        },
                    },
                    new SimulatorRoutineRevisionScriptStage() {
                        Order = 3,
                        Description = "Get output time series",
                        Steps = new List<SimulatorRoutineRevisionScriptStep>() {
                            new SimulatorRoutineRevisionScriptStep() {
                                Order = 1,
                                StepType = "Get",
                                Arguments = new Dictionary<string, string>() {
                                    { "referenceId", "OT1" },
                                    { "address", "42" },
                                },
                            },
                        },
                    },
                },
            };
            SimulatorRoutineCreateWithStringsIO = new SimulatorRoutineCreateCommandItem()
            {
                ExternalId = SimulatorRoutineRevisionWithStringsIO.RoutineExternalId,
                ModelExternalId = TestModelExternalId,
                SimulatorIntegrationExternalId = TestIntegrationExternalId,
                Name = "Simulation Runner Test With Strings IO",
            };


            SimulatorRoutineRevisionWithTsAndExtendedIO = new SimulatorRoutineRevisionCreate()
            {
                Configuration = new SimulatorRoutineRevisionConfiguration()
                {
                    Schedule = new SimulatorRoutineRevisionSchedule()
                    {
                        Enabled = false,
                    },
                    DataSampling = new SimulatorRoutineRevisionDataSampling()
                    {
                        Enabled = true,
                        ValidationWindow = 1440,
                        SamplingWindow = 60,
                        Granularity = 1,
                    },
                    LogicalCheck = new List<SimulatorRoutineRevisionLogicalCheck>()
                    {
                        new SimulatorRoutineRevisionLogicalCheck {
                            Enabled = true,
                            TimeseriesExternalId = "SimConnect-IntegrationTests-OnOffValues",
                            Aggregate = "stepInterpolation",
                            Operator = "eq",
                            Value = 1,
                        }
                    },
                    SteadyStateDetection = new List<SimulatorRoutineRevisionSteadyStateDetection>()
                    {
                        new SimulatorRoutineRevisionSteadyStateDetection 
                        {
                            Enabled = true,
                            TimeseriesExternalId = "SimConnect-IntegrationTests-SsdSensorData",
                            Aggregate = "average",
                            MinSectionSize = 60,
                            VarThreshold = 1.0,
                            SlopeThreshold = -3.0,
                        }
                    },
                    Outputs = new List<SimulatorRoutineRevisionOutput>() {
                        new SimulatorRoutineRevisionOutput() {
                            Name = "Output Test 1",
                            ReferenceId = "OT1",
                            ValueType = SimulatorValueType.DOUBLE,
                            Unit = new SimulatorValueUnit() {
                                Name = "STB/MMscf",
                                Quantity = "LiqRate/GasRate",
                            },
                            SaveTimeseriesExternalId = "SimConnect-IntegrationTests-OT1-Output",
                        },
                        new SimulatorRoutineRevisionOutput() {
                            Name = "Output Test 2",
                            ReferenceId = "OT2",
                            ValueType = SimulatorValueType.DOUBLE
                        },
                    },
                    Inputs = new List<SimulatorRoutineRevisionInput>() {
                        new SimulatorRoutineRevisionInput() {
                            Name = "Input Test 1",
                            ReferenceId = "IT1",
                            Unit = new SimulatorValueUnit() {
                                Name = "STB/MMscf",
                                Quantity = "LiqRate/GasRate",
                            },
                            Aggregate = "average",
                            SaveTimeseriesExternalId = "SimConnect-IntegrationTests-IT1-SampledSsd",
                            SourceExternalId = "SimConnect-IntegrationTests-SsdSensorData",
                        },
                        new SimulatorRoutineRevisionInput() {
                            Name = "Input Test 2",
                            ReferenceId = "IT2",
                            Aggregate = "average",
                            SourceExternalId = "SimConnect-IntegrationTests-OnOffValues",
                        },
                    },
                },
                ExternalId = $"{TestRoutineExternalIdWithTs} - 1",
                RoutineExternalId = TestRoutineExternalIdWithTs,
                Script = new List<SimulatorRoutineRevisionScriptStage>() {
                    new SimulatorRoutineRevisionScriptStage() {
                        Order = 1,
                        Description = "Set simulation inputs",
                        Steps = new List<SimulatorRoutineRevisionScriptStep>() {
                            new SimulatorRoutineRevisionScriptStep() {
                                Order = 1,
                                StepType = "Set",
                                Arguments = new Dictionary<string, string>() {
                                    { "referenceId", "IT1" },
                                    { "address", "42" },
                                },
                            },
                        },
                    },
                    new SimulatorRoutineRevisionScriptStage() {
                        Order = 2,
                        Description = "Perform simulation",
                        Steps = new List<SimulatorRoutineRevisionScriptStep>() {
                            new SimulatorRoutineRevisionScriptStep() {
                                Order = 1,
                                StepType = "Command",
                                Arguments = new Dictionary<string, string>() {
                                    { "command", "Simulate" },
                                },
                            },
                        },
                    },
                    new SimulatorRoutineRevisionScriptStage() {
                        Order = 3,
                        Description = "Get output time series",
                        Steps = new List<SimulatorRoutineRevisionScriptStep>() {
                            new SimulatorRoutineRevisionScriptStep() {
                                Order = 1,
                                StepType = "Get",
                                Arguments = new Dictionary<string, string>() {
                                    { "referenceId", "OT1" },
                                    { "address", "42" },
                                },
                            },
                            new SimulatorRoutineRevisionScriptStep() {
                                Order = 2,
                                StepType = "Get",
                                Arguments = new Dictionary<string, string>() {
                                    { "referenceId", "OT2" },
                                    { "address", "42" },
                                },
                            },
                        },
                    },
                },
            };

            SimulatorRoutineCreateWithTsAndExtendedIO = new SimulatorRoutineCreateCommandItem()
            {
                ExternalId = SimulatorRoutineRevisionWithTsAndExtendedIO.RoutineExternalId,
                ModelExternalId = TestModelExternalId,
                SimulatorIntegrationExternalId = TestIntegrationExternalId,
                Name = "Simulation Runner Test With TS and Extended IO",
            };

            SimulatorRoutineRevisionWithTsNoDataSampling = new SimulatorRoutineRevisionCreate()
            {
                Configuration = new SimulatorRoutineRevisionConfiguration()
                {
                    Schedule = new SimulatorRoutineRevisionSchedule()
                    {
                        Enabled = false,
                    },
                    DataSampling = new SimulatorRoutineRevisionDataSampling()
                    {
                        Enabled = false,
                    },
                    LogicalCheck = new List<SimulatorRoutineRevisionLogicalCheck>(),
                    SteadyStateDetection = new List<SimulatorRoutineRevisionSteadyStateDetection>(),
                    Outputs = new List<SimulatorRoutineRevisionOutput>() {
                        new SimulatorRoutineRevisionOutput() {
                            Name = "Output Test 1",
                            ReferenceId = "OT1",
                            ValueType = SimulatorValueType.DOUBLE,
                        },
                        new SimulatorRoutineRevisionOutput() {
                            Name = "Output Test 2",
                            ReferenceId = "OT2",
                            ValueType = SimulatorValueType.DOUBLE
                        },
                    },
                    Inputs = new List<SimulatorRoutineRevisionInput>() {
                        new SimulatorRoutineRevisionInput() {
                            Name = "Input Test 1",
                            ReferenceId = "IT1",
                            SourceExternalId = "SimConnect-IntegrationTests-SsdSensorData",
                        },
                    },
                },
                ExternalId = $"{TestRoutineExternalIdWithTsNoDataSampling} - 1",
                RoutineExternalId = TestRoutineExternalIdWithTsNoDataSampling,
                Script = new List<SimulatorRoutineRevisionScriptStage>() {
                    new SimulatorRoutineRevisionScriptStage() {
                        Order = 1,
                        Description = "Set simulation inputs",
                        Steps = new List<SimulatorRoutineRevisionScriptStep>() {
                            new SimulatorRoutineRevisionScriptStep() {
                                Order = 1,
                                StepType = "Set",
                                Arguments = new Dictionary<string, string>() {
                                    { "referenceId", "IT1" },
                                    { "address", "42" },
                                },
                            },
                        },
                    },
                    new SimulatorRoutineRevisionScriptStage() {
                        Order = 2,
                        Description = "Perform simulation",
                        Steps = new List<SimulatorRoutineRevisionScriptStep>() {
                            new SimulatorRoutineRevisionScriptStep() {
                                Order = 1,
                                StepType = "Command",
                                Arguments = new Dictionary<string, string>() {
                                    { "command", "Simulate" },
                                },
                            },
                        },
                    },
                    new SimulatorRoutineRevisionScriptStage() {
                        Order = 3,
                        Description = "Get output time series",
                        Steps = new List<SimulatorRoutineRevisionScriptStep>() {
                            new SimulatorRoutineRevisionScriptStep() {
                                Order = 1,
                                StepType = "Get",
                                Arguments = new Dictionary<string, string>() {
                                    { "referenceId", "OT1" },
                                    { "address", "42" },
                                },
                            }
                        },
                    },
                },
            };

            SimulatorRoutineCreateWithTsNoDataSampling = new SimulatorRoutineCreateCommandItem()
            {
                ExternalId = SimulatorRoutineRevisionWithTsNoDataSampling.RoutineExternalId,
                ModelExternalId = TestModelExternalId,
                SimulatorIntegrationExternalId = TestIntegrationExternalId,
                Name = "Simulation Runner Test with disabled Data Sampling",
            };

            SimulatorModelCreate = new SimulatorModelCreate()
            {
                ExternalId = TestModelExternalId,
                Name = "Connector Test Model",
                Description = "PETEX-Connector Test Model",
                DataSetId = TestDataSetId,
                SimulatorExternalId = TestSimulatorExternalId,
                Type = "OilWell",
            };

            SimulatorModelRevisionCreateV1 = GenerateSimulatorModelRevisionCreate(TestModelExternalId, 1);
            SimulatorModelRevisionCreateV2 = GenerateSimulatorModelRevisionCreate(TestModelExternalId, 2);

            GetOrCreateSimulator(SimulatorCreate).Wait();
            GetOrCreateSimulatorIntegration(TestIntegrationExternalId).Wait();
        }

        private async Task<CogniteSdk.Alpha.Simulator> GetOrCreateSimulator(SimulatorCreate simulator)
        {

            var simulators = await _sdk.Alpha.Simulators.ListAsync(
                new SimulatorQuery()).ConfigureAwait(false);

            var simulatorRes = simulators.Items.Where(s => s.ExternalId == simulator.ExternalId);
            if (simulatorRes.Count() > 0)
            {
                return simulatorRes.First();
            }

            var res = await _sdk.Alpha.Simulators.CreateAsync(
                new List<SimulatorCreate> { simulator }).ConfigureAwait(false);

            return res.First();
        }

        private async Task<SimulatorIntegration> GetOrCreateSimulatorIntegration(string connectorName = "scheduler-test-connector" ) {
            var integrations = await _sdk.Alpha.Simulators.ListSimulatorIntegrationsAsync(
                new SimulatorIntegrationQuery
                {
                    Filter = new SimulatorIntegrationFilter() {
                        simulatorExternalIds = new List<string> { TestSimulatorExternalId },
                    }
                }
            ).ConfigureAwait(false);
            var existing = integrations.Items.FirstOrDefault(i => i.ExternalId == connectorName);
            if (existing == null) {
                var res = await _sdk.Alpha.Simulators.CreateSimulatorIntegrationAsync(
                    new List<SimulatorIntegrationCreate>
                    {
                        new SimulatorIntegrationCreate
                        {
                            ExternalId = connectorName,
                            SimulatorExternalId = TestSimulatorExternalId,
                            DataSetId = TestDataSetId,
                            Heartbeat = DateTime.UtcNow.ToUnixTimeMilliseconds(),
                            ConnectorVersion = "N/A",
                            SimulatorVersion = "N/A",
                        }
                    }
                ).ConfigureAwait(false);
                return res.First();
            } else {
                await _sdk.Alpha.Simulators.UpdateSimulatorIntegrationAsync(
                    new List<SimulatorIntegrationUpdateItem>
                    {
                        new SimulatorIntegrationUpdateItem(existing.Id)
                        {
                            Update = new SimulatorIntegrationUpdate
                            {
                                Heartbeat = new Update<long>(DateTime.UtcNow.ToUnixTimeMilliseconds()),
                            }
                        }
                    }
                ).ConfigureAwait(false);
                return existing;
            }
        }

        public async Task DeleteSimulator()
        {
            var simulators = await _sdk.Alpha.Simulators.ListAsync(
                new SimulatorQuery()).ConfigureAwait(false);

            // delete all test simulators older than 3 minutes and the one with the given externalId
            var createdTime = DateTime.UtcNow.AddMinutes(-3).ToUnixTimeMilliseconds();
            var simulatorRes = simulators.Items.Where(s => 
                (s.ExternalId.StartsWith(TestSimulatorExternalIdPrefix) && s.CreatedTime < createdTime) || s.ExternalId == TestSimulatorExternalId
            );
            if (simulatorRes.Count() > 0)
            {
                foreach (var sim in simulatorRes)
                {
                    await _sdk.Alpha.Simulators.DeleteAsync(new List<Identity>
                    {
                        new Identity(sim.ExternalId)
                    }).ConfigureAwait(false);
                }
            }
        }

        public async Task DeleteSimulatorModel(string modelExternalId)
        {
            await _sdk.Alpha.Simulators.DeleteSimulatorModelsAsync(new List<Identity>
            {
                new Identity(modelExternalId)
            }).ConfigureAwait(false);
        }

        public async Task<SimulatorModel> GetOrCreateSimulatorModel(SimulatorModelCreate model)
        {
            var models = await _sdk.Alpha.Simulators.ListSimulatorModelsAsync(
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

            var res = await _sdk.Alpha.Simulators.CreateSimulatorModelsAsync(
                new List<SimulatorModelCreate>
                {
                    model
                }).ConfigureAwait(false);
            return res.First();
        }

        public static FileCreate SimpleModelFileCreate = new FileCreate() {
            Name = "simutils-tests-model.out",
            ExternalId = "simutils-tests-model-single-byte",
            DataSetId = TestDataSetId,
        };

        public static FileCreate SimpleModelFileCreate2 = new FileCreate() {
            Name = "simutils-tests-model-2.out",
            ExternalId = "simutils-tests-model-single-byte-2",
            DataSetId = TestDataSetId,
        };

        public async Task<CogniteSdk.File> GetOrCreateFile(FileCreate file)
        {
            if (_fileStorageClient == null)
            {
                throw new Exception("FileStorageClient is required for file");
            }
            if (file == null)
            {
                throw new Exception("File is required for file");
            }

            var filesRes = await _sdk.Files.RetrieveAsync(
                new List<string> { file.ExternalId }, true).ConfigureAwait(false);

            if (filesRes.Count() > 0)
            {
                return filesRes.First();
            }

            var res = await _sdk.Files.UploadAsync(file).ConfigureAwait(false);

            if (res == null || res.UploadUrl == null)
            {
                throw new Exception("Failed to upload file");
            }

            var uploadUrl = res.UploadUrl;
            var bytes = new byte[1] { 42 };

            using (var fileStream = new StreamContent(new MemoryStream(bytes))) {
                await _fileStorageClient.UploadFileAsync(uploadUrl, fileStream).ConfigureAwait(false);
            }

            return res;
        }

        public async Task<SimulatorModelRevision> GetOrCreateSimulatorModelRevision(SimulatorModelCreate model, SimulatorModelRevisionCreate revision)
        {
            var modelRes = await GetOrCreateSimulatorModel(model).ConfigureAwait(false);

            var revisions = await _sdk.Alpha.Simulators.ListSimulatorModelRevisionsAsync(
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

            var res = await _sdk.Alpha.Simulators.CreateSimulatorModelRevisionsAsync(
                new List<SimulatorModelRevisionCreate>
                {
                    revision
                }).ConfigureAwait(false);
            return res.First();
        }

        public async Task<SimulatorModelRevision> GetOrCreateSimulatorModelRevisionWithFile(FileCreate file, SimulatorModelRevisionCreate revision)
        {
            var modelFile = await GetOrCreateFile(file).ConfigureAwait(false);
            revision.FileId = modelFile.Id;
            return await GetOrCreateSimulatorModelRevision(SimulatorModelCreate, revision).ConfigureAwait(false);
        }

        public async Task<List<SimulatorModelRevision>> GetOrCreateSimulatorModelRevisions() {            
            var rev1 = await GetOrCreateSimulatorModelRevisionWithFile(SimpleModelFileCreate, SimulatorModelRevisionCreateV1).ConfigureAwait(false);
            var rev2 = await GetOrCreateSimulatorModelRevisionWithFile(SimpleModelFileCreate2, SimulatorModelRevisionCreateV2).ConfigureAwait(false);
            return new List<SimulatorModelRevision> { rev1, rev2 };
        }

        public async Task<SimulatorRoutine> GetOrCreateSimulatorRoutine(SimulatorRoutineCreateCommandItem routine)
        {
            var routines = await _sdk.Alpha.Simulators.ListSimulatorRoutinesAsync(
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

            var res = await _sdk.Alpha.Simulators.CreateSimulatorRoutinesAsync(
                new List<SimulatorRoutineCreateCommandItem> { routine }).ConfigureAwait(false);

            return res.First();
        }

        public TimeSeriesCreate OnOffValuesTimeSeries = new TimeSeriesCreate()
        {
            ExternalId = "SimConnect-IntegrationTests-OnOffValues",
            Name = "On/Off Values",
            DataSetId = TestDataSetId,
        };

        public TimeSeriesCreate SsdSensorDataTimeSeries = new TimeSeriesCreate()
        {
            ExternalId = "SimConnect-IntegrationTests-SsdSensorData",
            Name = "SSD Sensor Data",
            DataSetId = TestDataSetId,
        };

        public async Task<TimeSeries> GetOrCreateTimeSeries(TimeSeriesCreate timeSeries, long[] timestamps, double[] values)
        {
            var timeSeriesRes = await _sdk.TimeSeries.RetrieveAsync(
                new List<string>() { timeSeries.ExternalId }, true
            ).ConfigureAwait(false);

            if (timeSeriesRes.Count() > 0)
            {
                return timeSeriesRes.First();
            }

            var res = await _sdk.TimeSeries.CreateAsync(
                new List<TimeSeriesCreate> { timeSeries }).ConfigureAwait(false);

            var dataPoints = new NumericDatapoints();

            for (int i = 0; i < timestamps.Length; i++)
            {
                dataPoints.Datapoints.Add(new NumericDatapoint
                {
                    Timestamp = timestamps[i],
                    Value = values[i],
                });
            }

            var points = new DataPointInsertionRequest();
            points.Items.Add(new DataPointInsertionItem
            {
                ExternalId = timeSeries.ExternalId,
                NumericDatapoints = dataPoints,
            });

            await _sdk.DataPoints.CreateAsync(points).ConfigureAwait(false);

            return res.First();
        }


        public async Task<SimulatorRoutineRevision> GetOrCreateSimulatorRoutineRevision(SimulatorRoutineCreateCommandItem routineToCreate, SimulatorRoutineRevisionCreate revisionToCreate)
        {
            var testValues = new TestValues();
            await GetOrCreateTimeSeries(OnOffValuesTimeSeries, testValues.TimeLogic, testValues.DataLogic).ConfigureAwait(false);
            await GetOrCreateTimeSeries(SsdSensorDataTimeSeries, testValues.TimeSsd, testValues.DataSsd).ConfigureAwait(false);
            await GetOrCreateSimulatorModelRevisions().ConfigureAwait(false);
            var routine = await GetOrCreateSimulatorRoutine(routineToCreate).ConfigureAwait(false);

            var routineRevisions = await _sdk.Alpha.Simulators.ListSimulatorRoutineRevisionsAsync(
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

            var revisionRes = await _sdk.Alpha.Simulators.CreateSimulatorRoutineRevisionsAsync(
                new List<SimulatorRoutineRevisionCreate>
                {
                    revisionToCreate
                }).ConfigureAwait(false);
            return revisionRes.First();
        }

        public SimulatorRoutineRevisionCreate SimulatorRoutineRevisionCreateScheduled;

        public SimulatorRoutineCreateCommandItem SimulatorRoutineCreateScheduled;

        public SimulatorRoutineRevisionCreate SimulatorRoutineRevisionWithExtendedIO;

        public SimulatorRoutineCreateCommandItem SimulatorRoutineCreateWithExtendedIO;

        public SimulatorRoutineRevisionCreate SimulatorRoutineRevisionWithStringsIO;

        public SimulatorRoutineCreateCommandItem SimulatorRoutineCreateWithStringsIO;

        public SimulatorRoutineRevisionCreate SimulatorRoutineRevisionWithTsAndExtendedIO;

        public SimulatorRoutineCreateCommandItem SimulatorRoutineCreateWithTsAndExtendedIO;

        public SimulatorRoutineRevisionCreate SimulatorRoutineRevisionWithTsNoDataSampling;

        public SimulatorRoutineCreateCommandItem SimulatorRoutineCreateWithTsNoDataSampling;

        public SimulatorModelCreate SimulatorModelCreate;

        public SimulatorModelRevisionCreate SimulatorModelRevisionCreateV1;

        public SimulatorModelRevisionCreate SimulatorModelRevisionCreateV2;

        public SimulatorModelRevisionCreate GenerateSimulatorModelRevisionCreate(string externalId, int version = 1) {
            return new SimulatorModelRevisionCreate()
            {
                ExternalId = $"{externalId}-{version}",
                ModelExternalId = SimulatorModelCreate.ExternalId,
                Description = "integration test. can be deleted at any time. the test will recreate it.",
            };
        }

        public SimulatorCreate SimulatorCreate;
    }
}