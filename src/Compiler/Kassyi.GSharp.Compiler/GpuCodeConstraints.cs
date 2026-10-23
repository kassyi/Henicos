namespace Kassyi.GSharp.Compiler;

/// <summary>
/// The G# restrictions on device code, decided in one place: the analyzers report a violation, and the transpiler uses
/// the same answer only to decide whether it can emit the construct.
/// </summary>
/// <remarks>
/// A check written twice drifts; the analyzer and the transpiler then disagree on edge cases (#461). Each member takes
/// the syntax it judges and the semantic model, so that it works on the user's source and on a lowered copy of it.
/// </remarks>
internal static class GpuCodeConstraints
{
    /// <summary>Whether a GPU function returns <see langword="void"/> or an unmanaged type (GSH0004).</summary>
    public static bool HasSupportedReturnType(IMethodSymbol method) =>
        method.ReturnType.SpecialType == SpecialType.System_Void || method.ReturnType.IsUnmanagedType;

    /// <summary>The return type of <paramref name="method"/> as the GSH0004 message names it.</summary>
    public static string FormatReturnType(IMethodSymbol method) =>
#pragma warning disable RS0030 // Message text only (not a hot path); keeps the C# spelling of the type.
        method.ReturnType.ToDisplayString();
#pragma warning restore RS0030

    /// <summary>Whether <paramref name="method"/> is a <c>[GpuDeviceFunction]</c> that is not static (GSH0066).</summary>
    public static bool IsInstanceDeviceFunction(IMethodSymbol method) =>
        !method.IsStatic && method.HasAttribute(KnownAttributes.GpuDeviceFunction);

    /// <summary>
    /// Whether device code cannot hold a value of <paramref name="type"/> because it is a function pointer, which points into
    /// host code (GSH0003).
    /// </summary>
    public static bool IsFunctionPointer(ITypeSymbol? type) => type is IFunctionPointerTypeSymbol;

    /// <summary>
    /// Whether the argument of <c>Gpu.InlinePtx</c> is text known at compile time: a constant string or an interpolated
    /// string (GSH0006).
    /// </summary>
    public static bool IsInlinePtxText(ExpressionSyntax argument, SemanticModel semanticModel) =>
        argument is InterpolatedStringExpressionSyntax ||
        semanticModel.GetSafeConstantValue(argument) is { HasValue: true, Value: string };

    /// <summary>The factor of <c>Gpu.Hint.Unroll(factor)</c>, when it is a non-negative <see cref="int"/> constant (GSH0016).</summary>
    public static bool TryGetUnrollFactor(ExpressionSyntax factor, SemanticModel semanticModel, out int value)
    {
        if (semanticModel.GetSafeConstantValue(factor) is { HasValue: true, Value: int constant } && constant >= 0)
        {
            value = constant;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>
    /// The loop that the <c>Gpu.Hint.Unroll</c> statement <paramref name="hint"/> applies to: the next statement of the same
    /// block or switch section, when it is a <c>for</c>, <c>while</c> or <c>do</c> loop (GSH0017).
    /// </summary>
    public static StatementSyntax? GetUnrolledLoop(ExpressionStatementSyntax hint)
    {
        SyntaxList<StatementSyntax> statements;
        switch (hint.Parent)
        {
            case BlockSyntax block:
                statements = block.Statements;
                break;
            case SwitchSectionSyntax switchSection:
                statements = switchSection.Statements;
                break;
            default:
                return null;
        }

        var index = statements.IndexOf(hint);
        if (index < 0 || index + 1 >= statements.Count)
        {
            return null;
        }

        var nextStmt = statements[index + 1];
        return nextStmt is ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax ? nextStmt : null;
    }

    /// <summary>
    /// The body of a <c>Gpu.SyncGrid(body)</c> call, when it is written as the call's one argument and that argument is a
    /// lambda (GSH0003).
    /// </summary>
    public static bool TryGetSyncGridBody(InvocationExpressionSyntax invocation, [NotNullWhen(true)] out LambdaExpressionSyntax? body)
    {
        var args = invocation.ArgumentList.Arguments;
        body = args.Count == 1 ? args[0].Expression as LambdaExpressionSyntax : null;
        return body != null;
    }
}
