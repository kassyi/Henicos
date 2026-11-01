namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

internal static class StackLinqHelper
{
    /// <summary>Prefix of the per-stage element variable, followed by the stage index.</summary>
    internal const string ValuePrefix = "__gsharp_val_";

    /// <summary>The loop index of a fused pipeline.</summary>
    internal const string LoopIndex = "__gsharp_i";

    /// <summary>The source of a fused pipeline, evaluated once.</summary>
    internal const string FusionSource = "__gsharp_fusion_src";

    /// <summary>The first index of the source that a fused pipeline reads (after the folded position stages).</summary>
    internal const string FusionLow = "__gsharp_fusion_lo";

    /// <summary>The index past the last element that a fused pipeline reads.</summary>
    internal const string FusionHigh = "__gsharp_fusion_hi";

    /// <summary>Prefix of the per-stage counters, flags and arguments, followed by the stage index.</summary>
    internal const string StagePrefix = "__gsharp_stage_";

    /// <summary>Suffix of the argument of a position stage (<c>Take(n)</c>), evaluated once before the loop.</summary>
    internal const string StageCountSuffix = "_n";

    public static bool IsStackLinqMethod(IMethodSymbol symbol) =>
        symbol.ContainingNamespace is
        {
            Name: "Linq",
            ContainingNamespace: { Name: "ZeroAlloc", ContainingNamespace.Name: "Kassyi" }
        };

    /// <summary>
    /// Is it a StackLinq operator that runs on the host only? That is every overload passing a state value to its lambda
    /// (<c>Any(state, static (x, s) => ...)</c>; a kernel lambda reads outer values directly), and every
    /// operator in <c>StackLinqExtensions.HostOnly.cs</c>, plus <c>StackEnumerable.OfType</c>. The kernel transpiler
    /// rules have no strategy for them.
    /// </summary>
    public static bool IsHostOnly(IMethodSymbol symbol) =>
        IsStackLinqMethod(symbol) &&
        (symbol.Name is "AddTo" or "AddIf" or "Do" or "DoIf" or "SelectMany" or "TryFirst" or "IndexOf" or "OfType" ||
         symbol.Parameters
             .AsStackEnumerable()
             .Any(static p => p.Name == "state"));

    public static bool TryGetSourceExpression(
        InvocationExpressionSyntax invocation,
        IMethodSymbol symbol,
        [NotNullWhen(true)] out ExpressionSyntax? sourceExpression)
    {
        if (symbol.ReducedFrom is not null)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                sourceExpression = memberAccess.Expression;
                return true;
            }

            sourceExpression = null;
            return false;
        }

        var args = invocation.ArgumentList.Arguments;
        for (var i = 0; i < args.Count; i++)
        {
            var param = symbol.FindParameter(args, i);
            if (param is { Ordinal: 0 })
            {
                sourceExpression = args[i].Expression;
                return true;
            }
        }

        sourceExpression = null;
        return false;
    }

    public static int GetStageArgumentCount(IMethodSymbol symbol) =>
        symbol.ReducedFrom is not null ? symbol.Parameters.Length : symbol.Parameters.Length - 1;

    public static bool TryGetStageArgument(
        InvocationExpressionSyntax invocation,
        IMethodSymbol symbol,
        int stageArgOrdinal,
        [NotNullWhen(true)] out ArgumentSyntax? argument)
    {
        var targetOrdinal = (symbol.ReducedFrom is not null ? 0 : 1) + stageArgOrdinal;
        if (targetOrdinal < 0 || targetOrdinal >= symbol.Parameters.Length)
        {
            argument = null;
            return false;
        }

        var args = invocation.ArgumentList.Arguments;
        for (var i = 0; i < args.Count; i++)
        {
            var param = symbol.FindParameter(args, i);
            if (param is not null && param.Ordinal == targetOrdinal)
            {
                argument = args[i];
                return true;
            }
        }

        argument = null;
        return false;
    }

    public static bool HasInterferingReorderedArguments(
        InvocationExpressionSyntax invocation,
        IMethodSymbol symbol,
        SemanticModel semanticModel)
    {
        var args = invocation.ArgumentList.Arguments;
        for (var i = 0; i < args.Count; i++)
        {
            var paramI = symbol.FindParameter(args, i);
            if (paramI is null)
            {
                continue;
            }

            for (var j = i + 1; j < args.Count; j++)
            {
                var paramJ = symbol.FindParameter(args, j);
                if (paramJ is not null &&
                    paramI.Ordinal > paramJ.Ordinal &&
                    ExpressionEffects.Of(args[i].Expression, semanticModel)
                        .InterferesWith(ExpressionEffects.Of(args[j].Expression, semanticModel)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static ArgumentSyntax GetLambdaArgument(InvocationExpressionSyntax invocation, IMethodSymbol symbol) =>
        TryGetStageArgument(invocation, symbol, 0, out var arg)
            ? arg
            : invocation.ArgumentList.Arguments[0];

    public static bool HasPredicateOrSelector(InvocationExpressionSyntax invocation, IMethodSymbol symbol) =>
        GetStageArgumentCount(symbol) > 0;

    public static void EmitAccumulateElement(
        ref TranspileContext context,
        in LinqStage stage,
        string resultVar,
        int valIndex)
    {
        if (HasPredicateOrSelector(stage.Invocation, stage.Symbol))
        {
            var arg = GetLambdaArgument(stage.Invocation, stage.Symbol);
            context.Emitter.Writer.Append($"{resultVar} += (");
            context.Visitor.Visit(arg.Expression);
            context.Emitter.Writer.AppendLine($")({ValuePrefix}{valIndex});");
        }
        else
        {
            context.Emitter.Writer.AppendLine($"{resultVar} += {ValuePrefix}{valIndex};");
        }
    }
}
