namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules;

/// <summary>Invocation transpiler rule that maps BCL method calls to CUDA intrinsic functions.</summary>
internal sealed class BclIntrinsicTranspilerRule : IInvocationTranspilerRule
{
    public bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol)
    {
        if (!context.Query.BclIntrinsicMap.TryGetValue(symbol.OriginalDefinition, out var mappingInfo))
        {
            return false;
        }

        var arguments = node.ArgumentList.Arguments;
        using var sequence = EvaluationOrder.BeginCall(ref context, arguments, symbol, receiver: null, spillAll: false);
        context.Emitter.Writer.Append(mappingInfo.CudaIntrinsicName);
        using (context.Emitter.Writer.Parentheses())
        {
            for (var i = 0; i < arguments.Count; i++)
            {
                if (i == 0 && mappingInfo.CastFirstArgumentTo != null)
                {
                    using (context.Emitter.Writer.Parentheses())
                    {
                        context.Emitter.Writer.Append(mappingInfo.CastFirstArgumentTo);
                    }
                }

                using (i == 0 && mappingInfo.CastFirstArgumentTo != null
                           ? context.Emitter.Writer.Parentheses()
                           : default)
                {
                    sequence.AppendArgument(arguments[i], symbol.FindParameter(arguments, i),
                        ref context);
                }

                if (i < arguments.Count - 1)
                {
                    context.Emitter.Writer.Append(',');
                }
            }
        }

        return true;
    }
}
