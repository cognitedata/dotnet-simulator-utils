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
using BlocklyEngine.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlocklyEngine.Blocks.Text
{
  public class TextIndexOf : IBlock
  {
    public override object Evaluate(Context context)
    {
      var mode = this.Fields.Get("END");

      var text = (this.Values.Evaluate("VALUE", context) ?? "").ToString();
      var term = (this.Values.Evaluate("FIND", context) ?? "").ToString();

      switch (mode)
      {
        case "FIRST": return (double)text.IndexOf(term) + 1;
        case "LAST": return (double)text.LastIndexOf(term) + 1;
        default: throw new ApplicationException("unknown mode");
      }
    }

    public override SyntaxNode Generate(Context context)
    {
      var textExpression = this.Values.Generate("VALUE", context) as ExpressionSyntax;
      if (textExpression == null) throw new ApplicationException($"Unknown expression for value.");

      var findExpression = this.Values.Generate("FIND", context) as ExpressionSyntax;
      if (findExpression == null) throw new ApplicationException($"Unknown expression for find.");

      var mode = this.Fields.Get("END");
      switch (mode)
      {
        case "FIRST": return SyntaxGenerator.MethodInvokeExpression(textExpression, nameof(string.IndexOf), findExpression);
        case "LAST": return SyntaxGenerator.MethodInvokeExpression(textExpression, nameof(string.LastIndexOf), findExpression);
        default: throw new NotSupportedException("unknown mode");
      }
    }
  }
}