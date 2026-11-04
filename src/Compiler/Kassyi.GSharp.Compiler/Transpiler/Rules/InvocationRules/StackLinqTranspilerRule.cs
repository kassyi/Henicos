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

        if (symbol.ReducedFrom != null && node.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            TranspileReducedCall(node, memberAccess.Expression, symbol, symbol.ReducedFrom, methodName, ref context);
        }
        else
        {
            TranspileStaticCall(node, symbol, methodName, ref context);
        }

        return true;
    }

    private static void TranspileReducedCall(
        InvocationExpressionSyntax node,
        ExpressionSyntax receiver,
        IMethodSymbol symbol,
        IMethodSymbol extension,
        string methodName,
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
        }
    }

    private static void TranspileStaticCall(
        InvocationExpressionSyntax node,
        IMethodSymbol symbol,
        string methodName,
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
