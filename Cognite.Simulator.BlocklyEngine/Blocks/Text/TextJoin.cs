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
using System.Text;
using BlocklyEngine.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace BlocklyEngine.Blocks.Text
{
  public class TextJoin : IBlock
  {
    public override object Evaluate(Context context)
    {
      var items = int.Parse(this.Mutations.GetValue("items"));

      var sb = new StringBuilder();
      for (var i = 0; i < items; i++)
      {
        if (!this.Values.Any(x => x.Name == $"ADD{i}")) continue;
        sb.Append(this.Values.Evaluate($"ADD{i}", context));
      }

      return sb.ToString();
    }

    public override SyntaxNode Generate(Context context)
    {
      var items = int.Parse(this.Mutations.GetValue("items"));

      var arguments = new List<ExpressionSyntax>();

      for (var i = 0; i < items; i++)
      {
        if (!this.Values.Any(x => x.Name == $"ADD{i}")) continue;
        var addExpression = this.Values.Generate($"ADD{i}", context) as ExpressionSyntax;
        if (addExpression == null) throw new ApplicationException($"Unknown expression for ADD{i}.");

        arguments.Add(addExpression);
      }

      if (!arguments.Any())
        return base.Generate(context);

      return
        SyntaxGenerator.MethodInvokeExpression(
          PredefinedType(
            Token(SyntaxKind.StringKeyword)
          ),
          nameof(string.Concat),
          arguments
        );
    }
  }
}