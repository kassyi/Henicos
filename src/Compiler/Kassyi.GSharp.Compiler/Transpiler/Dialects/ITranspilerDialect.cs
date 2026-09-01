namespace Kassyi.GSharp.Compiler.Transpiler.Dialects;

/// <summary>
/// Defines a target environment dialect for the GSharp transpiler (e.g. CUDA, UE5, WebAssembly).
/// </summary>
public interface ITranspilerDialect
{
    /// <summary>Headers required for the generated C++ file.</summary>
    IEnumerable<string> RequiredHeaders { get; }

    /// <summary>Gets a value indicating whether this dialect targets CUDA, enabling GPU execution tests.</summary>
    bool IsCuda { get; }

    /// <summary>Gets the capabilities (features) supported by this dialect.</summary>
    DialectCapabilities Capabilities { get; }

    /// <summary>Prefix applied to all generated struct definitions (e.g. `__device__ ` for CUDA).</summary>
    string StructPrefix { get; }

    /// <summary>Prefix applied to all generated methods (e.g. `__device__ inline ` for CUDA).</summary>
    string MethodPrefix { get; }

    /// <summary>
    /// Appends the C++ type corresponding to a C# ITypeSymbol to the source writer.
    /// </summary>
    void AppendType(ref TranspileContext context, ITypeSymbol? typeSymbol);

    /// <summary>
    /// Appends the C++ parameter type, correctly handling pointers, references, and restrict semantics.
    /// </summary>
    (bool IsRefPointer, bool IsPointerType, bool HasRestrict) AppendParameter(ref TranspileContext context, ParameterSyntax p, ITypeSymbol? typeSymbol);

    /// <summary>
    /// Gets the approximate unmanaged size in bytes for a type, respecting target alignment rules.
    /// </summary>
    int GetApproximateSize(ITypeSymbol typeSymbol);

    /// <summary>
    /// Attempts to map a C# method invocation to a target-specific C++ expression or intrinsic.
    /// </summary>
    bool TryMapIntrinsic(ISymbol symbol, out string mappedExpression);

    /// <summary>
    /// Returns a rewriter to mutate the C# AST prior to transpilation (e.g. `foreach` -> `for` lowering), or null.
    /// </summary>
    CSharpSyntaxRewriter? GetAstPreProcessor(SemanticModel semanticModel);

    /// <summary>
    /// Returns a visitor capable of intercepting and handling C# nodes for custom code generation.
    /// Returns true if the node was successfully handled and normal transpilation should be skipped.
    /// </summary>
    CSharpSyntaxVisitor<bool> GetInterceptorVisitor(ref TranspileContext context);
}
