using static Kassyi.GSharp.Compiler.GSharpInternalNames.StackLinq;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules;

public sealed class StackLinqFusionRule : IInvocationTranspilerRule
{
    private static readonly Dictionary<StackLinqOperator, IStackLinqIntermediateStrategy> s_intermediateStrategies =
        new()
        {
            { StackLinqOperator.Where, new WhereStrategy() },
            { StackLinqOperator.Select, new SelectStrategy() },
            { StackLinqOperator.Take, new TakeStrategy() },
            { StackLinqOperator.Skip, new SkipStrategy() },
            { StackLinqOperator.TakeWhile, new TakeWhileStrategy() },
            { StackLinqOperator.SkipWhile, new SkipWhileStrategy() },
            { StackLinqOperator.TakeLast, new TakeLastStrategy() },
            { StackLinqOperator.SkipLast, new SkipLastStrategy() }
        };

    private static readonly Dictionary<StackLinqOperator, IStackLinqTerminalStrategy> s_terminalStrategies = new()
    {
        { StackLinqOperator.Sum, new SumStrategy() },
        { StackLinqOperator.Count, new CountStrategy() },
        { StackLinqOperator.Any, new AnyStrategy() },
        { StackLinqOperator.All, new AllStrategy() },
        { StackLinqOperator.First, new ElementSelectionStrategy(stopOnFirstMatch: true, trapOnEmpty: true) },
        { StackLinqOperator.FirstOrDefault, new ElementSelectionStrategy(stopOnFirstMatch: true, trapOnEmpty: false) },
        { StackLinqOperator.Last, new ElementSelectionStrategy(stopOnFirstMatch: false, trapOnEmpty: true) },
        { StackLinqOperator.LastOrDefault, new ElementSelectionStrategy(stopOnFirstMatch: false, trapOnEmpty: false) },
        { StackLinqOperator.Max, new ExtremaStrategy(isMax: true) },
        { StackLinqOperator.Min, new ExtremaStrategy(isMax: false) },
        { StackLinqOperator.Average, new AverageStrategy() },
        { StackLinqOperator.Aggregate, new AggregateStrategy() },
        { StackLinqOperator.Contains, new ContainsStrategy() }
    };

    /// <summary>Whether <paramref name="op"/> has a stage strategy; every non-terminal operator needs one.</summary>
    internal static bool HasIntermediateStrategy(StackLinqOperator op) => s_intermediateStrategies.ContainsKey(op);

    /// <summary>Whether <paramref name="op"/> has a terminal strategy; every terminal operator needs one.</summary>
    internal static bool HasTerminalStrategy(StackLinqOperator op) => s_terminalStrategies.ContainsKey(op);

    public bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol)
    {
        if (!StackLinqChainExtractor.TryExtract(node, context.Query.SemanticModel, symbol, out var chain))
        {
            return false;
        }

        if (StackLinqHelper.TryGetUnsupportedElementType(symbol, out var elementFailure))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, elementFailure);
            return true;
        }

        // [WHY] AsParallel() keeps the meaning of C#: every calling thread receives the aggregate of the whole sequence
        // (#624). On CUDA the threads of a block share the elements when ParallelAggregatePlanner shows that this gives the
        // same value (integer sums, uniform inputs, every thread of the block reaching the call); otherwise each thread
        // sums on its own, which is that result in every dialect and in any branch.
        var strategy = chain.IsParallel && context.Emitter.Dialect.IsCuda
            ? ParallelAggregatePlanner.Plan(node, context.Query.SemanticModel).Strategy
            : ParallelAggregateStrategy.PerThread;
        EmitFusedLoop(in chain, strategy, ref context);
        return true;
    }

    // ([&]() -> auto {
    //   auto&& __gsharp_fusion_src = <source>; int __gsharp_stage_<k>_n = <count>; ... <result> = <initial>;
    //   int __gsharp_fusion_lo = 0, __gsharp_fusion_hi = __gsharp_fusion_src.length; <folded position stages>
    //   for (int __gsharp_i = __gsharp_fusion_lo; __gsharp_i < __gsharp_fusion_hi; __gsharp_i++) { <stages> <terminal> }
    //   return <result>; }())
    // [WHY] C# evaluates the source and the stage arguments once, from left to right, when the chain is built; the
    // position stages right after the source (Select may come between) only narrow the range of indices.
    // The Block strategy (CUDA): the thread of rank r of the block takes the indices lo + r, lo + r + B, ... (64-bit, so
    // that i += B cannot wrap) and __gsharp_block_reduce combines the partial sums, which gives every thread of the block
    // the same total. The Grid strategy (cooperative kernels) does the same over the threads of the grid and combines the
    // block totals in __gsharp_grid_reduce, through the hidden scratch parameter. Only the range stages and Select / Where
    // can be in such a chain (ParallelAggregatePlanner).
    private static void EmitFusedLoop(in StackLinqChain chain, ParallelAggregateStrategy strategy, ref TranspileContext context)
    {
        // Every terminal operator (IsTerminal) has a strategy, and only the last stage is a terminal: a terminal returns
        // no StackLinq sequence for a further call
        var terminalStrategy = s_terminalStrategies[chain.Terminal];
        var terminalStage = chain.Stages[^1];
        var folded = StackLinqChainExtractor.CountRangeStages(in chain, context.Query.SemanticModel);
        var writer = context.Emitter.Writer;

        using (writer.ImmediatelyInvokedLambda("auto"))
        {
            writer.Append($"auto&& {FusionSource} = ");
            context.Visitor.Visit(chain.SourceArrayExpression);
            writer.AppendLine(";");

            EmitInitializations(in chain, folded, ref context, terminalStrategy, in terminalStage);
            EmitRange(in chain, folded, ref context);
            EmitLoopHeader(strategy, writer);

            using (writer.Scope())
            {
                EmitTakeStops(in chain, folded, writer);
                EmitElementRead(strategy, writer);
                EmitStages(in chain, folded, ref context, terminalStrategy, in terminalStage);
            }

            EmitFinalReturn(in chain, in terminalStage, terminalStrategy, strategy, ref context);
        }
    }

    // int __gsharp_fusion_lo = 0; int __gsharp_fusion_hi = <length>; <folded position stages>
    private static void EmitRange(in StackLinqChain chain, int folded, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        var length = FusionLength(chain.SourceArrayExpression, ref context);
        writer.AppendLine($"int {FusionLow} = 0;");
        writer.AppendLine($"int {FusionHigh} = {length};");
        for (var i = 0; i < folded; i++)
        {
            if (chain.Stages[i].Operator.IsPositionStage())
            {
                EmitRangeStage(chain.Stages[i], i, writer);
            }
        }
    }

    // [WHY] TakeEnumerator checks its count before it pulls from the stages before it: once it has given n elements
    // (or n <= 0), the next pull ends the sequence without calling them. Every pull after an element (the terminal, or
    // a Where / Skip / SkipWhile after the Take that passes over it) starts the next iteration, so the check comes
    // first in the body and a selector or predicate before the Take runs as many times as in C#.
    private static void EmitTakeStops(in StackLinqChain chain, int folded, SourceWriter writer)
    {
        for (var i = folded; i < chain.Stages.Length - 1; i++)
        {
            if (chain.Stages[i].Operator == StackLinqOperator.Take)
            {
                writer.AppendLine(
                    $"if ({StagePrefix}{i}{CountSuffix} >= {StagePrefix}{i}{StageCountSuffix}) {{ break; }}");
            }
        }
    }

    // auto __gsharp_val_0 = __gsharp_fusion_src[<index>];
    // The 64-bit index of the shared loop is narrowed for GSharpArray::operator[](int)
    private static void EmitElementRead(ParallelAggregateStrategy strategy, SourceWriter writer)
    {
        var narrowing = strategy != ParallelAggregateStrategy.PerThread ? "(int)" : "";
        writer.AppendLine($"auto {ValuePrefix}0 = {FusionSource}[{narrowing}{LoopIndex}];");
    }

    // The stages inside the loop (the folded position stages are already in the range), then the terminal, then the
    // braces the stages left open
    private static void EmitStages(
        in StackLinqChain chain,
        int folded,
        ref TranspileContext context,
        IStackLinqTerminalStrategy terminalStrategy,
        in LinqStage terminalStage)
    {
        var valIndex = 0;
        var openBraces = 0;
        for (var i = 0; i < chain.Stages.Length - 1; i++)
        {
            var stage = chain.Stages[i];
            if (i >= folded || !stage.Operator.IsPositionStage())
            {
                s_intermediateStrategies[stage.Operator].Emit(ref context, in stage, i, ref valIndex, ref openBraces);
            }
        }

        terminalStrategy.Emit(ref context, in terminalStage, FusionResult, valIndex);

        for (var i = 0; i < openBraces; i++)
        {
            context.Emitter.Writer.AppendLine("}");
        }
    }

    private static void EmitLoopHeader(ParallelAggregateStrategy strategy, SourceWriter writer)
    {
        if (strategy == ParallelAggregateStrategy.Grid)
        {
            writer.AppendLine(
                $"for (long long {LoopIndex} = " +
                $"{FusionLow} + (long long){GridBlockRank}() * {BlockSize}() + {BlockRank}(); " +
                $"{LoopIndex} < {FusionHigh}; " +
                $"{LoopIndex} += (long long){BlockSize}() * {GridBlockCount}())");
        }
        else if (strategy != ParallelAggregateStrategy.PerThread)
        {
            writer.AppendLine(
                $"for (long long {LoopIndex} = {FusionLow} + (long long){BlockRank}(); " +
                $"{LoopIndex} < {FusionHigh}; " +
                $"{LoopIndex} += {BlockSize}())");
        }
        else
        {
            writer.AppendLine($"for (int {LoopIndex} = {FusionLow}; {LoopIndex} < {FusionHigh}; {LoopIndex}++)");
        }
    }

    // After the loop: the terminal's post-loop code, then the return of the lambda (combined over the block or the grid
    // for a shared loop)
    private static void EmitFinalReturn(
        in StackLinqChain chain,
        in LinqStage terminalStage,
        IStackLinqTerminalStrategy terminalStrategy,
        ParallelAggregateStrategy strategy,
        ref TranspileContext context)
    {
        terminalStrategy.EmitPostLoop(ref context, in terminalStage, FusionResult);

        var kind = ClassifyFinalReturn(
            strategy == ParallelAggregateStrategy.Grid,
            strategy != ParallelAggregateStrategy.PerThread,
            in chain,
            in terminalStage
        );
        WriteReturn(kind, context.Emitter.Writer);
    }

    private static void WriteReturn(FinalReturnKind kind, SourceWriter writer)
    {
        switch (kind)
        {
            case FinalReturnKind.GridReduce:
                writer.Append($"return {GridReduce}({FusionResult}, {Plus}{{}}, {GridScratch});");
                break;
            case FinalReturnKind.BlockReduce:
                writer.Append($"return {BlockReduce}({FusionResult}, {Plus}{{}});");
                break;
            case FinalReturnKind.FloatCast:
                writer.Append($"return (float){FusionResult};");
                break;
            case FinalReturnKind.Plain:
                writer.Append($"return {FusionResult};");
                break;
            default:
                throw new InvalidOperationException($"Unknown {nameof(FinalReturnKind)}: {kind}");
        }

        writer.AppendLine();
    }

    private enum FinalReturnKind
    {
        GridReduce,
        BlockReduce,
        FloatCast,
        Plain,
    }

    private static FinalReturnKind ClassifyFinalReturn(
        bool isGrid,
        bool blockShared,
        in StackLinqChain chain,
        in LinqStage terminalStage)
    {
        if (isGrid)
        {
            return FinalReturnKind.GridReduce;
        }

        if (blockShared)
        {
            return FinalReturnKind.BlockReduce;
        }

        if ((chain.Terminal == StackLinqOperator.Average &&
             terminalStage.Symbol.ReturnType.SpecialType == SpecialType.System_Single) ||
            (chain.Terminal == StackLinqOperator.Sum && SumStrategy.AccumulatesInDouble(in terminalStage)))
        {
            return FinalReturnKind.FloatCast;
        }

        return FinalReturnKind.Plain;
    }

    // The source, then each stage argument and the terminal's initial value, in the C# order
    private static void EmitInitializations(
        in StackLinqChain chain,
        int folded,
        ref TranspileContext context,
        IStackLinqTerminalStrategy terminalStrategy,
        in LinqStage terminalStage)
    {
        for (var i = 0; i < chain.Stages.Length - 1; i++)
        {
            var stage = chain.Stages[i];
            if (stage.Operator.IsPositionStage())
            {
                // int __gsharp_stage_<i>_n = <count>;
                context.Emitter.Writer.Append($"int {StagePrefix}{i}{StageCountSuffix} = ");
                context.Visitor.Visit(StackLinqHelper.GetLambdaArgument(stage.Invocation, stage.Symbol).Expression);
                context.Emitter.Writer.AppendLine(";");
            }

            if (i >= folded || !stage.Operator.IsPositionStage())
            {
                s_intermediateStrategies[stage.Operator].EmitInitialValue(ref context, in stage, i);
            }
        }

        terminalStrategy.EmitInitialValue(ref context, FusionResult, in terminalStage);
    }


    // The element count of the source: a stack array has no length member, so its declared count is written
    private static string FusionLength(ExpressionSyntax source, ref TranspileContext context) =>
        StackArrayLocals.TryGetLength(source, context.Query.SemanticModel, out var length)
            ? length.ToString()
            : $"{FusionSource}.length";

    // Narrows [lo, hi) as C# does, with the count clamped to [0, hi - lo] (__gsharp_clamp_count)
    private static void EmitRangeStage(in LinqStage stage, int index, SourceWriter writer)
    {
        switch (stage.Operator)
        {
            case StackLinqOperator.Skip:
                writer.Append($"{FusionLow} += ");
                break;
            case StackLinqOperator.Take:
                writer.Append($"{FusionHigh} = {FusionLow} + ");
                break;
            case StackLinqOperator.SkipLast:
                writer.Append($"{FusionHigh} -= ");
                break;
            default:
                writer.Append($"{FusionLow} = {FusionHigh} - ");
                break;
        }

        writer.AppendLine($"{ClampCount}({StagePrefix}{index}{StageCountSuffix}, {FusionHigh} - {FusionLow});");
    }
}
