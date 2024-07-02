// MIT License

// Copyright (c) 2018 Richard Astbury

// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:

// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.

// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using BlocklyEngine.Blocks.Controls;
using BlocklyEngine.Blocks.Logic;
using BlocklyEngine.Blocks.Text;
using BlocklyEngine.Blocks.Math;
using BlocklyEngine.Blocks.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlocklyEngine.Blocks
{
  public static class Extensions
  {
    public static object Evaluate(this IEnumerable<Value> values, string name, Context context)
    {
      var value = values.FirstOrDefault(x => x.Name == name);
      if (null == value) throw new ArgumentException($"value {name} not found");

      return value.Evaluate(context);
    }

    public static SyntaxNode Generate(this IEnumerable<Value> values, string name, Context context)
    {
      var value = values.FirstOrDefault(x => x.Name == name);
      if (null == value) throw new ArgumentException($"value {name} not found");

      return value.Generate(context);
    }

    public static string Get(this IEnumerable<Field> fields, string name)
    {
      var field = fields.FirstOrDefault(x => x.Name == name);
      if (null == field) throw new ArgumentException($"field {name} not found");

      return field.Value;
    }

    public static Statement Get(this IEnumerable<Statement> statements, string name)
    {
      var statement = statements.FirstOrDefault(x => x.Name == name);
      if (null == statement) throw new ArgumentException($"statement {name} not found");

      return statement;
    }

    public static string GetValue(this IList<Mutation> mutations, string name, string domain = "mutation")
    {
      var mut = mutations.FirstOrDefault(x => x.Domain == domain && x.Name == name);
      if (null == mut) return null;
      return mut.Value;
    }

    public static object Evaluate(this Workspace workspace, IDictionary<string, object> arguments = null)
    {
      var ctx = new Context();
      if (null != arguments)
      {
        ctx.Variables = arguments;
      }
      return workspace.Evaluate(ctx);
    }

    public static SyntaxNode Generate(this Workspace workspace)
    {
      var context = new Context();
      return workspace.Generate(context);
    }

    public static StatementSyntax GenerateStatement(this IFragment fragment, Context context)
    {
      var syntaxNode = fragment.Generate(context);

      var statementSyntax = syntaxNode as StatementSyntax;
      if (statementSyntax != null)
        return statementSyntax;

      var expressionSyntax = syntaxNode as ExpressionSyntax;
      if (expressionSyntax != null)
        return SyntaxFactory.ExpressionStatement(expressionSyntax);

      return null;
    }

    public static Context GetRootContext(this Context context)
    {
      var parentContext = context?.Parent;

      while (parentContext != null)
      {
        if (parentContext.Parent == null)
          return parentContext;

        parentContext = parentContext.Parent;
      };

      return context;
    }
    internal static string CreateValidName(this string name)
    {
      return name?.Replace(" ", "_");
    }

    public static Parser AddStandardBlocks(this Parser parser)
    {
      parser.AddBlock<ControlsRepeatExt>("controls_repeat_ext");
      parser.AddBlock<ControlsIf>("controls_if");
      parser.AddBlock<ControlsWhileUntil>("controls_whileUntil");
      parser.AddBlock<ControlsFlowStatement>("controls_flow_statements");
      parser.AddBlock<ControlsForEach>("controls_forEach");
      parser.AddBlock<ControlsFor>("controls_for");

      parser.AddBlock<LogicCompare>("logic_compare");
      parser.AddBlock<LogicBoolean>("logic_boolean");
      parser.AddBlock<LogicNegate>("logic_negate");
      parser.AddBlock<LogicOperation>("logic_operation");
      parser.AddBlock<LogicNull>("logic_null");
      parser.AddBlock<LogicTernary>("logic_ternary");

      parser.AddBlock<MathNumber>("math_number");
      parser.AddBlock<MathNumberProperty>("math_number_property");

      parser.AddBlock<TextBlock>("text");
      parser.AddBlock<TextPrint>("text_print");
      parser.AddBlock<TextPrompt>("text_prompt_ext");
      parser.AddBlock<TextLength>("text_length");
      parser.AddBlock<TextIsEmpty>("text_isEmpty");
      parser.AddBlock<TextTrim>("text_trim");
      parser.AddBlock<TextCaseChange>("text_changeCase");
      parser.AddBlock<TextAppend>("text_append");
      parser.AddBlock<TextJoin>("text_join");
      parser.AddBlock<TextIndexOf>("text_indexOf");

      parser.AddBlock<VariablesGet>("variables_get");
      parser.AddBlock<VariablesSet>("variables_set");

      return parser;
    }

    public static JsonParser AddStandardBlocks(this JsonParser parser)
    {
      parser.AddBlock<ControlsRepeatExt>("controls_repeat_ext");
      parser.AddBlock<ControlsIf>("controls_if");
      parser.AddBlock<ControlsWhileUntil>("controls_whileUntil");
      parser.AddBlock<ControlsFlowStatement>("controls_flow_statements");
      parser.AddBlock<ControlsForEach>("controls_forEach");
      parser.AddBlock<ControlsFor>("controls_for");

      parser.AddBlock<LogicCompare>("logic_compare");
      parser.AddBlock<LogicBoolean>("logic_boolean");
      parser.AddBlock<LogicNegate>("logic_negate");
      parser.AddBlock<LogicOperation>("logic_operation");
      parser.AddBlock<LogicNull>("logic_null");
      parser.AddBlock<LogicTernary>("logic_ternary");

      parser.AddBlock<MathNumber>("math_number");
      parser.AddBlock<MathNumberProperty>("math_number_property");

      parser.AddBlock<TextBlock>("text");
      parser.AddBlock<TextPrint>("text_print");
      parser.AddBlock<TextPrompt>("text_prompt_ext");
      parser.AddBlock<TextLength>("text_length");
      parser.AddBlock<TextIsEmpty>("text_isEmpty");
      parser.AddBlock<TextTrim>("text_trim");
      parser.AddBlock<TextCaseChange>("text_changeCase");
      parser.AddBlock<TextAppend>("text_append");
      parser.AddBlock<TextJoin>("text_join");
      parser.AddBlock<TextIndexOf>("text_indexOf");

      parser.AddBlock<VariablesGet>("variables_get");
      parser.AddBlock<VariablesSet>("variables_set");

      return parser;
    }
  }

}