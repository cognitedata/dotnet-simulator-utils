using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace BlocklyEngine.Blocks.Variables
{
  public class VariablesSet : IBlock
  {
    public override object Evaluate(Context context)
    {
      var variables = context.Variables;
      variables.ToDictionary(x => x.Key, x => x.Value).ToList().ForEach(x => Console.WriteLine("VariablesSet: " + x.Key + " = " + x.Value));
      // Console.WriteLine("VariablesSet: " + variables);

      var value = Values.Evaluate("VALUE", context);
      var variableName = Fields.Get("VAR");
      // Fast-Solution
      if (variables.ContainsKey(variableName))
        variables[variableName] = value;
      else
      {
        var rootContext = context.GetRootContext();

        if (rootContext.Variables.ContainsKey(variableName))
          rootContext.Variables[variableName] = value;
        else
          variables.Add(variableName, value);
      }

      return base.Evaluate(context);
    }

    public override SyntaxNode Generate(Context context)
    {
      var variables = context.Variables;

      var variableName = Fields.Get("VAR").CreateValidName();

      var valueExpression = Values.Generate("VALUE", context) as ExpressionSyntax;
      if (valueExpression == null)
        throw new ApplicationException("Unknown expression for value.");

      context.GetRootContext().Variables[variableName] = valueExpression;

      var assignment = AssignmentExpression(
          SyntaxKind.SimpleAssignmentExpression,
          IdentifierName(variableName),
          valueExpression
      );

      return Statement(assignment, base.Generate(context), context);
    }
  }
}