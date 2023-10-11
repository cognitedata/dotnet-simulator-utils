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

namespace BlocklyEngine.Blocks.Logic
{
  public class LogicTernary : IBlock
  {
    public override object Evaluate(Context context)
    {
      var ifValue = (bool)this.Values.Evaluate("IF", context);

      if (ifValue)
      {
        if (this.Values.Any(x => x.Name == "THEN"))
        {
          return this.Values.Evaluate("THEN", context);
        }
      }
      else
      {
        if (this.Values.Any(x => x.Name == "ELSE"))
        {
          return this.Values.Generate("ELSE", context);
        }
      }
      return null;
    }
    public override SyntaxNode Generate(Context context)
    {
      var conditionalExpression = this.Values.Generate("IF", context) as ExpressionSyntax;
      if (conditionalExpression == null) throw new ApplicationException($"Unknown expression for conditional statement.");

      var trueExpression = this.Values.Generate("THEN", context) as ExpressionSyntax;
      if (trueExpression == null) throw new ApplicationException($"Unknown expression for true statement.");

      var falseExpression = this.Values.Generate("ELSE", context) as ExpressionSyntax;
      if (falseExpression == null) throw new ApplicationException($"Unknown expression for false statement.");

      return ConditionalExpression(
            conditionalExpression,
            trueExpression,
            falseExpression
          );
    }
  }

}