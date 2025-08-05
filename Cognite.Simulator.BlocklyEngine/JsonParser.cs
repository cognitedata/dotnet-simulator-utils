using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using BlocklyEngine.Blocks;
using BlocklyEngine.Blocks.Variables;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BlocklyEngine
{
  public class JsonParser
  {
    IDictionary<string, Func<IBlock>> blocks = new Dictionary<string, Func<IBlock>>();

    public JsonParser AddBlock<T>(string type) where T : IBlock, new()
    {
      this.AddBlock(type, () => new T());
      return this;
    }

    public JsonParser AddBlock<T>(string type, T block) where T : IBlock
    {
      this.AddBlock(type, () => block);
      return this;
    }

    public JsonParser AddBlock(string type, Func<IBlock> blockFactory)
    {
      if (this.blocks.ContainsKey(type))
      {
        this.blocks[type] = blockFactory;
        return this;
      }
      this.blocks.Add(type, blockFactory);
      return this;
    }

    public Workspace Parse(string json)
        {
            var workspace = new Workspace();
            var jsonObject = JObject.Parse(json);
            // Console.WriteLine("jsonObject = " + jsonObject);
            var blocks = jsonObject["blocks"]["blocks"];
            // var variables = jsonObject["blocks"]["variables"];
            
            ParseGlobalVariables(jsonObject, workspace);

            foreach (var blockObject in blocks)
            {
                var block = ParseBlock(blockObject);
                if (block != null)
                {
                    workspace.Blocks.Add(block);
                }
            }

            // Parse global variables if needed, based on your JSON structure
            

            return workspace;
    }

    private void ParseGlobalVariables(JObject jsonObject, Workspace workspace)
    {
        // Adapt this code to parse global variables based on your JSON structure
        // Here's a sample implementation assuming a "variables" property:
        if (jsonObject.TryGetValue("variables", out JToken variablesToken) && variablesToken is JArray variablesArray)
        {

            foreach (var variableObject in variablesArray)
            {
                var variableName = variableObject.Value<string>("name");
                Console.WriteLine("Variable name = " + variableName);

                if (!string.IsNullOrWhiteSpace(variableName))
                {
                    // Generate variable members
                    var block = new GlobalVariablesSet();

                    var field = new Field
                    {
                        Name = "VAR",
                        Value = variableName,
                    };

                    block.Fields.Add(field);

                    var value = new Value
                    {
                        Name = "VALUE"
                    };

                    block.Values.Add(value);

                    workspace.Blocks.Add(block);
                }
            }
        }
    }
    
    public IBlock ParseBlock(JToken blockToken)
    {
        if (blockToken is JObject blockObject)
        {

            var type = blockObject.Value<string>("type");
            // Console.WriteLine("block = " + blockObject);
            if (!this.blocks.ContainsKey(type)) throw new ApplicationException($"block type not registered: '{type}'");
            var block = this.blocks[type]();

            block.Type = type;
            block.Id = blockObject.Value<string>("id");

            // if blockObject contains a key 
            if (blockObject.ContainsKey("fields"))
            {
                var fields = blockObject["fields"];
                foreach (var fieldObject in fields)
                {
                    ParseField(fieldObject, block);
                }
            } 
            if (blockObject.ContainsKey("inputs")) {
                var values = blockObject["inputs"];
                Console.WriteLine("values = " + values);
                foreach (var valueObject in values)
                {
                    ParseValue(valueObject, block);
                }
            } 
            if (blockObject.ContainsKey("statements")) {
                var statements = blockObject["statements"];
                foreach (var statementObject in statements)
                {
                    ParseStatement(statementObject, block);
                }
            } 
            if (blockObject.ContainsKey("comment")) {
                var comment = blockObject["comment"];
                ParseComment(comment, block);
            } 
            if (blockObject.ContainsKey("mutation") ) {
                var mutation = blockObject["mutation"];
                ParseMutation(mutation, block);
            } 
            if (blockObject.ContainsKey("next")) {
                var nextBlockObject = blockObject["next"];
                if (nextBlockObject != null)
                {
                    var nextBlock = ParseBlock(nextBlockObject);
                    block.Next = nextBlock;
                }
            }
            

            // print blockObject.Properties();
            // blockObject.Properties().ToDictionary(x => x.Name, x => x.Value).ToList().ForEach(x => Console.WriteLine("Key = " + x.Key + " " + x.Value));
            // foreach (var property in blockObject.Properties())
            // {
            //     switch (property.Name)
            //     {
            //         case "mutation":
            //             ParseMutation(property.Value, block);
            //             break;
            //         case "field":
            //             ParseField(property.Value, block);
            //             break;
            //         case "value":
            //             ParseValue(property.Value, block);
            //             break;
            //         case "statement":
            //             ParseStatement(property.Value, block);
            //             break;
            //         case "comment":
            //             ParseComment(property.Value, block);
            //             break;
            //         case "next":
            //             var nextBlockObject = property.Value as JObject;
            //             if (nextBlockObject != null)
            //             {
            //                 var nextBlock = ParseBlock(nextBlockObject["block"]);
            //                 block.Next = nextBlock;
            //             }
            //             break;
            //         case "type": 
            //             break;
            //         // default:
            //         //     throw new ArgumentException($"unknown property: {property.Name} . Value = {property}");
            //     }
            // }

            return block;
        }
        
        return null; // Handle
    }

    // {
    //   if (bool.Parse(node.GetAttribute("disabled") ?? "false")) return null;

    //   var type = node.GetAttribute("type");
    //   if (!this.blocks.ContainsKey(type)) throw new ApplicationException($"block type not registered: '{type}'");
    //   var block = this.blocks[type]();

    //   block.Type = type;
    //   block.Id = node.GetAttribute("id");

    //   foreach (XmlNode childNode in node.ChildNodes)
    //   {
    //     switch (childNode.LocalName)
    //     {
    //       case "mutation":
    //         ParseMutation(childNode, block);
    //         break;
    //       case "field":
    //         ParseField(childNode, block);
    //         break;
    //       case "value":
    //         ParseValue(childNode, block);
    //         break;
    //       case "statement":
    //         ParseStatement(childNode, block);
    //         break;
    //       case "comment":
    //         ParseComment(childNode, block);
    //         break;
    //       case "next":
    //         var nextBlock = ParseBlock(childNode.FirstChild);
    //         if (null != nextBlock) block.Next = nextBlock;
    //         break;
    //       default:
    //         throw new ArgumentException($"unknown xml type: {childNode.LocalName}");
    //     }
    //   }
    //   return block;
    // }

    void ParseField(JToken fieldToken, IBlock block)
    {
        if (fieldToken is JObject fieldObject)
        {
            Console.WriteLine("fieldObject = " + fieldObject);
            var name = fieldObject.Value<string>("name");
            var value = fieldObject.Value<string>("value");

            var field = new Field
            {
                Name = name,
                Value = value
            };
            block.Fields.Add(field);
        }
    }


    void ParseValue(JToken valueToken, IBlock block)
    {
        var childNode = (valueToken as JProperty)?.Value["block"] ?? (valueToken as JProperty)?.Value["shadow"];
        if (childNode == null) return;
        var childBlock = ParseBlock(childNode);

        var value = new Value
        {
            Name = valueToken.Value<string>("name"),
            Block = childBlock
        };
        block.Values.Add(value);
    }

    void ParseComment(JToken commentToken, IBlock block)
    {
        block.Comments.Add(new Comment(commentToken.Value<string>()));
    }

    void ParseStatement(JToken statementToken, IBlock block)
    {
        var childNode = statementToken["block"] ?? statementToken["shadow"];
        if (childNode == null) return;
        var childBlock = ParseBlock(childNode);

        var statement = new Statement
        {
            Name = statementToken.Value<string>("name"),
            Block = childBlock
        };
        block.Statements.Add(statement);
    }

    void ParseMutation(JToken mutationToken, IBlock block)
    {
        if (mutationToken is JObject mutationObject)
        {
            foreach (var attribute in mutationObject.Properties())
            {
                block.Mutations.Add(new Mutation("mutation", attribute.Name, attribute.Value.ToString()));
            }
        }

        foreach (var node in mutationToken.Children<JObject>())
        {
            foreach (var attribute in node.Properties())
            {
                block.Mutations.Add(new Mutation(node.Path, attribute.Name, attribute.Value.ToString()));
            }
        }
    }
  }
}