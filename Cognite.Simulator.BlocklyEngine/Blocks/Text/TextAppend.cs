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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace BlocklyEngine.Blocks.Text
{
  public class TextAppend : IBlock
  {
    public override object Evaluate(Context context)
    {
      var variables = context.Variables;

      var variableName = this.Fields.Get("VAR");
      var textToAppend = (this.Values.Evaluate("TEXT", context) ?? "").ToString();

      if (!variables.ContainsKey(variableName))
      {
        variables.Add(variableName, "");
      }
      var value = variables[variableName].ToString();

      variables[variableName] = value + textToAppend;

      return base.Evaluate(context);
    }

    public override SyntaxNode Generate(Context context)
    {
      var variables = context.Variables;
      var variableName = this.Fields.Get("VAR").CreateValidName();

      var textExpression = this.Values.Generate("TEXT", context) as ExpressionSyntax;
      if (textExpression == null)
        throw new ApplicationException($"Unknown expression for text.");

      context.GetRootContext().Variables[variableName] = textExpression;

      var assignment =
        AssignmentExpression(
          SyntaxKind.AddAssignmentExpression,
          IdentifierName(variableName),
          textExpression
        );

      return Statement(assignment, base.Generate(context), context);
    }
  }
}