namespace Kassyi.GSharp.Compiler.Transpiler.Dialects;

/// <summary>
/// Defines the contract for an execution target dialect in the GSharp transpiler (e.g., NVIDIA CUDA, Unreal Engine 5 C++, WebAssembly).
/// </summary>
/// <remarks>
/// <para>
/// Implementations of this interface customize how C# symbols, types, statements, and expressions
/// are mapped to the target language and execution environment.
/// </para>
/// <para><b>Thread Safety:</b> Implementations must be immutable or stateless, ensuring safe concurrent usage across multiple compilation threads.</para>
/// <para><b>Execution Target:</b> Host-only (compiler-time analysis and emission). Not executed within GPU kernels.</para>
/// </remarks>
public interface ITranspilerDialect
{
    /// <summary>
    /// Gets the name of the dialect as shown in diagnostics (e.g., <c>StandardCppDialect</c>).
    /// </summary>
    string Name
    {
        get;
    }

    /// <summary>
    /// Gets a value indicating whether this dialect targets NVIDIA CUDA for GPU execution.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if the dialect emits CUDA C++ (<c>.cu</c>) kernels; otherwise, <see langword="false"/>.
    /// </value>
    bool IsCuda
    {
        get;
    }

    /// <summary>
    /// Gets the bitwise flag collection of hardware and language features supported by this dialect.
    /// </summary>
    /// <value>
    /// A <see cref="DialectCapabilities"/> value indicating supported runtime features.
    /// </value>
    DialectCapabilities Capabilities
    {
        get;
    }

    /// <summary>
    /// Emits the target C++ type representation for the specified C# Roslyn type symbol.
    /// </summary>
    /// <param name="context">The active transpilation context used for source output and symbol lookup.</param>
    /// <param name="typeSymbol">The Roslyn type symbol to convert, or <see langword="null"/> for unresolved or void types.</param>
    /// <remarks>
    /// <para><b>Performance:</b> Emits directly into the output stream without intermediate heap string allocations.</para>
    /// </remarks>
    void AppendType(ref TranspileContext context, ITypeSymbol? typeSymbol);

    /// <summary>
    /// Emits the target C++ parameter type and modifier attributes, resolving pointer, reference, and alias semantics.
    /// </summary>
    /// <param name="context">The active transpilation context used for source output.</param>
    /// <param name="p">The syntax node defining the method parameter.</param>
    /// <param name="typeSymbol">The resolved semantic type symbol for the parameter, or <see langword="null"/> if unresolvable.</param>
    /// <param name="parameterSymbol">The resolved parameter symbol, or <see langword="null"/> if unresolvable.</param>
    /// <returns>
    /// A tuple containing:
    /// <list type="bullet">
    /// <item><term><c>IsRefPointer</c></term><description><see langword="true"/> if the parameter is passed by reference as a pointer.</description></item>
    /// <item><term><c>IsPointerType</c></term><description><see langword="true"/> if the parameter represents an unmanaged raw pointer.</description></item>
    /// <item><term><c>HasRestrict</c></term><description><see langword="true"/> if this method has already emitted the C++ <c>__restrict__</c> qualifier for the parameter.</description></item>
    /// </list>
    /// </returns>
    (bool IsRefPointer, bool IsPointerType, bool HasRestrict) AppendParameter(ref TranspileContext context, ParameterSyntax p, ITypeSymbol? typeSymbol, IParameterSymbol? parameterSymbol);

    /// <summary>
    /// Attempts to map an invocation of a C# method to a target-specific intrinsic or macro expression.
    /// </summary>
    /// <param name="symbol">The Roslyn method or member symbol being invoked.</param>
    /// <param name="mappedExpression">When this method returns, contains the target intrinsic expression if found; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if a valid intrinsic mapping exists for the given symbol; otherwise, <see langword="false"/>.</returns>
    bool TryMapIntrinsic(ISymbol symbol, out string mappedExpression);

    /// <summary>
    /// Attempts to map a member access expression (e.g. property or field) for a specific type into a target representation.
    /// </summary>
    /// <param name="typeSymbol">The type of the expression being accessed.</param>
    /// <param name="memberName">The name of the member being accessed.</param>
    /// <param name="mappedName">When this method returns true, contains the target member name or method invocation string.</param>
    /// <param name="isMethodCall">When this method returns true, indicates whether the mapped access should be appended as a method call (e.g. size()) rather than a field access.</param>
    /// <returns><see langword="true"/> if a valid mapping exists; otherwise, <see langword="false"/>.</returns>
    bool TryMapMemberAccess(ITypeSymbol typeSymbol, string memberName, out string? mappedName, out bool isMethodCall);

    /// <summary>
    /// Determines whether <see cref="AppendType"/> emits <paramref name="typeSymbol"/> as a dialect-defined type (for
    /// example <c>float2</c>) rather than as a struct that G# defines itself.
    /// </summary>
    /// <param name="typeSymbol">The type to check.</param>
    /// <returns><see langword="true"/> if the dialect maps the type; otherwise, <see langword="false"/>.</returns>
    bool IsMappedType(ITypeSymbol typeSymbol);

    /// <summary>Gets the byte size and alignment of <c>GSharpArray&lt;T&gt;</c> on the target device.</summary>
    TypeLayout ArrayViewLayout
    {
        get;
    }

    /// <summary>Gets the byte size and alignment of a pointer on the target device.</summary>
    TypeLayout PointerLayout
    {
        get;
    }

    /// <summary>
    /// Attempts to get the byte size and alignment of a dialect-mapped type (such as primitives or vector types).
    /// </summary>
    bool TryGetTypeLayout(ITypeSymbol typeSymbol, out TypeLayout layout);
}

