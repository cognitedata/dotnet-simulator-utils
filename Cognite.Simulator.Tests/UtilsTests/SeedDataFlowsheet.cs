
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using Cognite.Extractor.Common;
using Cognite.Simulator.Tests.DataProcessingTests;
using Cognite.Simulator.Utils;

using CogniteSdk;
using CogniteSdk.Alpha;

using Com.Cognite.V1.Timeseries.Proto;

namespace Cognite.Simulator.Tests
{
    public class SeedDataFlowsheet
    {
        public static Dictionary<string, string> SimulatorModelRevisionDataDictionary = new Dictionary<string, string>
        {
            { "cronExpression", "*/5 * * * *"},
        };
        public static SimulatorModelRevisionDataFlowsheet SimulatorModelRevisionData = new SimulatorModelRevisionDataFlowsheet
        {
            SimulatorObjectNodes = new List<SimulatorModelRevisionDataObjectNode>(){
        new SimulatorModelRevisionDataObjectNode
        {
            Id = "Node1",
            Name = "Node1-Name",
            Type = "Node1-Type",
            GraphicalObject = new SimulatorModelRevisionDataGraphicalObject
            {
                Position = new SimulatorModelRevisionDataPosition
                {
                    X = 100,
                    Y = 100,
                },
                Width = 100,
                Height = 100,
                Angle = 30,
                Active = true,
            },
            Properties = new List<SimulatorModelRevisionDataProperty>()
            {
                new SimulatorModelRevisionDataProperty
                {
                    Name = "Node1-Property1",
                    ReadOnly = true,
                    ReferenceObject = new Dictionary<string, string>
                    {
                        { "Node1-key", "Node2-val" },
                    },
                    ValueType = SimulatorValueType.DOUBLE,
                    Value = SimulatorValue.Create(42),
                },
            },
        },
        new SimulatorModelRevisionDataObjectNode
        {
            Id = "Node2",
            Name = "Node2-Name",
            Type = "Node2-Type",
            GraphicalObject = new SimulatorModelRevisionDataGraphicalObject
            {
                Position = new SimulatorModelRevisionDataPosition
                {
                    X = 75,
                    Y = 60,
                },
                Width = 20,
                Height = 20,
                Active = false,
            },
            Properties = new List<SimulatorModelRevisionDataProperty>()
            {
                new SimulatorModelRevisionDataProperty
                {
                    Name = "Node2-Property2",
                    ValueType = SimulatorValueType.STRING,
                    Value = SimulatorValue.Create("Node2-Property2-Value"),
                    ReferenceObject = new Dictionary<string, string>
                    {
                        { "Node2-key", "Node1-val" },
                    },
                },
            },
        },
    },
            SimulatorObjectEdges = new List<SimulatorModelRevisionDataObjectEdge>() { },
            Thermodynamics = new SimulatorModelRevisionDataThermodynamic()
            {
                PropertyPackages = new List<string> { "Node1-PropertyPackage" },
                Components = new List<string> { "Node1-Component" },
            }
        };
    }
}
