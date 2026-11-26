namespace Kassyi.GSharp.Compiler.Dialects;

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
    /// Attempts to map a C# type to its dialect-defined C++ type name by fully qualified metadata name.
    /// </summary>
    /// <param name="fullyQualifiedName">The fully qualified C# type name (without <c>global::</c>).</param>
    /// <param name="mappedType">When this method returns <see langword="true"/>, contains the target C++ type name.</param>
    /// <returns><see langword="true"/> if the dialect maps the type; otherwise, <see langword="false"/>.</returns>
    bool TryMapType(string fullyQualifiedName, out string mappedType);

    /// <summary>
    /// Attempts to map a Roslyn type symbol to its dialect-defined C++ type name.
    /// </summary>
    /// <param name="typeSymbol">The Roslyn type symbol to look up.</param>
    /// <param name="mappedType">When this method returns <see langword="true"/>, contains the target C++ type name.</param>
    /// <returns><see langword="true"/> if the dialect maps the type; otherwise, <see langword="false"/>.</returns>
    bool TryMapType(ITypeSymbol typeSymbol, out string mappedType);

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
    /// Determines whether <paramref name="typeSymbol"/> is emitted as a dialect-defined type (for
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

