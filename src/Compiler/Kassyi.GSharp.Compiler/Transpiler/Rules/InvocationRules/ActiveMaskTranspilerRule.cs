namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules;

/// <summary>
/// Invocation transpiler rule that narrows a CUDA <c>Warp.ActiveMask()</c> call inside keyed loops to the lanes of
/// the same iteration using <c>__match_any_sync</c>.
/// </summary>
internal sealed class ActiveMaskTranspilerRule : IInvocationTranspilerRule
{
    public bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol)
    {
        if (!context.Emitter.Dialect.IsCuda ||
            !SyncIntrinsicClassifier.IsWarpActiveMask(symbol) ||
            context.Emitter.ActiveMaskIterations is not { } plan ||
            !plan.TryGetKeys(node, out var keys) ||
            keys.IsDefaultOrEmpty)
        {
            return false;
        }

        WriteMatch(keys, keys.Length - 1, ref context);
        return true;
    }

    private static void WriteMatch(ImmutableArray<IterationKey> keys, int index, ref TranspileContext context)
    {
        if (index < 0)
        {
            context.Emitter.Writer.Append("__activemask()");
            return;
        }

        context.Emitter.Writer.Append("__match_any_sync");
        using (context.Emitter.Writer.Parentheses())
        {
            WriteMatch(keys, index - 1, ref context);
            context.Emitter.Writer.Append(',');
            AppendKey(keys[index], ref context);
        }
    }

    private static void AppendKey(IterationKey key, ref TranspileContext context)
    {
        if (key.Kind == IterationKeyKind.LoopVariable && key.LoopVariable is { } loopVariable)
        {
            context.Emitter.Writer.Append("(unsigned int)");
            CppNameResolver.AppendSymbolName(loopVariable, ref context);
            return;
        }

        context.Emitter.Writer.Append($"{GSharpInternalNames.Lowering.IterationCounterPrefix}{key.CounterIndex}");
    }
}
