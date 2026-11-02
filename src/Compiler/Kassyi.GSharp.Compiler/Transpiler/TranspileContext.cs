namespace Kassyi.GSharp.Compiler.Transpiler;

using Rules;

/// <summary>
/// Encapsulates code generation writers, symbol caches, semantic models, and diagnostic reporters during AST transpilation.
/// </summary>
/// <param name="writer">The active <see cref="SourceWriter"/> used to emit generated source code.</param>
/// <param name="localVariableOverrides">A dictionary mapping variable identifiers to macro expansion replacement values.</param>
/// <param name="activeSwitchIds">A pooled stack tracking active switch statement IDs for nested <c>goto case</c> / <c>goto default</c> labels.</param>
/// <param name="dialect">The target language and hardware dialect implementation.</param>
/// <remarks>
/// <para><b>Performance:</b> Designed as a mutable value type (<see langword="struct"/>) to be passed by reference (<see langword="ref"/>), eliminating heap allocations during recursive syntax tree walking.</para>
/// <para><b>Thread Safety:</b> Not thread-safe. Each thread or compilation task must instantiate and use its own context instance.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment). Cannot be executed within GPU kernels.</para>
/// </remarks>
public struct Emitter(
    SourceWriter writer,
    OverrideScopeStack localVariableOverrides,
    Stack<int> activeSwitchIds,
    ITranspilerDialect dialect)
{
    public SourceWriter Writer = writer;
    public OverrideScopeStack LocalVariableOverrides = localVariableOverrides;
    public ITranspilerDialect Dialect { get; } = dialect;
    /// <summary>
    /// The <c>#include</c> targets that the emitted code needs. A device code export replaces the set for a moment to
    /// learn what one struct definition needs (<c>StructTranspiler.WriteDefinitionPart</c>).
    /// </summary>
    public HashSet<string> RequiredIncludes { get; set; } = new();

    /// <summary>
    /// The qualifier of <c>Slot&lt;Key&gt;</c> in the table of dynamic shared memory regions that <c>__gsharp_dsmem</c>
    /// points to (CUDA): <c>__gsharp_dsmem_&lt;kernel&gt;::</c> in a kernel, <c>TDsmem::template </c> in a device
    /// function that receives the table. <see langword="null"/> when the code being emitted has no table
    /// (<see cref="DynamicSharedMemoryEmitter"/>).
    /// </summary>
    public string? DynamicSharedMemorySlotScope;
    public bool IsGpuKernelEntry;

    /// <summary>The body is the state machine of a device function that may suspend (CPU dialects).</summary>
    public bool IsSuspendingDeviceFunction;
    public bool IsVoidReturn;
    public int CallableDepth;

    /// <summary>
    /// Numbers each <c>Gpu.SyncGrid(body)</c> body as it is emitted, for its done flag and labels
    /// (<c>__gsharp_sg_done_&lt;n&gt;</c>, <c>__gsharp_sg_next_&lt;n&gt;_&lt;statement&gt;</c>); a body emitted again (an
    /// unrolled loop) gets a new number, so the names stay unique in the C++ function.
    /// </summary>
    public int SyncGridBodyCounter;

    /// <summary>
    /// Numbers each <c>Gpu.SingleThread</c> body with a <c>return</c> as it is emitted, for the label after it
    /// (<c>__gsharp_st_end_&lt;n&gt;</c>); a body emitted again (an unrolled loop) gets a new number.
    /// </summary>
    public int SingleThreadBodyCounter;

    /// <summary>
    /// Numbers each <c>Gpu.GridStrideLoop</c> as it is emitted, for its index and count locals
    /// (<c>__gsharp_gsl_&lt;n&gt;</c>, <c>__gsharp_gsl_n_&lt;n&gt;</c>); a loop emitted again (an unrolled loop) gets a new
    /// number.
    /// </summary>
    public int GridStrideLoopCounter;

    /// <summary>
    /// The label after the <c>Gpu.SingleThread</c> body being emitted, when the body has a <c>return</c>; a
    /// <c>return</c> at <see cref="SingleThreadCallableDepth"/> (not in a lambda or local function of the body) jumps to
    /// it. <see langword="null"/> outside such a body.
    /// </summary>
    public string? SingleThreadEndLabel;

    /// <summary>The <see cref="CallableDepth"/> of the body that <see cref="SingleThreadEndLabel"/> ends.</summary>
    public int SingleThreadCallableDepth;
    public string? LabelSuffix;

    /// <summary>
    /// The struct whose definition or <c>Dispose</c> body is currently being emitted, used to substitute generic type
    /// parameters with the concrete type arguments of the specialization.
    /// </summary>
    public INamedTypeSymbol? CurrentStructType;

    /// <summary>
    /// The struct whose user-defined constructor body is currently being emitted, or <see langword="null"/>.
    /// </summary>
    public INamedTypeSymbol? StructConstructorType;

    /// <summary>
    /// The syntax node or member type syntax currently being emitted, used when reporting diagnostics from type emission.
    /// </summary>
    public SyntaxNode? CurrentNode;

    public int SwitchIdCounter;
    public Stack<int> ActiveSwitchIds = activeSwitchIds;

    /// <summary>Monotonically increasing region counter for inlined <c>foreach</c> iterator expansions in the current method.</summary>
    public int IteratorExpansionCounter;

    /// <summary>Monotonically increasing ordinal for <c>__gsharp_arg_temp_&lt;n&gt;</c> temporaries synthesized for out-of-order named arguments.</summary>
    public int ArgumentTempCounter;

    /// <summary>
    /// Numbers the <c>__gsharp_seq_&lt;n&gt;</c> temporaries that keep the C# order of evaluation in the method
    /// (<see cref="EvaluationOrder"/>).
    /// </summary>
    public int SequenceTempCounter;

    /// <summary>Numbers the <c>__gsharp_foreach_e_&lt;n&gt;</c> raw elements of converting range-for loops in the method.</summary>
    public int ForEachElementCounter;

    /// <summary>Numbers the guards and the resource temporaries of the <c>using</c>s of the method (UsingTranspiler).</summary>
    public int UsingCounter;

    /// <summary>
    /// Numbers the storages <c>__gsharp_stackalloc_&lt;n&gt;</c> that the statement prelude declares for the stack arrays
    /// outside a declaration (StackAllocEmitter).
    /// </summary>
    public int StackAllocCounter;

    /// <summary>The storage number of each node that the statement prelude declared a storage for, read where the node is written.</summary>
    public Dictionary<SyntaxNode, int> StackAllocStorages { get; } = new();

    /// <summary>
    /// The temporary (<c>__gsharp_seq_&lt;n&gt;</c>) that a part of the target of a step or compound assignment was
    /// evaluated into, written in place of the part while the target is written (IntegerSemanticsEmitter).
    /// </summary>
    public Dictionary<SyntaxNode, int> SpilledExpressions { get; } = new();

    /// <summary>Whether each for loop step that BoundedLoopStep analyzed cannot overflow (it then stays <c>i++</c>).</summary>
    public Dictionary<SyntaxNode, bool> BoundedLoopSteps { get; } = new();

    /// <summary>
    /// The struct definitions whose <c>Dispose</c> a <c>using</c> of the method calls, in the order of first use; each
    /// definition is emitted after the structs (UsingTranspiler.WriteDisposeDefinitions).
    /// </summary>
    public ImmutableArray<INamedTypeSymbol>.Builder DisposedStructs { get; } = ImmutableArray.CreateBuilder<INamedTypeSymbol>();

    /// <summary>
    /// The comparison members of user structs (IComparer&lt;T&gt;.Compare, IComparable&lt;T&gt;.CompareTo) that a
    /// <c>MemoryExtensions.Sort</c> of the method calls, in the order of first use; each definition is emitted after the
    /// <c>Dispose</c> definitions (SortComparisonMembers.WriteDefinitions).
    /// </summary>
    public ImmutableArray<IMethodSymbol>.Builder SortComparisons { get; } = ImmutableArray.CreateBuilder<IMethodSymbol>();

    public int CurrentSwitchId => ActiveSwitchIds.Count > 0 ? ActiveSwitchIds.Peek() : 0;

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

/// <summary>Collects the constructs that the transpilation could not emit (<see cref="TranspileFailure"/>).</summary>
/// <param name="reportFailure">Receives each failure; the analyzer reports it, the generator states it in the output.</param>
/// <param name="semanticModel">The semantic model that maps a node of a lowered body to its original location.</param>
public readonly struct Reporter(Action<TranspileFailure>? reportFailure, SemanticModel semanticModel)
{
    /// <summary>
    /// Records that the output is incomplete because of <paramref name="node"/>, which the analyzer reports with
    /// <paramref name="descriptor"/>. A node of a lowered body is placed at its original location
    /// (<see cref="SemanticModelExtensions.GetReportLocation"/>).
    /// </summary>
    public void Fail(DiagnosticDescriptor descriptor, SyntaxNode node, params object?[] messageArgs) =>
        reportFailure?.Invoke(new TranspileFailure(descriptor, semanticModel.GetReportLocation(node), messageArgs));

    /// <inheritdoc cref="Fail(DiagnosticDescriptor, SyntaxNode, object?[])"/>
    public void Fail(DiagnosticDescriptor descriptor, Location location, params object?[] messageArgs) =>
        reportFailure?.Invoke(new TranspileFailure(descriptor, location, messageArgs));

    /// <summary>
    /// The same sink with another semantic model, for a body bound in another tree (a struct's computed property getter);
    /// its failures reach the same transpilation.
    /// </summary>
    public Reporter WithSemanticModel(SemanticModel model) => new(reportFailure, model);

    /// <summary>
    /// The same sink, which also calls <paramref name="onFailure"/> for each failure; with <paramref name="forward"/>
    /// false the failures reach <paramref name="onFailure"/> only (a part of a device code export that keeps its own
    /// failures, <c>StructSourcePart.Failed</c>).
    /// </summary>
    public Reporter Intercept(Action onFailure, bool forward)
    {
        var sink = reportFailure;
        return new Reporter(failure =>
        {
            onFailure();
            if (forward)
            {
                sink?.Invoke(failure);
            }
        }, semanticModel);
    }
}

public struct TranspileContext(
    SourceWriter writer,
    HashSet<INamedTypeSymbol> usedStructs,
    OverrideScopeStack localVariableOverrides,
    Stack<int> activeSwitchIds,
    SemanticModel semanticModel,
    Action<TranspileFailure>? reportFailure,
    RoslynToCudaVisitor visitor,
    ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo> bclIntrinsicMap,
    ITranspilerDialect dialect)
{
    public Emitter Emitter = new(writer, localVariableOverrides, activeSwitchIds, dialect);
    public Query Query = new(semanticModel, bclIntrinsicMap, usedStructs);
    public Reporter Reporter = new(reportFailure, semanticModel);

    public RoslynToCudaVisitor Visitor { get; } = visitor;
    internal TranspilerRuleRegistry RuleRegistry => TranspilerRuleRegistry.Default;
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
