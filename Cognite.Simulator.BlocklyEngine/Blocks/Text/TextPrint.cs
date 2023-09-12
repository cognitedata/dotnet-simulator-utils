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
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace BlocklyEngine.Blocks.Text
{
  public class TextPrint : IBlock
  {
    public override object Evaluate(Context context)
    {
      var text = this.Values.Evaluate("TEXT", context);

      Console.WriteLine(text);

      return base.Evaluate(context);
    }

    public override SyntaxNode Generate(Context context)
    {
      SyntaxNode syntaxNode = this.Values.Generate("TEXT", context);
      var expression = syntaxNode as ExpressionSyntax;
      if (expression == null) throw new ApplicationException($"Unknown expression for text.");

      var invocationExpression =
        SyntaxGenerator.MethodInvokeExpression(IdentifierName(nameof(Console)), nameof(Console.WriteLine), expression);

      return Statement(invocationExpression, base.Generate(context), context);
    }
  }

}