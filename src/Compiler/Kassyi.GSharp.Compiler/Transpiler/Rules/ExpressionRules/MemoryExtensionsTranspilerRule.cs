
namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

public sealed class MemoryExtensionsTranspilerRule : IInvocationTranspilerRule
{
    public bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol)
    {
        if (symbol.ContainingType.Name != "MemoryExtensions" || symbol.Name != "Sort")
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

        // Register the required thrust include for this specific kernel
        context.Emitter.RequiredIncludes.Add("<thrust/sort.h>");
        context.Emitter.RequiredIncludes.Add("<thrust/execution_policy.h>");

        context.Emitter.Writer.Append("thrust::sort(thrust::device, ");

        context.Visitor.Visit(spanExpr);
        context.Emitter.Writer.Append(".begin(), ");

        context.Visitor.Visit(spanExpr);
        context.Emitter.Writer.Append(".end())");

        return true;
    }
}
