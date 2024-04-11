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

        public static FileCreate SimpleModelFileCreate = new FileCreate() {
            Name = "simulator-integration-tests-model",
            ExternalId = "simulator-integration-tests-model",
            DataSetId = 8148496886298377,
        };

        public static FileCreate SimpleModelFileCreate2 = new FileCreate() {
            Name = "simulator-integration-tests-model-2",
            ExternalId = "simulator-integration-tests-model-2",
            DataSetId = 8148496886298377,
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

            using (var fileStream = new StreamContent(new MemoryStream())) {
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

        public static async Task<List<SimulatorModelRevision>> GetOrCreateSimulatorModelRevisions(Client sdk, FileStorageClient fileStorageClient) {
            var modelFile = await GetOrCreateFile(sdk, fileStorageClient, SimpleModelFileCreate).ConfigureAwait(false);
            var modelFile2 = await GetOrCreateFile(sdk, fileStorageClient, SimpleModelFileCreate2).ConfigureAwait(false);

            SimulatorModelRevisionCreateV1.FileId = modelFile.Id;
            SimulatorModelRevisionCreateV2.FileId = modelFile2.Id;

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

        public static TimeSeriesCreate OnOffValuesTimeSeries = new TimeSeriesCreate()
        {
            ExternalId = "SimConnect-IntegrationTests-OnOffValues",
            Name = "On/Off Values",
            DataSetId = 8148496886298377,
        };

        public static TimeSeriesCreate SsdSensorDataTimeSeries = new TimeSeriesCreate()
        {
            ExternalId = "SimConnect-IntegrationTests-SsdSensorData",
            Name = "SSD Sensor Data",
            DataSetId = 8148496886298377,
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
                    CronExpression = "*/5 * * * * *",
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
                InputConstants = new List<SimulatorRoutineRevisionInputConstants>(),
                InputTimeseries = new List<SimulatorRoutineRevisionInputTimeseries>(),
                OutputTimeseries = new List<SimulatorRoutineRevisionOutputTimeseries>(),
            },
            ExternalId = "Test Scheduled Routine - 2",
            RoutineExternalId = $"Test Scheduled Routine - 1",
            Script = new List<SimulatorRoutineRevisionScriptStage>(),
        };

        public static SimulatorRoutineCreateCommandItem SimulatorRoutineCreateScheduled = new SimulatorRoutineCreateCommandItem()
        {
            ExternalId = "Test Scheduled Routine - 1",
            ModelExternalId = "PROSPER-Connector_Test_Model",
            SimulatorIntegrationExternalId = "scheduler-test-connector",
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
                LogicalCheck = new List<SimulatorRoutineRevisionLogicalCheck>() {
                    new SimulatorRoutineRevisionLogicalCheck()
                    {
                        Enabled = true,
                        TimeseriesExternalId = "SimConnect-IntegrationTests-OnOffValues",
                        Aggregate = "stepInterpolation",
                        Operator = "eq",
                        Value = 1,
                    },
                },
                SteadyStateDetection = new List<SimulatorRoutineRevisionSteadyStateDetection>() {
                    new SimulatorRoutineRevisionSteadyStateDetection()
                    {
                        Enabled = true,
                        TimeseriesExternalId = "SimConnect-IntegrationTests-SsdSensorData",
                        Aggregate = "average",
                        MinSectionSize = 60,
                        VarThreshold = 1.0,
                        SlopeThreshold = -3.0,
                    },
                },
                Inputs = new List<SimulatorRoutineRevisionInput>() {
                    new SimulatorRoutineRevisionInput() {
                        Name = "Input Constant 1",
                        ReferenceId = "IC1",
                        ValueType = SimulatorValueType.DOUBLE,
                        Value = SimulatorValue.Create(42),
                        Unit = new SimulatorValueUnit() {
                            Name = "STB/MMscf",
                            Type = "LiqRate/GasRate",
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
                            Type = "LiqRate/GasRate",
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
                            Type = "LiqRate/GasRate",
                        },
                        SaveTimeseriesExternalId = "SimConnect-IntegrationTests-OT1-Output",
                    },
                },
            },
            ExternalId = "Test Routine with extended IO - 1",
            RoutineExternalId = "Test Routine with extended IO",
            Script = new List<SimulatorRoutineRevisionScriptStage>() {
                new SimulatorRoutineRevisionScriptStage() {
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
                new SimulatorRoutineRevisionScriptStage() {
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
                new SimulatorRoutineRevisionScriptStage() {
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

        public static SimulatorRoutineCreateCommandItem SimulatorRoutineCreateWithExtendedIO = new SimulatorRoutineCreateCommandItem()
        {
            ExternalId = SimulatorRoutineRevisionWithExtendedIO.RoutineExternalId,
            ModelExternalId = "PROSPER-Connector_Test_Model",
            SimulatorIntegrationExternalId = "integration-tests-connector",
            Name = "Simulation Runner Test With Extended IO",
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
                LogicalCheck = new List<SimulatorRoutineRevisionLogicalCheck>() {
                    new SimulatorRoutineRevisionLogicalCheck()
                    {
                        Enabled = true,
                        TimeseriesExternalId = "SimConnect-IntegrationTests-OnOffValues",
                        Aggregate = "stepInterpolation",
                        Operator = "eq",
                        Value = 1,
                    },
                },
                SteadyStateDetection = new List<SimulatorRoutineRevisionSteadyStateDetection>() {
                    new SimulatorRoutineRevisionSteadyStateDetection()
                    {
                        Enabled = true,
                        TimeseriesExternalId = "SimConnect-IntegrationTests-SsdSensorData",
                        Aggregate = "average",
                        MinSectionSize = 60,
                        VarThreshold = 1.0,
                        SlopeThreshold = -3.0,
                    },
                },
                Outputs = new List<SimulatorRoutineRevisionOutput>() {
                    new SimulatorRoutineRevisionOutput() {
                        Name = "Output Test 1",
                        ReferenceId = "OT1",
                        ValueType = SimulatorValueType.DOUBLE,
                        Unit = new SimulatorValueUnit() {
                            Name = "STB/MMscf",
                            Type = "LiqRate/GasRate",
                        },
                        SaveTimeseriesExternalId = "SimConnect-IntegrationTests-OT1-Output",
                    },
                },
                Inputs = new List<SimulatorRoutineRevisionInput>() {
                    new SimulatorRoutineRevisionInput() {
                        Name = "Input Test 1",
                        ReferenceId = "IT1",
                        Unit = new SimulatorValueUnit() {
                            Name = "STB/MMscf",
                            Type = "LiqRate/GasRate",
                        },
                        Aggregate = "average",
                        SaveTimeseriesExternalId = "SimConnect-IntegrationTests-IT1-SampledSsd",
                        SourceExternalId = "SimConnect-IntegrationTests-SsdSensorData",
                    },
                },
            },
            ExternalId = "Test Routine with Input TS and extended IO - 1",
            RoutineExternalId = "Test Routine with Input TS and extended IO",
            Script = new List<SimulatorRoutineRevisionScriptStage>() {
                new SimulatorRoutineRevisionScriptStage() {
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
                new SimulatorRoutineRevisionScriptStage() {
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
                new SimulatorRoutineRevisionScriptStage() {
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

        public static SimulatorRoutineCreateCommandItem SimulatorRoutineCreateWithTsAndExtendedIO = new SimulatorRoutineCreateCommandItem()
        {
            ExternalId = SimulatorRoutineRevisionWithTsAndExtendedIO.RoutineExternalId,
            ModelExternalId = "PROSPER-Connector_Test_Model",
            SimulatorIntegrationExternalId = "integration-tests-connector",
            Name = "Simulation Runner Test With TS and Extended IO",
        };

        public static SimulatorModelCreate SimulatorModelCreate = new SimulatorModelCreate()
        {
            ExternalId = "PROSPER-Connector_Test_Model",
            Name = "Connector Test Model",
            Description = "PROSPER-Connector Test Model",
            DataSetId = 8148496886298377,
            SimulatorExternalId = "PROSPER",
        };

        public static SimulatorModelRevisionCreate SimulatorModelRevisionCreateV1 = new SimulatorModelRevisionCreate()
        {
            ExternalId = "PROSPER-Connector_Test_Model-1",
            ModelExternalId = SimulatorModelCreate.ExternalId,
            Description = "integration test. can be deleted at any time. the test will recreate it.",
        };

        public static SimulatorModelRevisionCreate SimulatorModelRevisionCreateV2 = new SimulatorModelRevisionCreate()
        {
            ExternalId = "PROSPER-Connector_Test_Model-2",
            ModelExternalId = SimulatorModelCreate.ExternalId,
            Description = "integration test. can be deleted at any time. the test will recreate it.",
        };
    }
}