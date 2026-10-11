using Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules.StackLinq;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

public sealed class StackLinqFusionRule : IInvocationTranspilerRule
{
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
        { "First", new FirstStrategy() },
        { "FirstOrDefault", new FirstOrDefaultStrategy() },
        { "Last", new LastStrategy() },
        { "LastOrDefault", new LastOrDefaultStrategy() },
        { "Max", new MaxStrategy() },
        { "Min", new MinStrategy() },
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

        if (!IsStackLinqMethod(symbol))
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

        while (true)
        {
            if (currentSymbol.Name == "AsStackEnumerable")
            {
                break;
            }

            if (!s_intermediateStrategies.ContainsKey(currentSymbol.Name) &&
                !s_terminalStrategies.ContainsKey(currentSymbol.Name))
            {
                return false;
            }

            chainBuilder.Add(new LinqStage(currentSymbol.Name, current, currentSymbol));

            if (current.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Expression is InvocationExpressionSyntax nextInvocation)
            {
                if (semanticModel.GetSymbol(nextInvocation) is IMethodSymbol nextSymbol &&
                    IsStackLinqMethod(nextSymbol))
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
                !IsStackLinqMethod(next))
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
        chain = new StackLinqChain(sourceArrayExpr, chainBuilder.ToImmutable(), methodName);
        return true;
    }

    private static void EmitFusedLoop(in StackLinqChain chain, ref TranspileContext context)
    {
        var terminalName = chain.TerminalMethodName;
        if (!s_terminalStrategies.TryGetValue(terminalName, out var terminalStrategy))
        {
            return;
        }

        const string ResultVar = "__fusion_res";
        var terminalStage = chain.Stages[^1];

        using (context.Emitter.Writer.ImmediatelyInvokedLambda("auto"))
        {
            // 1. Intermediate and Result variable initialization
            for (var i = 0; i < chain.Stages.Length - 1; i++)
            {
                var stage = chain.Stages[i];
                if (s_intermediateStrategies.TryGetValue(stage.Name, out var strategy))
                {
                    strategy.EmitInitialValue(ref context, in stage, i);
                }
            }
            terminalStrategy.EmitInitialValue(ref context, ResultVar, in terminalStage);

            // 2. For loop
            context.Emitter.Writer.Append("int __fusion_length = ");
            context.Visitor.Visit(chain.SourceArrayExpression);
            context.Emitter.Writer.AppendLine(".length;");
            
            context.Emitter.Writer.AppendLine("for (int __i = 0; __i < __fusion_length; __i++)");
            context.Emitter.Writer.AppendLine("{"); // Manually open for scope

            // 3. Initial element
            context.Emitter.Writer.Append("auto __val_0 = ");
            context.Visitor.Visit(chain.SourceArrayExpression);
            context.Emitter.Writer.AppendLine("[__i];");

            // 4. Intermediate stages
            var valIndex = 0;
            var openBraces = 1;

            for (var i = 0; i < chain.Stages.Length - 1; i++)
            {
                var stage = chain.Stages[i];
                if (s_intermediateStrategies.TryGetValue(stage.Name, out var strategy))
                {
                    strategy.Emit(ref context, in stage, i, ref valIndex, ref openBraces);
                }
            }

            // 5. Terminal stage
            terminalStrategy.Emit(ref context, in terminalStage, ResultVar, valIndex);

            // 6. Close braces
            for (var i = 0; i < openBraces; i++)
            {
                context.Emitter.Writer.AppendLine("}");
            }

            terminalStrategy.EmitPostLoop(ref context, in terminalStage, ResultVar);

            // 7. Return statement
            context.Emitter.Writer.Append($"return {ResultVar};");
            context.Emitter.Writer.AppendLine();
        }
    }

    private static bool IsStackLinqMethod(IMethodSymbol symbol)
    {
        return symbol.ContainingNamespace is
            { Name: "Linq", ContainingNamespace: { Name: "ZeroAlloc", ContainingNamespace.Name: "Kassyi" } };
    }
}
