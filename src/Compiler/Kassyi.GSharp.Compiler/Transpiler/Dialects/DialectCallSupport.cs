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
    /// Determines whether <paramref name="invocation"/> resolves to a <c>Gpu.InlinePtx</c> call that is unsupported on
    /// the CPU dialect (<see cref="HostDialects.Cpu"/>).
    /// </summary>
    public static bool IsUnsupportedInlinePtxCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel) =>
        !SupportsInlinePtx(HostDialects.Cpu) &&
        semanticModel.GetSymbol(invocation) is IMethodSymbol method &&
        GpuMacroClassifier.Classify(method).Kind == GpuMacroKind.InlinePtx;

    /// <summary>
    /// Formats the canonical runtime error message for an unsupported <c>Gpu.InlinePtx</c> call on <paramref name="dialectName"/>.
    /// </summary>
    public static string GetInlinePtxUnsupportedMessage(string dialectName) =>
        $"Gpu.InlinePtx is not supported by the {dialectName} dialect";
}
