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

    public static ArgumentSyntax GetLambdaArgument(InvocationExpressionSyntax invocation, IMethodSymbol symbol)
    {
        if (symbol.IsExtensionMethod && invocation.Expression is MemberAccessExpressionSyntax)
        {
            return invocation.ArgumentList.Arguments[0];
        }

        return invocation.ArgumentList.Arguments[1];
    }

    public static bool HasPredicateOrSelector(InvocationExpressionSyntax invocation, IMethodSymbol symbol)
    {
        if (symbol.IsExtensionMethod && invocation.Expression is MemberAccessExpressionSyntax)
        {
            return invocation.ArgumentList.Arguments.Count > 0;
        }

        return invocation.ArgumentList.Arguments.Count > 1;
    }

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
