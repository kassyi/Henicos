using Microsoft.CodeAnalysis;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules;

/// <summary>
/// Defines a non-generic strategy for transpiling a specific type of C# syntax node to CUDA C++.
/// </summary>
public interface ISyntaxNodeTranspilerRule
{
    /// <summary>
    /// Transpiles the given syntax node into the current transpilation context.
    /// </summary>
    void Transpile(SyntaxNode node, ref TranspileContext context);
}

/// <summary>
/// Defines a strongly-typed strategy for transpiling a specific type of C# syntax node.
/// </summary>
public interface ISyntaxNodeTranspilerRule<in TNode> : ISyntaxNodeTranspilerRule where TNode : SyntaxNode
{
    /// <summary>
    /// Transpiles the given strongly-typed syntax node into the current transpilation context.
    /// </summary>
    void Transpile(TNode node, ref TranspileContext context);
}

/// <summary>
/// Base class for strongly-typed syntax node transpilers. 
/// Automatically handles downcasting from the generic SyntaxNode dispatcher.
/// </summary>
public abstract class SyntaxNodeTranspilerRuleBase<TNode> : ISyntaxNodeTranspilerRule<TNode> where TNode : SyntaxNode
{
    void ISyntaxNodeTranspilerRule.Transpile(SyntaxNode node, ref TranspileContext context)
    {
        if (node is TNode typedNode)
        {
            Transpile(typedNode, ref context);
        }
    }

    public abstract void Transpile(TNode node, ref TranspileContext context);
}
