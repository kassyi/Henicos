namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

internal static class StackLinqHelper
{
    /// <summary>Prefix of the per-stage element variable, followed by the stage index.</summary>
    internal const string ValuePrefix = "__gsharp_val_";

    /// <summary>The loop index of a fused pipeline.</summary>
    internal const string LoopIndex = "__gsharp_i";

    /// <summary>The length of the source array of a fused pipeline.</summary>
    internal const string FusionLength = "__gsharp_fusion_length";

    public static bool IsStackLinqMethod(IMethodSymbol symbol) =>
        symbol.ContainingNamespace is
        {
            Name: "Linq",
            ContainingNamespace: { Name: "ZeroAlloc", ContainingNamespace.Name: "Kassyi" }
        };

    /// <summary>
    /// Is it an overload that passes a state value to its lambda (<c>Any(state, static (x, s) => ...)</c>)? Those exist
    /// so that host code avoids a closure; they are host-only because a kernel lambda reads outer values directly.
    /// </summary>
    public static bool TakesState(IMethodSymbol symbol) =>
        IsStackLinqMethod(symbol) && symbol.Parameters.AsStackEnumerable().Any(static p => p.Name == "state");

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
