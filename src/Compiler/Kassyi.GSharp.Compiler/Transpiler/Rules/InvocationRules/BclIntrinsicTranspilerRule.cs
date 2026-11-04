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
        var matched = ArgumentMatchingHelper.MatchArgumentsToParameters(arguments, symbol);
        using var sequence = EvaluationOrder.BeginCall(ref context, arguments, symbol, receiver: null,
            spillAll: !IsInParameterOrder(arguments, matched));
        context.Emitter.Writer.Append(mappingInfo.CudaIntrinsicName);
        using (context.Emitter.Writer.Parentheses())
        {
            for (var i = 0; i < matched.Length; i++)
            {
                if (i > 0)
                {
                    context.Emitter.Writer.Append(',');
                }

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
                    if (matched[i] is { } argument)
                    {
                        sequence.AppendArgument(argument, symbol.Parameters[i],
                            ref context, convertToParameter: i != 0 || mappingInfo.CastFirstArgumentTo == null);
                    }
                    else
                    {
                        ArgumentMatchingHelper.AppendDefaultArgument(symbol.Parameters[i], ref context);
                    }
                }
            }
        }

        return true;
    }

    private static bool IsInParameterOrder(SeparatedSyntaxList<ArgumentSyntax> arguments, ArgumentSyntax?[] matched)
    {
        if (arguments.Count != matched.Length)
        {
            return false;
        }

        for (var i = 0; i < matched.Length; i++)
        {
            if (matched[i] != arguments[i])
            {
                return false;
            }
        }

        return true;
    }
}
