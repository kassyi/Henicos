namespace Kassyi.GSharp.Compiler.Transpiler.Rules;

internal sealed class BclIntrinsicTranspilerRule : IInvocationTranspilerRule
{
    public static readonly BclIntrinsicTranspilerRule Instance = new();

    private BclIntrinsicTranspilerRule()
    {
    }

    public bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol)
    {
        if (!context.BclIntrinsicMap.TryGetValue(symbol.OriginalDefinition, out var mappingInfo))
        {
            return false;
        }

        context.Writer.Append(mappingInfo.CudaIntrinsicName);
        using (context.Writer.Parentheses())
        {
            for (var i = 0; i < node.ArgumentList.Arguments.Count; i++)
            {
                if (i == 0 && mappingInfo.CastFirstArgumentTo != null)
                {
                    using (context.Writer.Parentheses())
                    {
                        context.Writer.Append(mappingInfo.CastFirstArgumentTo);
                    }
                }

                using (i == 0 && mappingInfo.CastFirstArgumentTo != null
                           ? context.Writer.Parentheses()
                           : default)
                {
                    context.Visitor.Visit(node.ArgumentList.Arguments[i]);
                }

                if (i < node.ArgumentList.Arguments.Count - 1)
                {
                    context.Writer.AppendComma();
                }
            }
        }

        return true;
    }
}
