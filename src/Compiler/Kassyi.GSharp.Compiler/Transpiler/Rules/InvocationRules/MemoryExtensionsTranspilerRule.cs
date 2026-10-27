namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules;

/// <summary>
/// Transpiles <c>System.MemoryExtensions.Sort</c> to the port of the .NET introsort in the Prelude (<c>Sort.hpp</c>), which
/// sorts the span in place in the calling thread and gives the order of .NET.
/// </summary>
public sealed class MemoryExtensionsTranspilerRule : IInvocationTranspilerRule
{
    public bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol)
    {
        var method = symbol.ReducedFrom ?? symbol;
        if (method.ContainingType.Name != "MemoryExtensions" ||
            method.Name != "Sort" ||
            !method.ContainingNamespace.IsSystemNamespace())
        {
            return false;
        }

        ExpressionSyntax spanExpr;
        if (symbol.ReducedFrom != null && node.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            // Extension method call syntax: data.Sort()
            spanExpr = memberAccess.Expression;
        }
        else if (node.ArgumentList.Arguments.Count > 0)
        {
            // Static method call syntax: MemoryExtensions.Sort(data)
            spanExpr = node.ArgumentList.Arguments[0].Expression;
        }
        else
        {
            return false;
        }

        // __gsharp_sort(<span>): the span is evaluated once
        context.Emitter.Writer.Append(GSharpInternalNames.Sort.Keys);
        using (context.Emitter.Writer.Parentheses())
        {
            context.Visitor.Visit(spanExpr);
        }

        return true;
    }
}
