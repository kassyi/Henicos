namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules;

public sealed class MemoryExtensionsTranspilerRule : IInvocationTranspilerRule
{
    public bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol)
    {
        if (symbol.ContainingType.Name != "MemoryExtensions" ||
            symbol.Name != "Sort" ||
            !symbol.ContainingNamespace.IsSystemNamespace())
        {
            return false;
        }

        ExpressionSyntax spanExpr;
        if (node.ArgumentList.Arguments.Count > 0)
        {
            // Static method call syntax: MemoryExtensions.Sort(data)
            spanExpr = node.ArgumentList.Arguments[0].Expression;
        }
        else if (node.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            // Extension method call syntax: data.Sort()
            spanExpr = memberAccess.Expression;
        }
        else
        {
            return false;
        }

        if (context.Emitter.Dialect.HasThrustAlgorithms())
        {
            context.Emitter.RequiredIncludes.Add("<thrust/sort.h>");
            context.Emitter.RequiredIncludes.Add("<thrust/execution_policy.h>");
            context.Emitter.Writer.Append("thrust::sort(thrust::device, ");
        }
        else
        {
            context.Emitter.Writer.Append("std::sort(");
        }

        context.Visitor.Visit(spanExpr);
        context.Emitter.Writer.Append(".begin(), ");

        context.Visitor.Visit(spanExpr);
        context.Emitter.Writer.Append(".end())");

        return true;
    }
}
