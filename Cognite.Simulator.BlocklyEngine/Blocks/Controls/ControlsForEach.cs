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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
namespace BlocklyEngine.Blocks.Controls
{
  public class ControlsForEach : IBlock
  {
    public override object Evaluate(Context context)
    {
      var variableName = this.Fields.Get("VAR");
      var list = this.Values.Evaluate("LIST", context) as IEnumerable<object>;

      var statement = this.Statements.Where(x => x.Name == "DO").FirstOrDefault();

      if (null == statement) return base.Evaluate(context);

      foreach (var item in list)
      {
        if (context.Variables.ContainsKey(variableName))
        {
          context.Variables[variableName] = item;
        }
        else
        {
          context.Variables.Add(variableName, item);
        }
        statement.Evaluate(context);
      }

      return base.Evaluate(context);
    }

    public override SyntaxNode Generate(Context context)
    {
      var variableName = this.Fields.Get("VAR").CreateValidName();
      var listExpression = this.Values.Generate("LIST", context) as ExpressionSyntax;
      if (listExpression == null) throw new ApplicationException($"Unknown expression for list.");

      var statement = this.Statements.Where(x => x.Name == "DO").FirstOrDefault();

      if (null == statement) return base.Generate(context);

      var forEachContext = new Context() { Parent = context };
      if (statement?.Block != null)
      {
        var statementSyntax = statement.Block.GenerateStatement(forEachContext);
        if (statementSyntax != null)
        {
          forEachContext.Statements.Add(statementSyntax);
        }
      }

      var forEachStatement =
          ForEachStatement(
              IdentifierName("var"),
              Identifier(variableName),
              listExpression,
              Block(
                forEachContext.Statements
              )
            );

      return Statement(forEachStatement, base.Generate(context), context);
    }
  }

}