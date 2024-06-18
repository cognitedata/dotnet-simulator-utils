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
        private static readonly long Now = DateTime.UtcNow.ToUnixTimeMilliseconds();
        public static string TestSimulatorExternalIdPrefix = "UTILS_TEST_SIMULATOR_";
        public static string TestSimulatorExternalId = TestSimulatorExternalIdPrefix + Now;
        public static string TestIntegrationExternalId = "utils-integration-tests-connector-" + Now;
        public static string TestModelExternalId = "Utils-Connector_Test_Model_" + Now;
        public static string TestRoutineExternalId = "Test Routine with extended IO " + Now;
        public static string TestScheduledRoutineExternalId = "Test Scheduled Routine " + Now;
        public static string TestRoutineExternalIdWithTs = "Test Routine with Input TS and extended IO " + Now;
        public static long TestDataSetId = 386820206154952;

        public static async Task<CogniteSdk.Alpha.Simulator> GetOrCreateSimulator(Client sdk, SimulatorCreate simulator)
        {
            if (sdk == null)
            {
                throw new ArgumentNullException(nameof(sdk));
            }

            var simulators = await sdk.Alpha.Simulators.ListAsync(
                new SimulatorQuery()).ConfigureAwait(false);

            var simulatorRes = simulators.Items.Where(s => s.ExternalId == simulator.ExternalId);
            if (simulatorRes.Count() > 0)
            {
                return simulatorRes.First();
            }

            var res = await sdk.Alpha.Simulators.CreateAsync(
                new List<SimulatorCreate> { simulator }).ConfigureAwait(false);

            return res.First();
        }

        public static async Task DeleteSimulator(Client sdk, string externalId)
        {
            var simulators = await sdk.Alpha.Simulators.ListAsync(
                new SimulatorQuery()).ConfigureAwait(false);

            // delete all test simulators older than 3 minutes and the one with the given externalId
            var createdTime = DateTime.UtcNow.AddMinutes(-3).ToUnixTimeMilliseconds();
            var simulatorRes = simulators.Items.Where(s => 
                (s.ExternalId.StartsWith(TestSimulatorExternalIdPrefix) && s.CreatedTime < createdTime) || s.ExternalId == externalId
            );
            if (simulatorRes.Count() > 0)
            {
                foreach (var sim in simulatorRes)
                {
                    await sdk.Alpha.Simulators.DeleteAsync(new List<Identity>
                    {
                        new Identity(sim.ExternalId)
                    }).ConfigureAwait(false);
                }
            }
        }

        public static async Task DeleteSimulatorModel(Client sdk, string modelExternalId)
        {
            await sdk.Alpha.Simulators.DeleteSimulatorModelsAsync(new List<Identity>
            {
                new Identity(modelExternalId)
            }).ConfigureAwait(false);
        }

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

        public static async Task<CogniteSdk.File> GetOrCreateFile(Client sdk, FileStorageClient fileStorageClient, FileCreate file)
        {
            if (sdk == null)
            {
                throw new Exception("Client is required for file");
            }
            if (fileStorageClient == null)
            {
                throw new Exception("FileStorageClient is required for file");
            }
            if (file == null)
            {
                throw new Exception("File is required for file");
            }

            var filesRes = await sdk.Files.RetrieveAsync(
                new List<string> { file.ExternalId }, true).ConfigureAwait(false);

            if (filesRes.Count() > 0)
            {
                return filesRes.First();
            }

            var res = await sdk.Files.UploadAsync(file).ConfigureAwait(false);

            if (res == null || res.UploadUrl == null)
            {
                throw new Exception("Failed to upload file");
            }

            var uploadUrl = res.UploadUrl;
            var bytes = new byte[1] { 42 };

            using (var fileStream = new StreamContent(new MemoryStream(bytes))) {
                await fileStorageClient.UploadFileAsync(uploadUrl, fileStream).ConfigureAwait(false);
            }

            return res;
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

        public static async Task<SimulatorModelRevision> GetOrCreateSimulatorModelRevisionWithFile(Client sdk, FileStorageClient fileStorageClient, FileCreate file, SimulatorModelRevisionCreate revision)
        {
            var modelFile = await GetOrCreateFile(sdk, fileStorageClient, file).ConfigureAwait(false);
            revision.FileId = modelFile.Id;
            return await GetOrCreateSimulatorModelRevision(sdk, SimulatorModelCreate, revision).ConfigureAwait(false);
        }

        public static async Task<List<SimulatorModelRevision>> GetOrCreateSimulatorModelRevisions(Client sdk, FileStorageClient fileStorageClient) {            
            var rev1 = await GetOrCreateSimulatorModelRevisionWithFile(sdk, fileStorageClient, SimpleModelFileCreate, SimulatorModelRevisionCreateV1).ConfigureAwait(false);
            var rev2 = await GetOrCreateSimulatorModelRevisionWithFile(sdk, fileStorageClient, SimpleModelFileCreate2, SimulatorModelRevisionCreateV2).ConfigureAwait(false);
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

        public static TimeSeriesCreate OnOffValuesTimeSeries = new TimeSeriesCreate()
        {
            ExternalId = "SimConnect-IntegrationTests-OnOffValues",
            Name = "On/Off Values",
            DataSetId = TestDataSetId,
        };

        public static TimeSeriesCreate SsdSensorDataTimeSeries = new TimeSeriesCreate()
        {
            ExternalId = "SimConnect-IntegrationTests-SsdSensorData",
            Name = "SSD Sensor Data",
            DataSetId = TestDataSetId,
        };

        public static async Task<TimeSeries> GetOrCreateTimeSeries(Client sdk, TimeSeriesCreate timeSeries, long[] timestamps, double[] values)
        {
            var timeSeriesRes = await sdk.TimeSeries.RetrieveAsync(
                new List<string>() { timeSeries.ExternalId }, true
            ).ConfigureAwait(false);

            if (timeSeriesRes.Count() > 0)
            {
                return timeSeriesRes.First();
            }

            var res = await sdk.TimeSeries.CreateAsync(
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

            await sdk.DataPoints.CreateAsync(points).ConfigureAwait(false);

            return res.First();
        }


        public static async Task<SimulatorRoutineRevision> GetOrCreateSimulatorRoutineRevision(Client sdk, FileStorageClient fileStorageClient, SimulatorRoutineCreateCommandItem routineToCreate, SimulatorRoutineRevisionCreate revisionToCreate)
        {
            var testValues = new TestValues();
            await GetOrCreateTimeSeries(sdk, OnOffValuesTimeSeries, testValues.TimeLogic, testValues.DataLogic).ConfigureAwait(false);
            await GetOrCreateTimeSeries(sdk, SsdSensorDataTimeSeries, testValues.TimeSsd, testValues.DataSsd).ConfigureAwait(false);
            await GetOrCreateSimulatorModelRevisions(sdk, fileStorageClient).ConfigureAwait(false);
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
                    CronExpression = "*/5 * * * *",
                },
                DataSampling = new SimulatorRoutineRevisionDataSampling()
                {
                    Enabled = true,
                    ValidationWindow = 1440,
                    SamplingWindow = 60,
                    Granularity = 1,
                },
                LogicalCheck = new List<SimulatorRoutineRevisionLogicalCheck>(),
                SteadyStateDetection = new List<SimulatorRoutineRevisionSteadyStateDetection>(),
                Inputs = new List<SimulatorRoutineRevisionInput>(),
                Outputs = new List<SimulatorRoutineRevisionOutput>(),
            },
            ExternalId = $"{TestScheduledRoutineExternalId} - 2",
            RoutineExternalId = $"{TestScheduledRoutineExternalId} - 1",
            Script = new List<SimulatorRoutineRevisionScriptStage>(),
        };

        public static SimulatorRoutineCreateCommandItem SimulatorRoutineCreateScheduled = new SimulatorRoutineCreateCommandItem()
        {
            ExternalId = $"{TestScheduledRoutineExternalId} - 1",
            ModelExternalId = TestModelExternalId,
            SimulatorIntegrationExternalId = TestIntegrationExternalId,
            Name = "Simulation Runner Scheduled Routine",
        };

        public static SimulatorRoutineRevisionCreate SimulatorRoutineRevisionWithExtendedIO = new SimulatorRoutineRevisionCreate()
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

        public static SimulatorRoutineCreateCommandItem SimulatorRoutineCreateWithExtendedIO = new SimulatorRoutineCreateCommandItem()
        {
            ExternalId = SimulatorRoutineRevisionWithExtendedIO.RoutineExternalId,
            ModelExternalId = TestModelExternalId,
            SimulatorIntegrationExternalId = TestIntegrationExternalId,
            Name = "Simulation Runner Test With Extended IO",
        };

        public static SimulatorRoutineRevisionCreate SimulatorRoutineRevisionWithStringsIO = new SimulatorRoutineRevisionCreate()
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

        public static SimulatorRoutineCreateCommandItem SimulatorRoutineCreateWithStringsIO = new SimulatorRoutineCreateCommandItem()
        {
            ExternalId = SimulatorRoutineRevisionWithStringsIO.RoutineExternalId,
            ModelExternalId = TestModelExternalId,
            SimulatorIntegrationExternalId = TestIntegrationExternalId,
            Name = "Simulation Runner Test With Strings IO",
        };

        public static SimulatorRoutineRevisionCreate SimulatorRoutineRevisionWithTsAndExtendedIO = new SimulatorRoutineRevisionCreate()
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

        public static SimulatorRoutineCreateCommandItem SimulatorRoutineCreateWithTsAndExtendedIO = new SimulatorRoutineCreateCommandItem()
        {
            ExternalId = SimulatorRoutineRevisionWithTsAndExtendedIO.RoutineExternalId,
            ModelExternalId = TestModelExternalId,
            SimulatorIntegrationExternalId = TestIntegrationExternalId,
            Name = "Simulation Runner Test With TS and Extended IO",
        };

        public static SimulatorModelCreate SimulatorModelCreate = new SimulatorModelCreate()
        {
            ExternalId = TestModelExternalId,
            Name = "Connector Test Model",
            Description = "PETEX-Connector Test Model",
            DataSetId = TestDataSetId,
            SimulatorExternalId = TestSimulatorExternalId,
            Type = "OilWell",
        };

        public static SimulatorModelRevisionCreate SimulatorModelRevisionCreateV1 = GenerateSimulatorModelRevisionCreate(TestModelExternalId, 1);

        public static SimulatorModelRevisionCreate SimulatorModelRevisionCreateV2 = GenerateSimulatorModelRevisionCreate(TestModelExternalId, 2);

        public static SimulatorModelRevisionCreate GenerateSimulatorModelRevisionCreate(string externalId, int version = 1) {
            return new SimulatorModelRevisionCreate()
            {
                ExternalId = $"{externalId}-{version}",
                ModelExternalId = SimulatorModelCreate.ExternalId,
                Description = "integration test. can be deleted at any time. the test will recreate it.",
            };
        }

        public static SimulatorCreate SimulatorCreate = new SimulatorCreate()
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
    }
}