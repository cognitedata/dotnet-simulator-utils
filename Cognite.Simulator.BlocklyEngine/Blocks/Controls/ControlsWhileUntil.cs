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
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace BlocklyEngine.Blocks.Controls
{
  public class ControlsWhileUntil : IBlock
  {
    public override object Evaluate(Context context)
    {
      var mode = this.Fields.Get("MODE");
      var value = this.Values.FirstOrDefault(x => x.Name == "BOOL");

      if (!this.Statements.Any(x => x.Name == "DO") || null == value) return base.Evaluate(context);

      var statement = this.Statements.Get("DO");

      if (mode == "WHILE")
      {
        while ((bool)value.Evaluate(context))
        {
          if (context.EscapeMode == EscapeMode.Break)
          {
            context.EscapeMode = EscapeMode.None;
            break;
          }
          statement.Evaluate(context);
        }
      }
      else
      {
        while (!(bool)value.Evaluate(context))
        {
          statement.Evaluate(context);
        }
      }

      return base.Evaluate(context);
    }

    public override SyntaxNode Generate(Context context)
    {
      var mode = this.Fields.Get("MODE");
      var value = this.Values.FirstOrDefault(x => x.Name == "BOOL");

      if (!this.Statements.Any(x => x.Name == "DO") || null == value) return base.Generate(context);

      var statement = this.Statements.Get("DO");

      var conditionExpression = value.Generate(context) as ExpressionSyntax;
      if (conditionExpression == null) throw new ApplicationException($"Unknown expression for condition.");

      var whileContext = new Context() { Parent = context };
      if (statement?.Block != null)
      {
        var statementSyntax = statement.Block.GenerateStatement(whileContext);
        if (statementSyntax != null)
        {
          whileContext.Statements.Add(statementSyntax);
        }
      }

      if (mode != "WHILE")
      {
        conditionExpression = PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, conditionExpression);
      }

      var whileStatement =
          WhileStatement(
            conditionExpression,
            Block(whileContext.Statements)
          );

      return Statement(whileStatement, base.Generate(context), context);
    }
  }

}