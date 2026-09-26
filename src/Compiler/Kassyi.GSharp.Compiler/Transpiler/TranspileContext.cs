namespace Kassyi.GSharp.Compiler.Transpiler;

using Kassyi.GSharp.Compiler.Transpiler.Rules;

/// <summary>
/// Encapsulates code generation writers, symbol caches, semantic models, and diagnostic reporters during AST transpilation.
/// </summary>
/// <param name="writer">The active <see cref="SourceWriter"/> used to emit generated source code.</param>
/// <param name="usedStructs">A set tracking all named struct symbols referenced in the transpiled unit for dependency ordering.</param>
/// <param name="localVariableOverrides">A dictionary mapping variable identifiers to macro expansion replacement values.</param>
/// <param name="semanticModel">The Roslyn <see cref="Microsoft.CodeAnalysis.SemanticModel"/> used for symbol resolution.</param>
/// <param name="reportDiagnostic">An optional callback delegate used to report compiler diagnostics and warnings.</param>
/// <param name="visitor">The active syntax walker driving code generation.</param>
/// <param name="bclIntrinsicMap">A lookup map associating BCL method symbols with their target dialect intrinsic equivalents.</param>
/// <param name="dialect">The target language and hardware dialect implementation.</param>
/// <remarks>
/// <para><b>Performance:</b> Designed as a mutable value type (<see langword="struct"/>) to be passed by reference (<see langword="ref"/>), eliminating heap allocations during recursive syntax tree walking.</para>
/// <para><b>Thread Safety:</b> Not thread-safe. Each thread or compilation task must instantiate and use its own context instance.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment). Cannot be executed within GPU kernels.</para>
/// </remarks>
public struct TranspileContext(
    SourceWriter writer,
    HashSet<INamedTypeSymbol> usedStructs,
    List<(string Name, OverrideValue Value)> localVariableOverrides,
    SemanticModel semanticModel,
    Action<Diagnostic>? reportDiagnostic,
    RoslynToCudaVisitor visitor,
    ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo> bclIntrinsicMap,
    ITranspilerDialect dialect,
    ITranspilerRuleRegistry ruleRegistry)
{
    /// <summary>
    /// The active source writer receiving emitted target code.
    /// </summary>
    public SourceWriter Writer = writer;

    /// <summary>
    /// The collection of struct symbols referenced during transpilation, used for generating forward declarations.
    /// </summary>
    public HashSet<INamedTypeSymbol> UsedStructs = usedStructs;

    /// <summary>
    /// The stack of identifier substitutions applied during macro and intrinsic expansion.
    /// Searched in reverse order so inner scopes shadow outer scopes.
    /// </summary>
    public List<(string Name, OverrideValue Value)> LocalVariableOverrides = localVariableOverrides;

    /// <summary>
    /// Pushes an override value into the active scope and returns a state token for later restoration.
    /// </summary>
    public int PushOverrideScope(string name, OverrideValue value)
    {
        int cookie = LocalVariableOverrides.Count;
        LocalVariableOverrides.Add((name, value));
        return cookie;
    }

    /// <summary>
    /// Restores the override scope to the specified state token.
    /// </summary>
    public void PopOverrideScope(int cookie)
    {
        int removeCount = LocalVariableOverrides.Count - cookie;
        if (removeCount > 0)
        {
            LocalVariableOverrides.RemoveRange(cookie, removeCount);
        }
    }

    /// <summary>
    /// Attempts to resolve a local variable override by name, searching from the innermost scope outwards.
    /// </summary>
    public bool TryGetOverride(string name, out OverrideValue value)
    {
        for (int i = LocalVariableOverrides.Count - 1; i >= 0; i--)
        {
            if (LocalVariableOverrides[i].Name == name)
            {
                value = LocalVariableOverrides[i].Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Indicates whether the dynamic shared memory base declaration (<c>extern __shared__</c>) has already been emitted in the current kernel.
    /// </summary>
    public bool HasEmittedDynamicSmemBase = false;

    /// <summary>
    /// Indicates whether the <c>[SkipLocalsInit]</c> attribute is applied to the current method or containing scope, suppressing zero-initialization of local variables.
    /// </summary>
    public bool SkipLocalsInit = false;

    /// <summary>
    /// Indicates whether the current method being transpiled is a GpuKernel entry point.
    /// </summary>
    public bool IsGpuKernelEntry = false;

    /// <summary>
    /// Indicates whether the current method being transpiled has a void return type.
    /// </summary>
    public bool IsVoidReturn = false;

    /// <summary>
    /// Tracks the depth of nested callables (lambdas, local functions). 0 means the root kernel method.
    /// </summary>
    public int CallableDepth = 0;

    /// <summary>
    /// Tracks the depth of active Gpu.SyncGrid macro scopes to automatically inject grid synchronization barriers between statements.
    /// </summary>
    public int AutoSyncGridDepth = 0;

    /// <summary>
    /// Gets the Roslyn semantic model associated with the syntax tree being transpiled.
    /// </summary>
    public SemanticModel SemanticModel { get; } = semanticModel;

    /// <summary>
    /// Gets the callback delegate used to emit compiler warnings and error diagnostics.
    /// </summary>
    public Action<Diagnostic>? ReportDiagnostic { get; } = reportDiagnostic;

    /// <summary>
    /// Gets the immutable mapping dictionary between BCL method symbols and target dialect intrinsics.
    /// </summary>
    public ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo> BclIntrinsicMap { get; } = bclIntrinsicMap;

    /// <summary>
    /// Gets the active target language dialect controlling code emission and keyword formatting.
    /// </summary>
    public ITranspilerDialect Dialect { get; } = dialect;

    /// <summary>
    /// Gets the parent syntax walker executing AST visitation.
    /// </summary>
    public RoslynToCudaVisitor Visitor { get; } = visitor;

    /// <summary>
    /// Gets the registry used to resolve syntax node transpiler rules.
    /// </summary>
    public ITranspilerRuleRegistry RuleRegistry { get; } = ruleRegistry;

    /// <summary>
    /// Appends the C++ type corresponding to the specified C# type symbol directly into the active source writer.
    /// </summary>
    /// <param name="typeSymbol">The Roslyn type symbol to resolve and emit.</param>
    public void AppendType(ITypeSymbol? typeSymbol) => Dialect.AppendType(ref this, typeSymbol);

    /// <summary>
    /// Appends the C++ parameter type and modifiers for a given parameter syntax and symbol.
    /// </summary>
    /// <param name="p">The syntax node defining the parameter.</param>
    /// <param name="typeSymbol">The resolved semantic type symbol for the parameter.</param>
    /// <returns>A tuple detailing reference, pointer, and restrict qualifiers emitted.</returns>
    public (bool IsRefPointer, bool IsPointerType, bool HasRestrict) AppendParameterCppType(ParameterSyntax p,
        ITypeSymbol? typeSymbol) => Dialect.AppendParameter(ref this, p, typeSymbol);
}

/// <summary>
/// Represents a replacement substitution value (literal string snippet or Roslyn syntax node) used during macro expansion.
/// </summary>
/// <remarks>
/// <para><b>Performance:</b> Blittable readonly value type to ensure zero-allocation storage in dictionaries.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public readonly struct OverrideValue
{
    /// <summary>
    /// Gets the raw string replacement snippet, if initialized with a string; otherwise, <see langword="null"/>.
    /// </summary>
    public readonly string? StringValue;

    /// <summary>
    /// Gets the Roslyn syntax node replacement, if initialized with an AST node; otherwise, <see langword="null"/>.
    /// </summary>
    public readonly SyntaxNode? NodeValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="OverrideValue"/> struct with a literal string substitution.
    /// </summary>
    /// <param name="stringValue">The replacement source string to emit.</param>
    public OverrideValue(string stringValue)
    {
        StringValue = stringValue;
        NodeValue = null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OverrideValue"/> struct with a syntax node substitution.
    /// </summary>
    /// <param name="nodeValue">The replacement syntax node to be visited and emitted.</param>
    public OverrideValue(SyntaxNode nodeValue)
    {
        NodeValue = nodeValue;
        StringValue = null;
    }

    /// <summary>
    /// Emits the substitution value to the visitor's underlying source writer.
    /// </summary>
    /// <param name="visitor">The active syntax walker handling node emission.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="visitor"/> is <see langword="null"/>.</exception>
    public void WriteTo(RoslynToCudaVisitor visitor)
    {
        if (visitor is null)
        {
            throw new ArgumentNullException(nameof(visitor));
        }

        if (NodeValue != null)
        {
            visitor.Visit(NodeValue);
        }
        else if (StringValue != null)
        {
            visitor.Context.Writer.Append(StringValue);
        }
    }
}

