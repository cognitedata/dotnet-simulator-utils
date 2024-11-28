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
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace BlocklyEngine
{
  public interface IFragment
  {
    // probably need a method like this here:
    object Evaluate(Context context);
    SyntaxNode Generate(Context context);
  }

  public class Workspace : IFragment
  {
    public Workspace()
    {
      this.Blocks = new List<IBlock>();
    }

    public IList<IBlock> Blocks { get; set; }

    public virtual object Evaluate(Context context)

    {
      // TODO: variables
      object returnValue = null;

      // first process procedure def blocks
      var processedProcedureDefBlocks = new List<IBlock>();
      // foreach (IBlock block in this.Blocks)
      // {
      //   if (block is ProceduresDef)
      //   {
      //     block.Evaluate(context);
      //     processedProcedureDefBlocks.Add(block);
      //   }
      // }

      foreach (var block in this.Blocks)
      {
        Console.WriteLine("Executing block: " + block.GetType().Name);
        if (!processedProcedureDefBlocks.Contains(block))
        {
          returnValue = block.Evaluate(context);
        }
      }

      return returnValue;
    }

    public virtual SyntaxNode Generate(Context context)
    {
      foreach (var block in this.Blocks)
      {
        var syntaxNode = block.Generate(context);
        if (syntaxNode == null)
          continue;

        var statement = syntaxNode as StatementSyntax;
        if (statement == null)
        {
          statement = ExpressionStatement(syntaxNode as ExpressionSyntax);
        }

        var comments = string.Join("\n", block.Comments.Select(x => x.Value));
        if (!string.IsNullOrWhiteSpace(comments))
        {
          statement = statement.WithLeadingTrivia(SyntaxFactory.Comment($"/* {comments} */"));
        }

        context.Statements.Add(statement);
      }

      foreach (var function in context.Functions.Reverse())
      {
        var methodDeclaration = function.Value as LocalFunctionStatementSyntax;
        if (methodDeclaration == null)
          continue;

        context.Statements.Insert(0, methodDeclaration);
      }

      foreach (var variable in context.Variables.Reverse())
      {
        var variableDeclaration = GenerateVariableDeclaration(variable.Key);
        context.Statements.Insert(0, variableDeclaration);
      }

      var blockSyntax = Block(context.Statements);
      return blockSyntax;
    }

    private LocalDeclarationStatementSyntax GenerateVariableDeclaration(string variableName)
    {
      return LocalDeclarationStatement(
            VariableDeclaration(
              IdentifierName("dynamic")
            )
            .WithVariables(
              SingletonSeparatedList(
                VariableDeclarator(
                  Identifier(variableName)
                )
              )
            )
          );
    }
  }

  public abstract class IBlock : IFragment
  {
    public IBlock()
    {
      this.Fields = new List<Field>();
      this.Values = new List<Value>();
      this.Statements = new List<Statement>();
      this.Mutations = new List<Mutation>();
      this.Comments = new List<Comment>();
    }

    public string Id { get; set; }
    public IList<Field> Fields { get; set; }
    public IList<Value> Values { get; set; }
    public IList<Statement> Statements { get; set; }
    public string Type { get; set; }
    public bool Inline { get; set; }
    public IBlock Next { get; set; }
    public IList<Mutation> Mutations { get; set; }
    public IList<Comment> Comments { get; set; }
    public virtual object Evaluate(Context context)
    {
      if (null != this.Next && context.EscapeMode == EscapeMode.None)
      {
        return this.Next.Evaluate(context);
      }
      return null;
    }

    public virtual SyntaxNode Generate(Context context)
    {
      if (null != this.Next && context.EscapeMode == EscapeMode.None)
      {
        var node = this.Next.Generate(context);
        var commentText = string.Join("\n", this.Next.Comments.Select(x => x.Value));
        if (string.IsNullOrWhiteSpace(commentText)) return node;
        return node.WithLeadingTrivia(SyntaxFactory.Comment($"/* {commentText} */"));
      }
      return null;
    }

    protected SyntaxNode Statement(SyntaxNode syntaxNode, SyntaxNode nextSyntaxNode, Context context)
    {
      if (nextSyntaxNode == null)
        return syntaxNode;

      StatementSyntax statementSyntax = null;

      if (syntaxNode is ExpressionSyntax expressionSyntax)
      {
        statementSyntax = ExpressionStatement(expressionSyntax);
      }
      else if (syntaxNode is StatementSyntax statement)
      {
        statementSyntax = statement;
      }

      if (statementSyntax == null)
        throw new ApplicationException($"Unknown statement.");

      context.Statements.Insert(0, statementSyntax);
      return nextSyntaxNode;
    }
  }

  public class Statement : IFragment
  {
    public string Name { get; set; }
    public IBlock Block { get; set; }
    public object Evaluate(Context context)
    {
      if (null == this.Block) return null;
      return this.Block.Evaluate(context);
    }
    public SyntaxNode Generate(Context context)
    {
      if (null == this.Block) return null;
      return this.Block.Generate(context);
    }
  }

  public class Value : IFragment
  {
    public string Name { get; set; }
    public IBlock Block { get; set; }
    public object Evaluate(Context context)
    {
      if (null == this.Block) return null;
      return this.Block.Evaluate(context);
    }
    public SyntaxNode Generate(Context context)
    {
      if (null == this.Block) return null;
      return this.Block.Generate(context);
    }
  }

  public class Field
  {
    public string Name { get; set; }
    public string Value { get; set; }
  }


  public enum EscapeMode
  {
    None,
    Break,
    Continue
  }


  public class Context
  {
    public Context()
    {
      this.Variables = new Dictionary<string, object>();
      this.Functions = new Dictionary<string, object>();

      this.Statements = new List<StatementSyntax>();
    }

    public IDictionary<string, object> Variables { get; set; }

    public IDictionary<string, object> Functions { get; set; }

    public EscapeMode EscapeMode { get; set; }

    public List<StatementSyntax> Statements { get; }

    public Context Parent { get; set; }
  }

  public class ProcedureContext : Context
  {
    public ProcedureContext()
    {
      this.Parameters = new Dictionary<string, object>();
    }

    public IDictionary<string, object> Parameters { get; set; }
  }

  public class Mutation
  {
    public Mutation(string domain, string name, string value)
    {
      this.Domain = domain;
      this.Name = name;
      this.Value = value;
    }
    public string Domain { get; set; }
    public string Name { get; set; }
    public string Value { get; set; }

  }


  public class Comment
  {
    public Comment(string value)
    {
      this.Value = value;
    }
    public string Value { get; set; }
  }

}
