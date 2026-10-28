namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules;

public sealed class StackLinqFusionRule : IInvocationTranspilerRule
{
    private const string ResultVar = "__gsharp_fusion_res";

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
        { "First", new ElementSelectionStrategy(stopOnFirstMatch: true, trapOnEmpty: true) },
        { "FirstOrDefault", new ElementSelectionStrategy(stopOnFirstMatch: true, trapOnEmpty: false) },
        { "Last", new ElementSelectionStrategy(stopOnFirstMatch: false, trapOnEmpty: true) },
        { "LastOrDefault", new ElementSelectionStrategy(stopOnFirstMatch: false, trapOnEmpty: false) },
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

        // [WHY] AsParallel() keeps the meaning of C#: every calling thread receives the aggregate of the whole sequence
        // (#624). Each thread evaluates it on its own, which is that result in every dialect and in any branch.
        EmitFusedLoop(in chain, ref context);
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

    // ([&]() -> auto {
    //   auto&& __gsharp_fusion_src = <source>; int __gsharp_stage_<k>_n = <count>; ... <result> = <initial>;
    //   int __gsharp_fusion_lo = 0, __gsharp_fusion_hi = __gsharp_fusion_src.length; <folded position stages>
    //   for (int __gsharp_i = __gsharp_fusion_lo; __gsharp_i < __gsharp_fusion_hi; __gsharp_i++) { <stages> <terminal> }
    //   return <result>; }())
    // [WHY] C# evaluates the source and the stage arguments once, from left to right, when the chain is built; the
    // position stages right after the source (Select may come between) only narrow the range of indices.
    private static void EmitFusedLoop(in StackLinqChain chain, ref TranspileContext context)
    {
        var terminalName = chain.TerminalMethodName;
        if (!s_terminalStrategies.TryGetValue(terminalName, out var terminalStrategy))
        {
            return;
        }

        var terminalStage = chain.Stages[^1];
        var folded = CountFoldedStages(in chain, context.Query.SemanticModel);
        var writer = context.Emitter.Writer;

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
                if (IsPositionStage(chain.Stages[i].Name))
                {
                    EmitRangeStage(chain.Stages[i], i, writer);
                }
            }

            writer.AppendLine(
                $"for (int {StackLinqHelper.LoopIndex} = {StackLinqHelper.FusionLow}; {StackLinqHelper.LoopIndex} < {StackLinqHelper.FusionHigh}; {StackLinqHelper.LoopIndex}++)");
            using (writer.Scope())
            {
                // [WHY] TakeEnumerator checks its count before it pulls from the stages before it: once it has given n
                // elements (or n <= 0), the next pull ends the sequence without calling them. Every pull after an
                // element (the terminal, or a Where / Skip / SkipWhile after the Take that passes over it) starts the
                // next iteration, so the check comes first in the body and a selector or predicate before the Take runs
                // as many times as in C#.
                for (var i = folded; i < chain.Stages.Length - 1; i++)
                {
                    if (chain.Stages[i].Name == "Take")
                    {
                        writer.AppendLine(
                            $"if ({StackLinqHelper.StagePrefix}{i}_count >= {StackLinqHelper.StagePrefix}{i}{StackLinqHelper.StageCountSuffix}) {{ break; }}");
                    }
                }

                writer.AppendLine(
                    $"auto {StackLinqHelper.ValuePrefix}0 = {StackLinqHelper.FusionSource}[{StackLinqHelper.LoopIndex}];");

                var valIndex = 0;
                var openBraces = 0;

                for (var i = 0; i < chain.Stages.Length - 1; i++)
                {
                    var stage = chain.Stages[i];
                    if ((i >= folded || !IsPositionStage(stage.Name)) &&
                        s_intermediateStrategies.TryGetValue(stage.Name, out var strategy))
                    {
                        strategy.Emit(ref context, in stage, i, ref valIndex, ref openBraces);
                    }
                }

                terminalStrategy.Emit(ref context, in terminalStage, ResultVar, valIndex);

                for (var i = 0; i < openBraces; i++)
                {
                    writer.AppendLine("}");
                }
            }

            terminalStrategy.EmitPostLoop(ref context, in terminalStage, ResultVar);

            writer.Append($"return {ResultVar};");
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
            if (IsPositionStage(stage.Name))
            {
                // int __gsharp_stage_<i>_n = <count>;
                context.Emitter.Writer.Append($"int {StackLinqHelper.StagePrefix}{i}{StackLinqHelper.StageCountSuffix} = ");
                context.Visitor.Visit(StackLinqHelper.GetLambdaArgument(stage.Invocation, stage.Symbol).Expression);
                context.Emitter.Writer.AppendLine(";");
            }

            if ((i >= folded || !IsPositionStage(stage.Name)) &&
                s_intermediateStrategies.TryGetValue(stage.Name, out var strategy))
            {
                strategy.EmitInitialValue(ref context, in stage, i);
            }
        }

        terminalStrategy.EmitInitialValue(ref context, resultVar, in terminalStage);
    }

    // The leading stages that only narrow the range of indices: position stages, and Select between them.
    // [WHY] Folding a Skip that follows a Select skips the selector for the skipped elements, which C# calls. That changes
    // nothing for a selector without side effects; after one that may write (a statement body, a method group, or an
    // expression with writes), the following stages stay in the loop so that it is called as many times as in C#.
    private static int CountFoldedStages(in StackLinqChain chain, SemanticModel model)
    {
        var folded = 0;
        for (var i = 0; i < chain.Stages.Length - 1; i++)
        {
            var stage = chain.Stages[i];
            if (!IsPositionStage(stage.Name) && stage.Name != "Select")
            {
                break;
            }

            folded = i + 1;
            if (stage.Name == "Select" && MayWrite(stage, model))
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

    private static bool IsPositionStage(string name) => name is "Skip" or "Take" or "SkipLast" or "TakeLast";

    // Narrows [lo, hi) as C# does, with the count clamped to [0, hi - lo] (__gsharp_clamp_count)
    private static void EmitRangeStage(in LinqStage stage, int index, SourceWriter writer)
    {
        var lo = StackLinqHelper.FusionLow;
        var hi = StackLinqHelper.FusionHigh;
        switch (stage.Name)
        {
            case "Skip":
                writer.Append($"{lo} += ");
                break;
            case "Take":
                writer.Append($"{hi} = {lo} + ");
                break;
            case "SkipLast":
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
