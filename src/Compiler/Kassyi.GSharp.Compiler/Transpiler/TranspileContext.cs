namespace Kassyi.GSharp.Compiler.Transpiler;

using Rules;

/// <summary>
/// Encapsulates code generation writers, symbol caches, semantic models, and diagnostic reporters during AST transpilation.
/// </summary>
/// <param name="writer">The active <see cref="SourceWriter"/> used to emit generated source code.</param>
/// <param name="localVariableOverrides">A dictionary mapping variable identifiers to macro expansion replacement values.</param>
/// <param name="dialect">The target language and hardware dialect implementation.</param>
/// <remarks>
/// <para><b>Performance:</b> Designed as a mutable value type (<see langword="struct"/>) to be passed by reference (<see langword="ref"/>), eliminating heap allocations during recursive syntax tree walking.</para>
/// <para><b>Thread Safety:</b> Not thread-safe. Each thread or compilation task must instantiate and use its own context instance.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment). Cannot be executed within GPU kernels.</para>
/// </remarks>
public struct Emitter(
    SourceWriter writer,
    OverrideScopeStack localVariableOverrides,
    ITranspilerDialect dialect)
{
    public SourceWriter Writer = writer;
    public OverrideScopeStack LocalVariableOverrides = localVariableOverrides;
    public ITranspilerDialect Dialect { get; } = dialect;

    public bool HasEmittedDynamicSmemBase = false;
    public bool SkipLocalsInit = false;
    public bool IsGpuKernelEntry = false;
    public bool IsVoidReturn = false;
    public int CallableDepth = 0;
    public int AutoSyncGridDepth = 0;
    public string? LabelSuffix = null;
    public int SmemAllocCount = 0;

    public int PushOverrideScope(string name, OverrideValue value) =>
        LocalVariableOverrides.Push(name, value);

    public void PopOverrideScope(int cookie) =>
        LocalVariableOverrides.Pop(cookie);

    public bool TryGetOverride(string name, out OverrideValue value) =>
        LocalVariableOverrides.TryGet(name, out value);
}

public readonly struct Query(
    SemanticModel semanticModel,
    ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo> bclIntrinsicMap,
    HashSet<INamedTypeSymbol> usedStructs)
{
    public SemanticModel SemanticModel { get; } = semanticModel;
    public ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo> BclIntrinsicMap { get; } = bclIntrinsicMap;
    public HashSet<INamedTypeSymbol> UsedStructs { get; } = usedStructs;
}

public readonly struct Reporter(Action<Diagnostic>? reportDiagnostic)
{
    public Action<Diagnostic>? ReportDiagnostic { get; } = reportDiagnostic;

    public void Report(DiagnosticDescriptor descriptor, SyntaxNode node, params object?[]? messageArgs) =>
        ReportDiagnostic?.Invoke(Diagnostic.Create(descriptor, node.GetLocation(), messageArgs));

    public void Report(DiagnosticDescriptor descriptor, Location? location, params object?[]? messageArgs) =>
        ReportDiagnostic?.Invoke(Diagnostic.Create(descriptor, location, messageArgs));
}

public struct TranspileContext(
    SourceWriter writer,
    HashSet<INamedTypeSymbol> usedStructs,
    OverrideScopeStack localVariableOverrides,
    SemanticModel semanticModel,
    Action<Diagnostic>? reportDiagnostic,
    RoslynToCudaVisitor visitor,
    ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo> bclIntrinsicMap,
    ITranspilerDialect dialect,
    ITranspilerRuleRegistry ruleRegistry)
{
    public Emitter Emitter = new(writer, localVariableOverrides, dialect);
    public Query Query = new(semanticModel, bclIntrinsicMap, usedStructs);
    public Reporter Reporter = new(reportDiagnostic);

    public RoslynToCudaVisitor Visitor { get; } = visitor;
    public ITranspilerRuleRegistry RuleRegistry { get; } = ruleRegistry;
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
            visitor.Context.Emitter.Writer.Append(StringValue);
        }
    }
}

