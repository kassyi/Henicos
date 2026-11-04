namespace Kassyi.GSharp.Compiler.Transpiler.Dialects;

/// <summary>
/// Centralizes per-dialect call and feature support checks across the analyzer, C++/CUDA transpiler, and managed rewriter.
/// </summary>
internal static class DialectCallSupport
{
    /// <summary>
    /// Determines whether <paramref name="dialect"/> supports <c>Gpu.InlinePtx</c> inline assembly.
    /// </summary>
    public static bool SupportsInlinePtx(ITranspilerDialect dialect) =>
        dialect.Capabilities.HasFlag(DialectCapabilities.InlineAssembly);

    /// <summary>
    /// Determines whether <paramref name="invocation"/> resolves to a <c>Gpu.InlinePtx</c> call.
    /// </summary>
    public static bool IsInlinePtxCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel) =>
        semanticModel.GetSymbol(invocation) is IMethodSymbol method &&
        GpuMacroClassifier.Classify(method).Kind == GpuMacroKind.InlinePtx;

    /// <summary>
    /// Determines whether <paramref name="invocation"/> resolves to a <c>Gpu.InlinePtx</c> call that is unsupported on
    /// <paramref name="dialect"/>.
    /// </summary>
    public static bool IsUnsupportedInlinePtxCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ITranspilerDialect dialect) =>
        !SupportsInlinePtx(dialect) &&
        IsInlinePtxCall(invocation, semanticModel);

    /// <summary>
    /// Formats the canonical runtime error message for an unsupported <c>Gpu.InlinePtx</c> call on <paramref name="dialectName"/>.
    /// </summary>
    public static string GetInlinePtxUnsupportedMessage(string dialectName) =>
        $"Gpu.InlinePtx is not supported by the {dialectName} dialect";
}
