namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules;

/// <summary>
/// Transpiles Kassyi.ZeroAlloc.Linq method invocations into their corresponding C++ Prelude equivalents.
/// </summary>
public sealed class StackLinqTranspilerRule : IInvocationTranspilerRule
{
    public bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol)
    {
        if (!StackLinqHelper.IsStackLinqMethod(symbol))
        {
            return false;
        }

        var methodName = symbol.Name;

        // Detect indexed overloads and map to WhereAt / SelectAt / CountAt
        if (methodName is "Where" or "Select" or "Count")
        {
            var lastParam = symbol.Parameters.Length > 0 ? symbol.Parameters[^1] : null;
            if (lastParam?.Type is INamedTypeSymbol { DelegateInvokeMethod.Parameters.Length: 2 })
            {
                methodName += "At";
            }
        }

        var sites = RegisterErrorSites(node, symbol, methodName, ref context);
        if (symbol.ReducedFrom != null && node.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            TranspileReducedCall(node, memberAccess.Expression, symbol, symbol.ReducedFrom, methodName, sites, ref context);
        }
        else
        {
            TranspileStaticCall(node, symbol, methodName, sites, ref context);
        }

        return true;
    }

    // Registers the error sites of a trapping terminal; the ids are appended after its arguments (zero: no site)
    private static (uint First, uint Second) RegisterErrorSites(
        InvocationExpressionSyntax node,
        IMethodSymbol symbol,
        string methodName,
        ref TranspileContext context)
    {
        var table = context.Emitter.ErrorSites;
        if (table == null)
        {
            return (0u, 0u);
        }

        var model = context.Query.SemanticModel;
        var memberName = context.Emitter.CurrentMemberName ?? symbol.Name;
        var hasPredicate = StackLinqHelper.GetStageArgumentCount(symbol) > 0;
        switch (methodName)
        {
            case "ElementAt":
                return (table.Register(node, model, memberName, "Index was out of range.", "ElementAt"), 0u);
            case "First" when hasPredicate:
                return (table.Register(node, model, memberName, "Sequence contains no matching element", "First.Predicate"), 0u);
            case "First":
                return (table.Register(node, model, memberName, "Sequence contains no elements", "First"), 0u);
            case "Last":
            case "Aggregate" when !HasSeedParameter(symbol):
            case "Max":
            case "Min":
            case "Average":
                return (table.Register(node, model, memberName, "Sequence contains no elements", methodName), 0u);
            case "Single" when hasPredicate:
                return (
                    table.Register(node, model, memberName, "Sequence contains more than one matching element", "Single.MoreThanOneMatching"),
                    table.Register(node, model, memberName, "Sequence contains no matching element", "Single.NoMatching")
                );
            case "Single":
                return (
                    table.Register(node, model, memberName, "Sequence contains no elements", "Single.Empty"),
                    table.Register(node, model, memberName, "Sequence contains more than one element", "Single.MoreThanOne")
                );
            case "SingleOrDefault" when hasPredicate:
                return (table.Register(node, model, memberName, "Sequence contains more than one matching element", "SingleOrDefault.MoreThanOneMatching"), 0u);
            case "SingleOrDefault":
                return (table.Register(node, model, memberName, "Sequence contains more than one element", "SingleOrDefault.MoreThanOne"), 0u);
            default:
                return (0u, 0u);
        }
    }

    private static bool HasSeedParameter(IMethodSymbol symbol) =>
        symbol.Parameters
            .AsStackEnumerable()
            .Any(static p => p.Name == "seed");

    // ,<site>u[,<site>u]
    private static void AppendErrorSites((uint First, uint Second) sites, ref TranspileContext context)
    {
        if (sites.First == 0)
        {
            return;
        }

        context.Emitter.Writer.Append($",{sites.First}u");
        if (sites.Second != 0)
        {
            context.Emitter.Writer.Append($",{sites.Second}u");
        }
    }

    private static void TranspileReducedCall(
        InvocationExpressionSyntax node,
        ExpressionSyntax receiver,
        IMethodSymbol symbol,
        IMethodSymbol extension,
        string methodName,
        (uint First, uint Second) sites,
        ref TranspileContext context)
    {
        using var sequence = EvaluationOrder.BeginExtensionCall(
            ref context,
            receiver,
            extension.Parameters[0],
            node.ArgumentList.Arguments,
            symbol);

        AppendMethodName(methodName, ref context);
        using (context.Emitter.Writer.Parentheses())
        {
            AppendSource(receiver, in sequence, ref context);

            var stageArgCount = StackLinqHelper.GetStageArgumentCount(symbol);
            for (var i = 0; i < stageArgCount; i++)
            {
                if (!StackLinqHelper.TryGetStageArgument(node, symbol, i, out var stageArg))
                {
                    throw new InvalidOperationException($"Stage argument {i} was not found.");
                }

                context.Emitter.Writer.Append(',');
                AppendStageArgument(stageArg, symbol.Parameters[i], in sequence, ref context);
            }

            AppendErrorSites(sites, ref context);
        }
    }

    private static void TranspileStaticCall(
        InvocationExpressionSyntax node,
        IMethodSymbol symbol,
        string methodName,
        (uint First, uint Second) sites,
        ref TranspileContext context)
    {
        using var sequence = EvaluationOrder.BeginCall(
            ref context,
            node.ArgumentList.Arguments,
            symbol,
            receiver: null,
            spillAll: false);

        if (!StackLinqHelper.TryFindArgument(node, symbol, 0, out var sourceArg))
        {
            throw new InvalidOperationException("Source argument was not found.");
        }

        AppendMethodName(methodName, ref context);
        using (context.Emitter.Writer.Parentheses())
        {
            AppendArgumentWithStackArray(sourceArg, symbol.Parameters[0], in sequence, ref context);

            var stageArgCount = StackLinqHelper.GetStageArgumentCount(symbol);
            for (var i = 0; i < stageArgCount; i++)
            {
                if (!StackLinqHelper.TryGetStageArgument(node, symbol, i, out var stageArg))
                {
                    throw new InvalidOperationException($"Stage argument {i} was not found.");
                }

                context.Emitter.Writer.Append(',');
                AppendStageArgument(stageArg, symbol.Parameters[i + 1], in sequence, ref context);
            }

            AppendErrorSites(sites, ref context);
        }
    }

    private static void AppendMethodName(string methodName, ref TranspileContext context)
    {
        context.Emitter.Writer.Append("Kassyi::ZeroAlloc::Linq::StackLinq::");
        context.Emitter.Writer.Append(methodName);
    }

    private static void AppendSource(
        ExpressionSyntax receiver,
        in EvaluationOrder.CallSequence sequence,
        ref TranspileContext context)
    {
        if (StackArrayLocals.IsStackArrayReference(receiver, context.Query.SemanticModel))
        {
            context.Emitter.Writer.Append("__gsharp_stackalloc_span(");
            sequence.AppendReceiver(receiver, byReference: false, ref context);
            context.Emitter.Writer.Append(')');
        }
        else
        {
            sequence.AppendReceiver(receiver, byReference: false, ref context);
        }
    }

    private static void AppendStageArgument(
        ArgumentSyntax stageArg,
        IParameterSymbol parameter,
        in EvaluationOrder.CallSequence sequence,
        ref TranspileContext context) =>
        AppendArgumentWithStackArray(stageArg, parameter, in sequence, ref context);

    private static void AppendArgumentWithStackArray(
        ArgumentSyntax argument,
        IParameterSymbol parameter,
        in EvaluationOrder.CallSequence sequence,
        ref TranspileContext context)
    {
        if (StackArrayLocals.IsStackArrayReference(argument.Expression, context.Query.SemanticModel))
        {
            context.Emitter.Writer.Append("__gsharp_stackalloc_span(");
            sequence.AppendArgument(argument, parameter, ref context);
            context.Emitter.Writer.Append(')');
        }
        else
        {
            sequence.AppendArgument(argument, parameter, ref context);
        }
    }
}
