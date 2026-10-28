namespace Kassyi.GSharp.Compiler;

/// <summary>
/// Reads a fused StackLinq chain in device code (<c>data.AsStackEnumerable().Where(...).Sum()</c>) from its terminal call.
/// The transpiler (StackLinqFusionRule) and the analyzer (ParallelAggregateRule, through ParallelAggregatePlanner) read
/// the same chain.
/// </summary>
internal static class StackLinqChainExtractor
{
    /// <summary>
    /// The number of leading stages that only narrow the range of source indices: position stages, and Select between
    /// them. A position stage after them (after Where, TakeWhile or SkipWhile) counts the elements that pass instead.
    /// </summary>
    /// <remarks>
    /// [WHY] Folding a Skip that follows a Select skips the selector for the skipped elements, which C# calls. That changes
    /// nothing for a selector without side effects; after one that may write (a statement body, a method group, or an
    /// expression with writes), the following stages stay in the loop so that it is called as many times as in C#.
    /// </remarks>
    public static int CountRangeStages(in StackLinqChain chain, SemanticModel model)
    {
        var folded = 0;
        for (var i = 0; i < chain.Stages.Length - 1; i++)
        {
            var stage = chain.Stages[i];
            if (!stage.Operator.IsPositionStage() && stage.Operator != StackLinqOperator.Select)
            {
                break;
            }

            folded = i + 1;
            if (stage.Operator == StackLinqOperator.Select && MayWrite(stage, model))
            {
                break;
            }
        }

        return folded;
    }

    // A selector that is not an expression lambda, or one whose expression writes a variable or memory
    private static bool MayWrite(in LinqStage stage, SemanticModel model) =>
        StackLinqHelper.GetLambdaArgument(stage.Invocation, stage.Symbol).Expression is not LambdaExpressionSyntax
        {
            ExpressionBody: { } body
        } || !ExpressionEffects.Of(body, model).Writes.IsEmpty;

    /// <summary>The chain that ends in the terminal call <paramref name="node"/>, from its source to the terminal.</summary>
    public static bool TryExtract(
        InvocationExpressionSyntax node,
        SemanticModel semanticModel,
        IMethodSymbol symbol,
        out StackLinqChain chain)
    {
        chain = default;

        if (!StackLinqHelper.IsStackLinqMethod(symbol))
        {
            return false;
        }

        if (!StackLinqOperators.TryParse(symbol.Name, out var terminal) || !terminal.IsTerminal())
        {
            return false;
        }

        var chainBuilder = ImmutableArray.CreateBuilder<LinqStage>();
        var current = node;
        var currentSymbol = symbol;
        var isParallel = false;

        while (true)
        {
            if (currentSymbol.Name == "AsStackEnumerable")
            {
                break;
            }

            if (currentSymbol.Name == "AsParallel")
            {
                isParallel = true;
            }
            else if (StackLinqOperators.TryParse(currentSymbol.Name, out var op))
            {
                chainBuilder.Add(new LinqStage(op, current, currentSymbol));
            }
            else
            {
                return false;
            }

            if (current.Expression is MemberAccessExpressionSyntax
                {
                    Expression: InvocationExpressionSyntax nextInvocation
                })
            {
                if (semanticModel.GetSymbol(nextInvocation) is IMethodSymbol nextSymbol &&
                    StackLinqHelper.IsStackLinqMethod(nextSymbol))
                {
                    current = nextInvocation;
                    currentSymbol = nextSymbol;
                    continue;
                }
            }

            if (current.ArgumentList.Arguments.Count <= 0 ||
                current.ArgumentList.Arguments[0].Expression is not InvocationExpressionSyntax argInvocation)
            {
                return false;
            }

            if (semanticModel.GetSymbol(argInvocation) is not IMethodSymbol next ||
                !StackLinqHelper.IsStackLinqMethod(next))
            {
                return false;
            }

            current = argInvocation;
            currentSymbol = next;
        }

        var sourceArrayExpr = current.Expression is MemberAccessExpressionSyntax rootMemberAccess
            ? rootMemberAccess.Expression
            : current.ArgumentList.Arguments[0].Expression;

        chainBuilder.Reverse();
        chain = new StackLinqChain(sourceArrayExpr, chainBuilder.ToImmutable(), terminal, isParallel);
        return true;
    }
}
