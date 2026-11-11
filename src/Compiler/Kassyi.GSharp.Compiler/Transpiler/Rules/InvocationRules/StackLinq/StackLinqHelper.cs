using static Kassyi.GSharp.Compiler.GSharpInternalNames.StackLinq;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

internal static class StackLinqHelper
{
    public static bool IsStackLinqMethod(IMethodSymbol symbol) =>
        symbol.ContainingNamespace is
        {
            Name: "Linq",
            ContainingNamespace: { Name: "ZeroAlloc", ContainingNamespace.Name: "Kassyi" }
        };

    public static bool IsStackLinqType(ITypeSymbol? type) =>
        type?.ContainingNamespace is
        {
            Name: "Linq",
            ContainingNamespace: { Name: "ZeroAlloc", ContainingNamespace.Name: "Kassyi" }
        };

    /// <summary>
    /// Is it a StackLinq operator that runs on the host only? That is every overload passing a state value to its lambda
    /// (<c>Any(state, static (x, s) => ...)</c>; a kernel lambda reads outer values directly), and every
    /// operator in <c>StackLinqExtensions.HostOnly.cs</c>, plus <c>StackEnumerable.OfType</c>. The kernel transpiler
    /// rules have no strategy for them.
    /// </summary>
    public static bool IsHostOnly(IMethodSymbol symbol) =>
        IsStackLinqMethod(symbol) &&
        (symbol.Name is "AddTo" or "AddIf" or "Do" or "DoIf" or "SelectMany" or "TryFirst" or "IndexOf" or "OfType" ||
         symbol.Parameters
             .AsStackEnumerable()
             .Any(static p => p.Name == "state"));

    /// <summary>
    /// Is it the overload of <c>Where</c>, <c>Select</c>, <c>Count</c>, <c>TakeWhile</c> or <c>SkipWhile</c> whose lambda
    /// also takes the index of the element (<c>Where((x, i) => ...)</c>)? The device header has an <c>...At</c> function
    /// for each; a fused loop does not pass the index.
    /// </summary>
    public static bool IsIndexedOverload(IMethodSymbol symbol) =>
        symbol.Name is "Where" or "Select" or "Count" or "TakeWhile" or "SkipWhile" &&
        symbol.Parameters.Length > 0 &&
        symbol.Parameters[^1].Type is INamedTypeSymbol { DelegateInvokeMethod.Parameters.Length: 2 };

    /// <summary>
    /// The failure of an operator that needs an operation of a struct element type that GPU code does not call: a
    /// <c>Sum</c> of elements that are not of a built-in arithmetic type (the generic <c>Sum</c> of an <c>AsParallel()</c>
    /// sequence adds with the user-defined <c>+</c> of the type, GSH0060, from its <c>AdditiveIdentity</c>), and a
    /// <c>Contains</c> of a struct (C# compares with <c>EqualityComparer&lt;T&gt;.Default</c>, an <c>Equals</c> that the
    /// device does not reproduce).
    /// </summary>
    public static bool TryGetUnsupportedElementType(IMethodSymbol symbol, [NotNullWhen(true)] out string? failure)
    {
        switch (symbol.Name)
        {
            case "Sum" or "SumSequential" when !symbol.ReturnType.SpecialType.IsArithmetic():
                failure =
                    $"Sum over '{IteratorTypeResolver.FormatType(symbol.ReturnType)}' " +
                    "(only built-in numeric types are summed in GPU code; " +
                    "use Aggregate with a [GpuDeviceFunction] that adds two values)";
                return true;
            case "Contains" when symbol.Parameters[^1].Type is var value &&
                                 !value.SpecialType.IsPrimitiveValue() && value.TypeKind != TypeKind.Enum:
                failure =
                    $"Contains over '{IteratorTypeResolver.FormatType(value)}' " +
                    "(only built-in types and enums are compared in GPU code; " +
                    "use Any with a [GpuDeviceFunction] that compares two values)";
                return true;
            default:
                failure = null;
                return false;
        }
    }

    public static bool TryFindArgument(
        InvocationExpressionSyntax invocation,
        IMethodSymbol symbol,
        int targetOrdinal,
        [NotNullWhen(true)] out ArgumentSyntax? argument)
    {
        var args = invocation.ArgumentList.Arguments;
        for (var i = 0; i < args.Count; i++)
        {
            var param = symbol.FindParameter(args, i);
            if (param is not null && param.Ordinal == targetOrdinal)
            {
                argument = args[i];
                return true;
            }
        }

        argument = null;
        return false;
    }

    public static bool TryGetSourceExpression(
        InvocationExpressionSyntax invocation,
        IMethodSymbol symbol,
        [NotNullWhen(true)] out ExpressionSyntax? sourceExpression)
    {
        if (symbol.ReducedFrom is not null)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                sourceExpression = memberAccess.Expression;
                return true;
            }

            sourceExpression = null;
            return false;
        }

        if (TryFindArgument(invocation, symbol, 0, out var sourceArg))
        {
            sourceExpression = sourceArg.Expression;
            return true;
        }

        sourceExpression = null;
        return false;
    }

    public static int GetStageArgumentCount(IMethodSymbol symbol) =>
        symbol.ReducedFrom is not null ? symbol.Parameters.Length : symbol.Parameters.Length - 1;

    public static bool TryGetStageArgument(
        InvocationExpressionSyntax invocation,
        IMethodSymbol symbol,
        int stageArgOrdinal,
        [NotNullWhen(true)] out ArgumentSyntax? argument)
    {
        var targetOrdinal = (symbol.ReducedFrom is not null ? 0 : 1) + stageArgOrdinal;
        if (targetOrdinal < 0 || targetOrdinal >= symbol.Parameters.Length)
        {
            argument = null;
            return false;
        }

        return TryFindArgument(invocation, symbol, targetOrdinal, out argument);
    }

    public static bool HasInterferingReorderedArguments(
        InvocationExpressionSyntax invocation,
        IMethodSymbol symbol,
        SemanticModel semanticModel)
    {
        var args = invocation.ArgumentList.Arguments;
        for (var i = 0; i < args.Count; i++)
        {
            var paramI = symbol.FindParameter(args, i);
            if (paramI is null)
            {
                continue;
            }

            for (var j = i + 1; j < args.Count; j++)
            {
                var paramJ = symbol.FindParameter(args, j);
                if (paramJ is not null &&
                    paramI.Ordinal > paramJ.Ordinal &&
                    ExpressionEffects.Of(args[i].Expression, semanticModel)
                        .InterferesWith(ExpressionEffects.Of(args[j].Expression, semanticModel)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static ArgumentSyntax GetLambdaArgument(InvocationExpressionSyntax invocation, IMethodSymbol symbol) =>
        TryGetStageArgument(invocation, symbol, 0, out var arg)
            ? arg
            : throw new InvalidOperationException("Lambda argument was not found.");

    public static bool HasPredicateOrSelector(InvocationExpressionSyntax invocation, IMethodSymbol symbol) =>
        GetStageArgumentCount(symbol) > 0;

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
