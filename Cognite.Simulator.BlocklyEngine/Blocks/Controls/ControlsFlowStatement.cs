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
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace BlocklyEngine.Blocks.Controls
{
  public class ControlsFlowStatement : IBlock
  {
    public override object Evaluate(Context context)
    {
      var flow = this.Fields.Get("FLOW");
      if (flow == "CONTINUE")
      {
        context.EscapeMode = EscapeMode.Continue;
        return null;
      }

      if (flow == "BREAK")
      {
        context.EscapeMode = EscapeMode.Break;
        return null;
      }

      throw new NotSupportedException($"{flow} flow is not supported");
    }

    public override SyntaxNode Generate(Context context)
    {
      var flow = this.Fields.Get("FLOW");
      if (flow == "CONTINUE")
      {
        return ContinueStatement();
      }

      if (flow == "BREAK")
      {
        return BreakStatement();
      }

      throw new NotSupportedException($"{flow} flow is not supported");
    }
  }

}