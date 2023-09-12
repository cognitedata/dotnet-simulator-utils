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

namespace BlocklyEngine.Blocks.Logic
{
  public class LogicOperation : IBlock
  {
    public override object Evaluate(Context context)
    {
      var a = (bool)(this.Values.Evaluate("A", context) ?? false);
      var b = (bool)(this.Values.Evaluate("B", context) ?? false);

      var op = this.Fields.Get("OP");

      switch (op)
      {
        case "AND": return a && b;
        case "OR": return a || b;
        default: throw new ApplicationException($"Unknown OP {op}");
      }

    }

    public override SyntaxNode Generate(Context context)
    {
      var firstExpression = this.Values.Generate("A", context) as ExpressionSyntax;
      if (firstExpression == null) throw new ApplicationException($"Unknown expression for value A.");

      var secondExpression = this.Values.Generate("B", context) as ExpressionSyntax;
      if (secondExpression == null) throw new ApplicationException($"Unknown expression for value B.");

      var opValue = this.Fields.Get("OP");

      var binaryOperator = GetBinaryOperator(opValue);
      var expression = BinaryExpression(binaryOperator, firstExpression, secondExpression);

      return ParenthesizedExpression(expression);
    }

    private SyntaxKind GetBinaryOperator(string opValue)
    {
      switch (opValue)
      {
        case "AND": return SyntaxKind.LogicalAndExpression;
        case "OR": return SyntaxKind.LogicalOrExpression;

        default: throw new ApplicationException($"Unknown OP {opValue}");
      }
    }
  }

}