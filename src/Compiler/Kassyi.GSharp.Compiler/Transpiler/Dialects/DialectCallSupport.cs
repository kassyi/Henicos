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

    /// <summary>
    /// Whether <paramref name="dialect"/> implements the <c>[CudaIntrinsic]</c> method <paramref name="method"/>: its
    /// attribute does not name the dialect in <c>UnsupportedDialects</c> (the <c>unsupported</c> column of the
    /// native API YAML), so the dialect's prelude, the CUDA toolkit or the lowering defines the native name.
    /// </summary>
    public static bool ImplementsIntrinsic(IMethodSymbol method, ITranspilerDialect dialect)
    {
        if (method.GetAttribute(KnownAttributes.CudaIntrinsic) is not { } attribute)
        {
            return true;
        }

        var name = dialect.IsCuda
            ? NativeApi.ApiMethodDefinition.CudaDialect
            : NativeApi.ApiMethodDefinition.CpuDialect;
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument is { Key: UnsupportedDialectsArgument, Value.Kind: TypedConstantKind.Array } &&
                argument.Value.Values
                    .AsStackEnumerable()
                    .Any(name, static (value, n) => value.Value as string == n))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The text of the runtime failure that replaces a call of <paramref name="method"/>, which
    /// <paramref name="dialectName"/> does not implement (<see cref="ImplementsIntrinsic"/>).
    /// </summary>
    public static string GetNotImplementedMessage(string method, string dialectName) =>
        $"{method} is not implemented by the {dialectName} dialect";

    private const string UnsupportedDialectsArgument = "UnsupportedDialects";
}
