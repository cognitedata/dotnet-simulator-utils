using CogniteSdk.Alpha;
using Python.Runtime;
using Microsoft.Extensions.Logging;

namespace Sample.PythonConnector;

static class SimulatorDefinition
{
    public static SimulatorCreate Get()
    {
        try
        {
            return LoadFromPython();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to load simulator definition from Python. Ensure definition.py exists and contains get_simulator_definition() function.", 
                ex);
        }
    }
    
    private static SimulatorCreate LoadFromPython()
    {
        using (Py.GIL())
        {
            dynamic definitionModule = Py.Import("definition");
            dynamic pyDef = definitionModule.get_simulator_definition();
            
            return new SimulatorCreate()
            {
                ExternalId = pyDef["external_id"].ToString(),
                Name = pyDef["name"].ToString(),
                FileExtensionTypes = ConvertToStringList(pyDef.get("file_extension_types", null)),
                ModelTypes = ConvertModelTypes(pyDef.get("model_types", null)),
                StepFields = ConvertStepFields(pyDef.get("step_fields", null)),
                UnitQuantities = ConvertUnitQuantities(pyDef.get("unit_quantities", null)),
            };
        }
    }
    
    private static List<string> ConvertToStringList(dynamic? pyList)
    {
        var result = new List<string>();
        if (pyList == null) return result;
        
        foreach (var item in pyList)
        {
            result.Add(item.ToString());
        }
        return result;
    }
    
    private static List<SimulatorModelType> ConvertModelTypes(dynamic? pyList)
    {
        var result = new List<SimulatorModelType>();
        if (pyList == null) return result;
        
        foreach (var item in pyList)
        {
            result.Add(new SimulatorModelType
            {
                Name = item["name"].ToString(),
                Key = item["key"].ToString(),
            });
        }
        return result;
    }
    
    private static List<SimulatorStepField> ConvertStepFields(dynamic? pyList)
    {
        var result = new List<SimulatorStepField>();
        if (pyList == null) return result;
        
        foreach (var item in pyList)
        {
            var fields = new List<SimulatorStepFieldParam>();
            foreach (var field in item["fields"])
            {
                fields.Add(new SimulatorStepFieldParam
                {
                    Name = field["name"].ToString(),
                    Label = field["label"].ToString(),
                    Info = field.get("info", "").ToString(),
                });
            }
            
            result.Add(new SimulatorStepField
            {
                StepType = item["step_type"].ToString(),
                Fields = fields,
            });
        }
        return result;
    }
    
    private static List<SimulatorUnitQuantity> ConvertUnitQuantities(dynamic? pyList)
    {
        var result = new List<SimulatorUnitQuantity>();
        if (pyList == null) return result;
        
        foreach (var item in pyList)
        {
            var units = new List<SimulatorUnitEntry>();
            foreach (var unit in item["units"])
            {
                units.Add(new SimulatorUnitEntry
                {
                    Name = unit["name"].ToString(),
                    Label = unit["label"].ToString(),
                });
            }
            
            result.Add(new SimulatorUnitQuantity
            {
                Name = item["name"].ToString(),
                Label = item["label"].ToString(),
                Units = units,
            });
        }
        return result;
    }
}
