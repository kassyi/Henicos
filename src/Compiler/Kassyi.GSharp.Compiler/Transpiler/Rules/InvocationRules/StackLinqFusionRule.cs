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
}
