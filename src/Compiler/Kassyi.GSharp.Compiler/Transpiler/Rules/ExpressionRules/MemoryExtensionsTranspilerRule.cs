
namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

public sealed class MemoryExtensionsTranspilerRule : IInvocationTranspilerRule
{
    public bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol)
    {
        if (symbol.ContainingType.Name != "MemoryExtensions" || symbol.Name != "Sort")
        {
            return false;
        }

        // Register the required thrust include for this specific kernel
        context.Emitter.RequiredIncludes.Add("<thrust/sort.h>");
        context.Emitter.RequiredIncludes.Add("<thrust/execution_policy.h>");

        context.Emitter.Writer.Append("thrust::sort(thrust::device, ");

        var spanArg = node.ArgumentList.Arguments[0];
        context.Visitor.Visit(spanArg.Expression);
        context.Emitter.Writer.Append(".begin(), ");

        context.Visitor.Visit(spanArg.Expression);
        context.Emitter.Writer.Append(".end())");

        return true;
    }
}
