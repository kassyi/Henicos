namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules;

public sealed class StackLinqFusionRule : IInvocationTranspilerRule
{
    private const string ResultVar = "__gsharp_fusion_res";

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
        { StackLinqOperator.Max, new ExtremaStrategy(">") },
        { StackLinqOperator.Min, new ExtremaStrategy("<") },
        { StackLinqOperator.Average, new AverageStrategy() },
        { StackLinqOperator.Aggregate, new AggregateStrategy() },
        { StackLinqOperator.Contains, new ContainsStrategy() }
    };

    public bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol)
    {
        if (!StackLinqChainExtractor.TryExtract(node, context.Query.SemanticModel, symbol, out var chain))
        {
            return false;
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
        if (!s_terminalStrategies.TryGetValue(chain.Terminal, out var terminalStrategy))
        {
            return;
        }

        var terminalStage = chain.Stages[^1];
        var folded = StackLinqChainExtractor.CountRangeStages(in chain, context.Query.SemanticModel);
        var writer = context.Emitter.Writer;
        var blockShared = strategy != ParallelAggregateStrategy.PerThread;
        var isGrid = strategy == ParallelAggregateStrategy.Grid;

        using (writer.ImmediatelyInvokedLambda("auto"))
        {
            writer.Append($"auto&& {StackLinqHelper.FusionSource} = ");
            context.Visitor.Visit(chain.SourceArrayExpression);
            writer.AppendLine(";");

            EmitInitializations(in chain, folded, ref context, terminalStrategy, in terminalStage, ResultVar);

            writer.AppendLine($"int {StackLinqHelper.FusionLow} = 0;");
            writer.AppendLine($"int {StackLinqHelper.FusionHigh} = {StackLinqHelper.FusionSource}.length;");
            for (var i = 0; i < folded; i++)
            {
                if (chain.Stages[i].Operator.IsPositionStage())
                {
                    EmitRangeStage(chain.Stages[i], i, writer);
                }
            }

            if (isGrid)
            {
                var rank = GSharpInternalNames.StackLinq.GridBlockRank;
                var size = GSharpInternalNames.StackLinq.BlockSize;
                writer.AppendLine(
                    $"for (long long {StackLinqHelper.LoopIndex} = {StackLinqHelper.FusionLow} + (long long){rank}() * {size}() + {GSharpInternalNames.StackLinq.BlockRank}(); {StackLinqHelper.LoopIndex} < {StackLinqHelper.FusionHigh}; {StackLinqHelper.LoopIndex} += (long long){size}() * {GSharpInternalNames.StackLinq.GridBlockCount}())");
            }
            else if (blockShared)
            {
                writer.AppendLine(
                    $"for (long long {StackLinqHelper.LoopIndex} = {StackLinqHelper.FusionLow} + (long long){GSharpInternalNames.StackLinq.BlockRank}(); {StackLinqHelper.LoopIndex} < {StackLinqHelper.FusionHigh}; {StackLinqHelper.LoopIndex} += {GSharpInternalNames.StackLinq.BlockSize}())");
            }
            else
            {
                writer.AppendLine(
                    $"for (int {StackLinqHelper.LoopIndex} = {StackLinqHelper.FusionLow}; {StackLinqHelper.LoopIndex} < {StackLinqHelper.FusionHigh}; {StackLinqHelper.LoopIndex}++)");
            }

            using (writer.Scope())
            {
                // [WHY] TakeEnumerator checks its count before it pulls from the stages before it: once it has given n
                // elements (or n <= 0), the next pull ends the sequence without calling them. Every pull after an
                // element (the terminal, or a Where / Skip / SkipWhile after the Take that passes over it) starts the
                // next iteration, so the check comes first in the body and a selector or predicate before the Take runs
                // as many times as in C#.
                for (var i = folded; i < chain.Stages.Length - 1; i++)
                {
                    if (chain.Stages[i].Operator == StackLinqOperator.Take)
                    {
                        writer.AppendLine(
                            $"if ({StackLinqHelper.StagePrefix}{i}_count >= {StackLinqHelper.StagePrefix}{i}{StackLinqHelper.StageCountSuffix}) {{ break; }}");
                    }
                }

                // The 64-bit index of the shared loop is narrowed for GSharpArray::operator[](int)
                writer.AppendLine(
                    $"auto {StackLinqHelper.ValuePrefix}0 = {StackLinqHelper.FusionSource}[{(blockShared ? "(int)" : "")}{StackLinqHelper.LoopIndex}];");

                var valIndex = 0;
                var openBraces = 0;

                for (var i = 0; i < chain.Stages.Length - 1; i++)
                {
                    var stage = chain.Stages[i];
                    if ((i >= folded || !stage.Operator.IsPositionStage()) &&
                        s_intermediateStrategies.TryGetValue(stage.Operator, out var stageStrategy))
                    {
                        stageStrategy.Emit(ref context, in stage, i, ref valIndex, ref openBraces);
                    }
                }

                terminalStrategy.Emit(ref context, in terminalStage, ResultVar, valIndex);

                for (var i = 0; i < openBraces; i++)
                {
                    writer.AppendLine("}");
                }
            }

            terminalStrategy.EmitPostLoop(ref context, in terminalStage, ResultVar);

            if (isGrid)
            {
                writer.Append(
                    $"return {GSharpInternalNames.StackLinq.GridReduce}({ResultVar}, {GSharpInternalNames.StackLinq.Plus}{{}}, {GSharpInternalNames.StackLinq.GridScratch});");
            }
            else if (blockShared)
            {
                writer.Append(
                    $"return {GSharpInternalNames.StackLinq.BlockReduce}({ResultVar}, {GSharpInternalNames.StackLinq.Plus}{{}});");
            }
            else
            {
                writer.Append($"return {ResultVar};");
            }

            writer.AppendLine();
        }
    }

    // The source, then each stage argument and the terminal's initial value, in the C# order
    private static void EmitInitializations(in StackLinqChain chain, int folded, ref TranspileContext context,
        IStackLinqTerminalStrategy terminalStrategy, in LinqStage terminalStage, string resultVar)
    {
        for (var i = 0; i < chain.Stages.Length - 1; i++)
        {
            var stage = chain.Stages[i];
            if (stage.Operator.IsPositionStage())
            {
                // int __gsharp_stage_<i>_n = <count>;
                context.Emitter.Writer.Append($"int {StackLinqHelper.StagePrefix}{i}{StackLinqHelper.StageCountSuffix} = ");
                context.Visitor.Visit(StackLinqHelper.GetLambdaArgument(stage.Invocation, stage.Symbol).Expression);
                context.Emitter.Writer.AppendLine(";");
            }

            if ((i >= folded || !stage.Operator.IsPositionStage()) &&
                s_intermediateStrategies.TryGetValue(stage.Operator, out var strategy))
            {
                strategy.EmitInitialValue(ref context, in stage, i);
            }
        }

        terminalStrategy.EmitInitialValue(ref context, resultVar, in terminalStage);
    }

    // Narrows [lo, hi) as C# does, with the count clamped to [0, hi - lo] (__gsharp_clamp_count)
    private static void EmitRangeStage(in LinqStage stage, int index, SourceWriter writer)
    {
        var lo = StackLinqHelper.FusionLow;
        var hi = StackLinqHelper.FusionHigh;
        switch (stage.Operator)
        {
            case StackLinqOperator.Skip:
                writer.Append($"{lo} += ");
                break;
            case StackLinqOperator.Take:
                writer.Append($"{hi} = {lo} + ");
                break;
            case StackLinqOperator.SkipLast:
                writer.Append($"{hi} -= ");
                break;
            default:
                writer.Append($"{lo} = {hi} - ");
                break;
        }

        writer.AppendLine(
            $"{GSharpInternalNames.StackLinq.ClampCount}({StackLinqHelper.StagePrefix}{index}{StackLinqHelper.StageCountSuffix}, {hi} - {lo});");
    }
}
