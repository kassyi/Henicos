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
    /// Gets the collection of C/C++ header include directives required by the emitted code.
    /// </summary>
    /// <value>
    /// An enumerable of header include paths (e.g., <c>&lt;cuda_runtime.h&gt;</c>).
    /// </value>
    IEnumerable<string> RequiredHeaders
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
    /// Gets the language prefix prepended to all emitted struct definitions.
    /// </summary>
    /// <value>
    /// The prefix string (e.g., <c>"__device__ "</c> in CUDA, or empty string in host C++).
    /// </value>
    string StructPrefix
    {
        get;
    }

    /// <summary>
    /// Gets the language prefix prepended to all emitted function and method definitions.
    /// </summary>
    /// <value>
    /// The prefix string (e.g., <c>"__device__ inline "</c> in CUDA).
    /// </value>
    string MethodPrefix
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
    /// <item><term><c>HasRestrict</c></term><description><see langword="true"/> if the parameter is decorated with the C++ <c>__restrict__</c> keyword.</description></item>
    /// </list>
    /// </returns>
    (bool IsRefPointer, bool IsPointerType, bool HasRestrict) AppendParameter(ref TranspileContext context, ParameterSyntax p, ITypeSymbol? typeSymbol, IParameterSymbol? parameterSymbol);

    /// <summary>
    /// Computes the approximate unmanaged memory footprint in bytes for the specified type according to the target's alignment rules.
    /// </summary>
    /// <param name="typeSymbol">The Roslyn type symbol to measure.</param>
    /// <returns>The unmanaged size of the type in bytes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="typeSymbol"/> is <see langword="null"/>.</exception>
    int GetApproximateSize(ITypeSymbol typeSymbol);

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

    /// <summary>
    /// Retrieves an AST rewriter to perform dialect-specific syntax lowering (e.g., desugaring <c>foreach</c> loops into <c>for</c> loops) prior to emission.
    /// </summary>
    /// <param name="semanticModel">The semantic model corresponding to the syntax tree being transformed.</param>
    /// <returns>A configured <see cref="CSharpSyntaxRewriter"/> instance, or <see langword="null"/> if no preprocessing is required.</returns>
    CSharpSyntaxRewriter? GetAstPreProcessor(SemanticModel semanticModel);

    /// <summary>
    /// Retrieves a specialized syntax visitor capable of intercepting and custom-transpiling specific syntax nodes.
    /// </summary>
    /// <param name="context">The active transpilation context.</param>
    /// <returns>A visitor returning <see langword="true"/> when a node was handled customly and default transpilation should be skipped.</returns>
    CSharpSyntaxVisitor<bool> GetInterceptorVisitor(ref TranspileContext context);
}

