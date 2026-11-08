namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.Wmma;

/// <summary>
/// Checks the arguments of the <c>Wmma</c> operations: fragments must be variables, the memory of <c>Load</c> and
/// <c>Store</c> must be global or shared, and <c>MultiplyAccumulate</c> must use a supported shape and element pair.
/// </summary>
internal static class WmmaCallArguments
{
    private const string LoadSourceReason =
        "the source must refer to global or shared memory (an array, Span, or SharedMemory<T> element, " +
        "or a ref/in parameter)";

    private const string StoreDestinationReason =
        "the destination must refer to global or shared memory (an array, Span, or SharedMemory<T> element, " +
        "or a ref parameter)";

    /// <summary>Reports the arguments of a call to a <c>Wmma</c> method that cannot be emitted.</summary>
    public static void Analyze(InvocationExpressionSyntax node, IMethodSymbol method, ref ValidationContext context)
    {
        var arguments = node.ArgumentList.Arguments;

        switch (method.Name)
        {
            case TypeConstants.WmmaTypes.MultiplyAccumulate:
                AnalyzeMultiplyAccumulate(node, method, arguments, ref context);
                break;
            case TypeConstants.WmmaTypes.Load when arguments.Count >= 2:
                AnalyzeLoad(arguments, ref context);
                break;
            case TypeConstants.WmmaTypes.Store when arguments.Count >= 2:
                AnalyzeStore(arguments, ref context);
                break;
            case TypeConstants.WmmaTypes.Fill when arguments.Count >= 1:
                ReportIfNotVariable(
                    FindArgument(arguments, "fragment", 0),
                    "Wmma.Fill",
                    "the fragment must be a variable, not a temporary value",
                    ref context
                );
                break;
        }
    }

    private static void AnalyzeMultiplyAccumulate(
        InvocationExpressionSyntax node,
        IMethodSymbol method,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        ref ValidationContext context)
    {
        const string Operation = "Wmma.MultiplyAccumulate";

        if (method.TypeArguments.Length >= 5 &&
            !WmmaFragmentTypes.IsSupportedMultiplyAccumulate(
                method.TypeArguments[0],
                method.TypeArguments[1],
                method.TypeArguments[4],
                out var reason
            ))
        {
            context.ReportDiagnostic(GSharpDiagnostics.UnsupportedWarpMatrixOperation, node, Operation, reason);
        }

        foreach (var argument in arguments)
        {
            ReportIfNotVariable(
                argument,
                Operation,
                "fragment arguments must be variables, not temporary values",
                ref context
            );
        }
    }

    private static void AnalyzeLoad(SeparatedSyntaxList<ArgumentSyntax> arguments, ref ValidationContext context)
    {
        const string Operation = "Wmma.Load";

        var fragment = FindArgument(arguments, "fragment", 0);
        var source = FindArgument(arguments, "source", 1);

        ReportIfNotVariable(
            fragment,
            Operation,
            "the destination fragment must be a variable, not a temporary value",
            ref context
        );
        ReportIfNotDeviceMemory(source, Operation, LoadSourceReason, ref context);
    }

    private static void AnalyzeStore(SeparatedSyntaxList<ArgumentSyntax> arguments, ref ValidationContext context)
    {
        const string Operation = "Wmma.Store";

        var destination = FindArgument(arguments, "destination", 0);
        var fragment = FindArgument(arguments, "fragment", 1);

        ReportIfNotDeviceMemory(destination, Operation, StoreDestinationReason, ref context);
        ReportIfNotVariable(
            fragment,
            Operation,
            "the source fragment must be a variable, not a temporary value",
            ref context
        );
    }

    private static void ReportIfNotVariable(
        ArgumentSyntax? argument,
        string operation,
        string reason,
        ref ValidationContext context)
    {
        if (argument is not null && !RefSyntaxHelper.IsCppLValue(argument.Expression, context.SemanticModel))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.UnsupportedWarpMatrixOperation,
                argument.Expression,
                operation,
                reason
            );
        }
    }

    private static void ReportIfNotDeviceMemory(
        ArgumentSyntax? argument,
        string operation,
        string reason,
        ref ValidationContext context)
    {
        if (argument is not null && !IsGlobalOrSharedMemoryElement(argument.Expression, context.SemanticModel))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.UnsupportedWarpMatrixOperation,
                argument.Expression,
                operation,
                reason
            );
        }
    }

    private static bool IsGlobalOrSharedMemoryElement(ExpressionSyntax expression, SemanticModel semanticModel) =>
        RefSyntaxHelper.IsCppLValue(expression, semanticModel) &&
        (CopyAsyncRule.IsGlobalMemorySourceLocation(expression, semanticModel) ||
         CopyAsyncRule.IsSharedMemoryLocation(expression, semanticModel));

    // The argument named parameterName, or else the positional argument at positionalIndex.
    private static ArgumentSyntax? FindArgument(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        string parameterName,
        int positionalIndex)
    {
        var named = arguments
            .AsStackEnumerable()
            .FirstOrDefault(
                parameterName,
                static (argument, name) => argument.NameColon is { Name.Identifier.ValueText: var argumentName } &&
                                           argumentName == name
            );
        if (named is not null)
        {
            return named;
        }

        return positionalIndex < arguments.Count && arguments[positionalIndex].NameColon is null
            ? arguments[positionalIndex]
            : null;
    }
}
