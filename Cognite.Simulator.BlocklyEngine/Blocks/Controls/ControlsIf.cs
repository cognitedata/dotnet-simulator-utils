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
  public class ControlsIf : IBlock
  {
    public override object Evaluate(Context context)
    {

      var ifCount = 1;
      if (null != this.Mutations.GetValue("elseif"))
      {
        var elseIf = this.Mutations.GetValue("elseif");
        ifCount = int.Parse(elseIf) + 1;
      }

      var done = false;
      for (var i = 0; i < ifCount; i++)
      {
        if ((bool)Values.Evaluate($"IF{i}", context))
        {
          var statement = this.Statements.Get($"DO{i}");
          statement.Evaluate(context);
          done = true;
          break;
        }
      }

      if (!done)
      {
        if (null != this.Mutations.GetValue("else"))
        {
          var elseExists = this.Mutations.GetValue("else");
          if (elseExists == "1")
          {
            var statement = this.Statements.Get("ELSE");
            statement.Evaluate(context);
          }
        }
      }

      return base.Evaluate(context);
    }

    public override SyntaxNode Generate(Context context)
    {
      var ifCount = 1;
      string elseifMutation = this.Mutations.GetValue("elseif");
      if (!string.IsNullOrEmpty(elseifMutation))
      {
        var elseIf = elseifMutation;
        ifCount = int.Parse(elseIf) + 1;
      }

      var ifStatements = new List<IfStatementSyntax>();

      for (var i = 0; i < ifCount; i++)
      {
        var conditional = Values.Generate($"IF{i}", context) as ExpressionSyntax;
        if (conditional == null) throw new ApplicationException($"Unknown expression for condition.");

        var statement = this.Statements.Get($"DO{i}");

        var ifContext = new Context() { Parent = context };
        if (statement?.Block != null)
        {
          var statementSyntax = statement.Block.GenerateStatement(ifContext);
          if (statementSyntax != null)
          {
            ifContext.Statements.Add(statementSyntax);
          }
        }

        var newIfStatement = IfStatement(conditional, Block(ifContext.Statements));
        ifStatements.Add(newIfStatement);
      }

      string elseMutation = this.Mutations.GetValue("else");
      if (elseMutation == "1")
      {
        var statement = this.Statements.Get("ELSE");

        var elseContext = new Context() { Parent = context };
        if (statement?.Block != null)
        {
          var statementSyntax = statement.Block.GenerateStatement(elseContext);
          if (statementSyntax != null)
          {
            elseContext.Statements.Add(statementSyntax);
          }
        }

        int lastIndex = ifStatements.Count - 1;
        if (lastIndex >= 0)
        {
          var lastIfStatement = ifStatements[lastIndex];
          ifStatements[lastIndex] = lastIfStatement.WithElse(ElseClause(Block(elseContext.Statements)));
        }
      }

      for (int index = ifStatements.Count - 1; index >= 0; index--)
      {
        var currentIfStatement = ifStatements[index];
        if (index > 0)
        {
          var previousIfStatement = ifStatements[index - 1];
          ifStatements[index - 1] = previousIfStatement.WithElse(ElseClause(currentIfStatement));
        }
      }


      var ifStatement = ifStatements.FirstOrDefault();

      return Statement(ifStatement, base.Generate(context), context);
    }
  }
}