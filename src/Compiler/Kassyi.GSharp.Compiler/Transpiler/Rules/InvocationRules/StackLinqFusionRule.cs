
namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules;

public sealed class StackLinqFusionRule : IInvocationTranspilerRule
{
    private const string ResultVar = "__gsharp_fusion_res";
    private const string ThreadId = "__gsharp_tid";
    private const string Stride = "__gsharp_stride";
    private const string Mask = "__gsharp_mask";
    private const string WarpSums = "__gsharp_warp_sums";
    private const string WarpId = "__gsharp_warp_id";
    private const string LaneId = "__gsharp_lane_id";
    private const string Offset = "__gsharp_offset";

    private static readonly Dictionary<string, IStackLinqIntermediateStrategy> s_intermediateStrategies = new()
    {
        { "Where", new WhereStrategy() },
        { "Select", new SelectStrategy() },
        { "Take", new TakeStrategy() },
        { "Skip", new SkipStrategy() },
        { "TakeWhile", new TakeWhileStrategy() },
        { "SkipWhile", new SkipWhileStrategy() },
        { "TakeLast", new TakeLastStrategy() },
        { "SkipLast", new SkipLastStrategy() }
    };

    private static readonly Dictionary<string, IStackLinqTerminalStrategy> s_terminalStrategies = new()
    {
        { "Sum", new SumStrategy() },
        { "Count", new CountStrategy() },
        { "Any", new AnyStrategy() },
        { "All", new AllStrategy() },
        { "First", new ElementSelectionStrategy(stopOnFirstMatch: true) },
        { "FirstOrDefault", new ElementSelectionStrategy(stopOnFirstMatch: true) },
        { "Last", new ElementSelectionStrategy(stopOnFirstMatch: false) },
        { "LastOrDefault", new ElementSelectionStrategy(stopOnFirstMatch: false) },
        { "Max", new ExtremaStrategy(">") },
        { "Min", new ExtremaStrategy("<") },
        { "Average", new AverageStrategy() },
        { "Aggregate", new AggregateStrategy() },
        { "Contains", new ContainsStrategy() }
    };

    public bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol)
    {
        if (!TryExtractChain(node, context.Query.SemanticModel, symbol, out var chain))
        {
            return false;
        }

        if (chain.IsParallel && context.Emitter.Dialect.IsCuda && chain.TerminalMethodName is "Sum" or "Count")
        {
            EmitParallelFusedLoop(in chain, ref context);
        }
        else
        {
            EmitFusedLoop(in chain, ref context);
        }

        return true;
    }

    private static bool TryExtractChain(
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

        var methodName = symbol.Name;
        if (!s_terminalStrategies.ContainsKey(methodName))
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
            else if (!s_intermediateStrategies.ContainsKey(currentSymbol.Name) &&
                     !s_terminalStrategies.ContainsKey(currentSymbol.Name))
            {
                return false;
            }
            else
            {
                chainBuilder.Add(new LinqStage(currentSymbol.Name, current, currentSymbol));
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
        chain = new StackLinqChain(sourceArrayExpr, chainBuilder.ToImmutable(), methodName, isParallel);
        return true;
    }

    private static void EmitFusedLoop(in StackLinqChain chain, ref TranspileContext context)
    {
        var terminalName = chain.TerminalMethodName;
        if (!s_terminalStrategies.TryGetValue(terminalName, out var terminalStrategy))
        {
            return;
        }

        var terminalStage = chain.Stages[^1];

        using (context.Emitter.Writer.ImmediatelyInvokedLambda("auto"))
        {
            EmitInitializations(in chain, ref context, terminalStrategy, in terminalStage, ResultVar);

            context.Emitter.Writer.Append($"int {StackLinqHelper.FusionLength} = ");
            context.Visitor.Visit(chain.SourceArrayExpression);
            context.Emitter.Writer.AppendLine(".length;");

            context.Emitter.Writer.AppendLine($"for (int {StackLinqHelper.LoopIndex} = 0; {StackLinqHelper.LoopIndex} < {StackLinqHelper.FusionLength}; {StackLinqHelper.LoopIndex}++)");
            using (context.Emitter.Writer.Scope())
            {
                context.Emitter.Writer.Append($"auto {StackLinqHelper.ValuePrefix}0 = ");
                context.Visitor.Visit(chain.SourceArrayExpression);
                context.Emitter.Writer.AppendLine($"[{StackLinqHelper.LoopIndex}];");

                var valIndex = 0;
                var openBraces = 0;

                for (var i = 0; i < chain.Stages.Length - 1; i++)
                {
                    var stage = chain.Stages[i];
                    if (s_intermediateStrategies.TryGetValue(stage.Name, out var strategy))
                    {
                        strategy.Emit(ref context, in stage, i, ref valIndex, ref openBraces);
                    }
                }

                terminalStrategy.Emit(ref context, in terminalStage, ResultVar, valIndex);

                for (var i = 0; i < openBraces; i++)
                {
                    context.Emitter.Writer.AppendLine("}");
                }
            }

            terminalStrategy.EmitPostLoop(ref context, in terminalStage, ResultVar);

            context.Emitter.Writer.Append($"return {ResultVar};");
            context.Emitter.Writer.AppendLine();
        }
    }

    private static void EmitParallelFusedLoop(in StackLinqChain chain, ref TranspileContext context)
    {
        var terminalName = chain.TerminalMethodName;
        if (!s_terminalStrategies.TryGetValue(terminalName, out var terminalStrategy))
        {
            return;
        }

        var terminalStage = chain.Stages[^1];

        using (context.Emitter.Writer.ImmediatelyInvokedLambda("auto"))
        {
            EmitInitializations(in chain, ref context, terminalStrategy, in terminalStage, ResultVar);
            EmitThreadLocalReductionLoop(in chain, ref context, terminalStrategy, in terminalStage, ResultVar);
            EmitBlockWideReduction(ref context, in terminalStage, ResultVar);

            context.Emitter.Writer.AppendLine($"return {ResultVar};");
        }
    }

    private static void EmitInitializations(in StackLinqChain chain, ref TranspileContext context,
        IStackLinqTerminalStrategy terminalStrategy, in LinqStage terminalStage, string resultVar)
    {
        for (var i = 0; i < chain.Stages.Length - 1; i++)
        {
            var stage = chain.Stages[i];
            if (s_intermediateStrategies.TryGetValue(stage.Name, out var strategy))
            {
                strategy.EmitInitialValue(ref context, in stage, i);
            }
        }

        terminalStrategy.EmitInitialValue(ref context, resultVar, in terminalStage);
    }

    private static void EmitThreadLocalReductionLoop(in StackLinqChain chain, ref TranspileContext context,
        IStackLinqTerminalStrategy terminalStrategy, in LinqStage terminalStage, string resultVar)
    {
        context.Emitter.Writer.Append($"int {StackLinqHelper.FusionLength} = ");
        context.Visitor.Visit(chain.SourceArrayExpression);
        context.Emitter.Writer.AppendLine(".length;");

        context.Emitter.Writer.AppendLine($"int {ThreadId} = threadIdx.x + blockIdx.x * blockDim.x;");
        context.Emitter.Writer.AppendLine($"int {Stride} = blockDim.x * gridDim.x;");

        context.Emitter.Writer.AppendLine($"for (int {StackLinqHelper.LoopIndex} = {ThreadId}; {StackLinqHelper.LoopIndex} < {StackLinqHelper.FusionLength}; {StackLinqHelper.LoopIndex} += {Stride})");
        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Writer.Append($"auto {StackLinqHelper.ValuePrefix}0 = ");
            context.Visitor.Visit(chain.SourceArrayExpression);
            context.Emitter.Writer.AppendLine($"[{StackLinqHelper.LoopIndex}];");

            var valIndex = 0;
            var openBraces = 0;

            for (var i = 0; i < chain.Stages.Length - 1; i++)
            {
                var stage = chain.Stages[i];
                if (stage.Name == "AsParallel")
                {
                    continue;
                }

                if (s_intermediateStrategies.TryGetValue(stage.Name, out var strategy))
                {
                    strategy.Emit(ref context, in stage, i, ref valIndex, ref openBraces);
                }
            }

            terminalStrategy.Emit(ref context, in terminalStage, resultVar, valIndex);

            for (var i = 0; i < openBraces; i++)
            {
                context.Emitter.Writer.AppendLine("}");
            }

            terminalStrategy.EmitPostLoop(ref context, in terminalStage, resultVar);
        }
    }

    private static void EmitBlockWideReduction(ref TranspileContext context,
        in LinqStage terminalStage, string resultVar)
    {
        context.Emitter.Writer.AppendLine($"unsigned int {Mask} = 0xffffffff;");
        EmitWarpShuffleSum(ref context, resultVar);

        context.Emitter.Writer.Append("__shared__ ");
        context.Emitter.Dialect.AppendType(ref context, terminalStage.Symbol.ReturnType);
        context.Emitter.Writer.AppendLine($" {WarpSums}[32];");

        context.Emitter.Writer.AppendLine($"int {WarpId} = threadIdx.x / 32;");
        context.Emitter.Writer.AppendLine($"int {LaneId} = threadIdx.x % 32;");

        context.Emitter.Writer.AppendLine($"if ({LaneId} == 0)");
        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Writer.AppendLine($"{WarpSums}[{WarpId}] = {resultVar};");
        }

        context.Emitter.Writer.AppendLine("__syncthreads();");

        context.Emitter.Writer.AppendLine($"if ({WarpId} == 0)");
        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Writer.AppendLine(
                $"{resultVar} = ({LaneId} < (blockDim.x + 31) / 32) ? {WarpSums}[{LaneId}] : 0;");
            EmitWarpShuffleSum(ref context, resultVar);
        }
    }

    private static void EmitWarpShuffleSum(ref TranspileContext context, string resultVar)
    {
        context.Emitter.Writer.AppendLine("#pragma unroll");
        context.Emitter.Writer.AppendLine($"for (int {Offset} = 16; {Offset} > 0; {Offset} /= 2)");
        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Writer.AppendLine($"{resultVar} += __shfl_down_sync({Mask}, {resultVar}, {Offset});");
        }
    }
}
