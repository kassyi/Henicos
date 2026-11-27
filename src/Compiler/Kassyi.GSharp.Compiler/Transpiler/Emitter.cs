using Kassyi.GSharp.Compiler.Marshaling;

namespace Kassyi.GSharp.Compiler.Transpiler;

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

    /// <summary>
    /// The two-forms plan for kernel emission, tracking whether the kernel emits two forms on CUDA
    /// and which parameters are passed by address.
    /// </summary>
    internal KernelTwoFormsPlan TwoFormsPlan = KernelTwoFormsPlan.Empty;

    /// <summary>
    /// The kernel form currently being emitted on CUDA (by-value or by-address).
    /// </summary>
    internal KernelFormKind CurrentKernelForm = KernelFormKind.ByValue;

    /// <summary>
    /// Evaluates whether <paramref name="parameter"/> is passed by address in the current dialect and kernel form.
    /// </summary>
    internal bool IsParameterPassedByAddress(IParameterSymbol? parameter)
    {
        if (parameter == null || !IsGpuKernelEntry || MethodSymbol == null)
        {
            return false;
        }

        // Only parameters directly belonging to the kernel entry point are passed by address under TwoFormsPlan.
        // Parameters of struct constructors, local functions, and device functions must never be affected.
        if (!SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, MethodSymbol))
        {
            return false;
        }

        if (Dialect.IsCuda)
        {
            return CurrentKernelForm == KernelFormKind.ByAddress && TwoFormsPlan.IsPassedByAddress(parameter.Ordinal);
        }

        return TwoFormsPlan.IsPassedByAddress(parameter.Ordinal);
    }

    /// <summary>
    /// The places of the warp collectives of the stack form being written in the visit counts of its kernel
    /// (<see cref="StackWarpEmitter"/>); <see langword="null"/> for any other code.
    /// </summary>
    internal StackWarpFrame? StackWarp;

    /// <summary>The body is the state machine of a device function that may suspend (CPU dialects).</summary>
    public bool IsSuspendingDeviceFunction;
    public bool IsVoidReturn;

    /// <summary>The method being transpiled, whose lowered body may be outside any declaration of the tree.</summary>
    internal IMethodSymbol? MethodSymbol;
    public int CallableDepth;

    /// <summary>
    /// The compile-time error site table used to record source locations for device runtime traps (#729).
    /// </summary>
    internal ErrorSiteTable? ErrorSites;

    /// <summary>
    /// The error sites of the <c>throw</c> statements of a device function, which has no <see cref="ErrorSites"/>: the
    /// other failed checks of a device function carry their text instead of a site. Null in a kernel, whose table takes
    /// the throws too.
    /// </summary>
    internal ErrorSiteTable? ThrowSites;

    /// <summary>
    /// The current member or kernel method name being transpiled.
    /// </summary>
    internal string? CurrentMemberName;

    /// <summary>
    /// Packed method-scoped identifier and temporary counters.
    /// </summary>
    public struct MethodCounters
    {
        public int SyncGridBody;
        public int SingleThreadBody;
        public int GridStrideLoop;
        public int SwitchId;
        public int IteratorExpansion;
        public int ArgumentTemp;
        public int SequenceTemp;
        public int ForEachElement;
        public int Using;
        public int StackAlloc;
    }

    /// <summary>The packed counters of the method being transpiled.</summary>
    public MethodCounters Counters;

    /// <summary>
    /// Numbers each <c>Gpu.SyncGrid(body)</c> body as it is emitted, for its done flag and labels
    /// (<c>__gsharp_sg_done_&lt;n&gt;</c>, <c>__gsharp_sg_next_&lt;n&gt;_&lt;statement&gt;</c>); a body emitted again (an
    /// unrolled loop) gets a new number, so the names stay unique in the C++ function.
    /// </summary>
    public int SyncGridBodyCounter
    {
        readonly get => Counters.SyncGridBody;
        set => Counters.SyncGridBody = value;
    }

    /// <summary>
    /// Numbers each <c>Gpu.SingleThread</c> body with a <c>return</c> as it is emitted, for the label after it
    /// (<c>__gsharp_st_end_&lt;n&gt;</c>); a body emitted again (an unrolled loop) gets a new number.
    /// </summary>
    public int SingleThreadBodyCounter
    {
        readonly get => Counters.SingleThreadBody;
        set => Counters.SingleThreadBody = value;
    }

    /// <summary>
    /// Numbers each <c>Gpu.GridStrideLoop</c> as it is emitted, for its index and count locals
    /// (<c>__gsharp_gsl_&lt;n&gt;</c>, <c>__gsharp_gsl_n_&lt;n&gt;</c>); a loop emitted again (an unrolled loop) gets a new
    /// number.
    /// </summary>
    public int GridStrideLoopCounter
    {
        readonly get => Counters.GridStrideLoop;
        set => Counters.GridStrideLoop = value;
    }

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
    /// Planned CUDA iteration keys for <c>Warp.ActiveMask()</c> calls in the method body currently being emitted.
    /// </summary>
    internal ActiveMaskIterationPlan? ActiveMaskIterations;

    /// <summary>
    /// The struct whose definition or <c>Dispose</c> body is currently being emitted, used to substitute generic type
    /// parameters with the concrete type arguments of the specialization.
    /// </summary>
    public INamedTypeSymbol? CurrentStructType;

    /// <summary>
    /// The struct whose constructor or instance device function body is currently being emitted and how
    /// <see langword="this"/> is spelled there, or default (<see cref="SelfForm.MemberFunction"/>).
    /// </summary>
    internal StructSelfReference StructSelf;

    /// <summary>
    /// The syntax node or member type syntax currently being emitted, used when reporting diagnostics from type emission.
    /// </summary>
    public SyntaxNode? CurrentNode;

    /// <summary>
    /// The expression syntax node whose outer implicit user-defined conversion is currently being emitted, so that
    /// visiting the operand does not apply the conversion a second time (<c>UserDefinedConversionHelper</c>).
    /// </summary>
    public SyntaxNode? SuppressImplicitConversionNode;

    public int SwitchIdCounter
    {
        readonly get => Counters.SwitchId;
        set => Counters.SwitchId = value;
    }
    public Stack<int> ActiveSwitchIds = activeSwitchIds;

    /// <summary>Monotonically increasing region counter for inlined <c>foreach</c> iterator expansions in the current method.</summary>
    public int IteratorExpansionCounter
    {
        readonly get => Counters.IteratorExpansion;
        set => Counters.IteratorExpansion = value;
    }

    /// <summary>Monotonically increasing ordinal for <c>__gsharp_arg_temp_&lt;n&gt;</c> temporaries synthesized for out-of-order named arguments.</summary>
    public int ArgumentTempCounter
    {
        readonly get => Counters.ArgumentTemp;
        set => Counters.ArgumentTemp = value;
    }

    /// <summary>
    /// Numbers the <c>__gsharp_seq_&lt;n&gt;</c> temporaries that keep the C# order of evaluation in the method
    /// (<see cref="EvaluationOrder"/>).
    /// </summary>
    public int SequenceTempCounter
    {
        readonly get => Counters.SequenceTemp;
        set => Counters.SequenceTemp = value;
    }

    /// <summary>Numbers the <c>__gsharp_foreach_e_&lt;n&gt;</c> raw elements of converting range-for loops in the method.</summary>
    public int ForEachElementCounter
    {
        readonly get => Counters.ForEachElement;
        set => Counters.ForEachElement = value;
    }

    /// <summary>Numbers the guards and the resource temporaries of the <c>using</c>s of the method (UsingTranspiler).</summary>
    public int UsingCounter
    {
        readonly get => Counters.Using;
        set => Counters.Using = value;
    }

    /// <summary>
    /// The names of the guards of the <c>using</c>s whose scope the CUDA code being written is in, the innermost last:
    /// a failed check there disposes them before the thread ends (<see cref="UnwindingTrap"/>).
    /// </summary>
    internal List<string> ActiveGuards { get; } = [];

    /// <summary>
    /// Forgets the guards declared after the first <paramref name="count"/>: their C++ scope has ended.
    /// </summary>
    internal void EndGuardScope(int count)
    {
        if (ActiveGuards.Count > count)
        {
            ActiveGuards.RemoveRange(count, ActiveGuards.Count - count);
        }
    }

    /// <summary>
    /// The <c>using</c>s whose body is being written on the CPU, the innermost last: a jump out of the body goes to an
    /// exit label of the innermost one (<see cref="UsingCpuEmitter"/>).
    /// </summary>
    internal ImmutableArray<UsingCpuScope>.Builder UsingScopes { get; } = ImmutableArray.CreateBuilder<UsingCpuScope>();

    /// <summary>
    /// While the exit label of a <c>using</c> writes its <c>return</c> again: the text that reads the value the
    /// <c>return</c> kept (<c>__gsharp_using_return</c>), which takes the place of its expression.
    /// </summary>
    public string? UsingReturnValue;

    /// <summary>
    /// Numbers the storages <c>__gsharp_stackalloc_&lt;n&gt;</c> that the statement prelude declares for the stack arrays
    /// outside a declaration (StackAllocEmitter).
    /// </summary>
    public int StackAllocCounter
    {
        readonly get => Counters.StackAlloc;
        set => Counters.StackAlloc = value;
    }

    /// <summary>The storage number of each node that the statement prelude declared a storage for, read where the node is written.</summary>
    public Dictionary<SyntaxNode, int> StackAllocStorages { get; } = new();

    /// <summary>
    /// The storage number (<c>__gsharp_discard_&lt;n&gt;</c>) of each discard <c>out</c> argument that the statement
    /// prelude declared a variable for (DiscardOutArgument); the count is the next number.
    /// </summary>
    public Dictionary<SyntaxNode, int> DiscardStorages { get; } = new();

    /// <summary>
    /// The number (<c>__gsharp_smem_&lt;n&gt;</c>) of each <c>SharedMemory&lt;T&gt;</c> creation that the statement
    /// prelude declared the memory for (SharedMemoryCreationLift); the count is the next number.
    /// </summary>
    public Dictionary<SyntaxNode, int> SharedMemoryLifts { get; } = new();

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

    public int PushOverrideScope(string name, OverrideValue value, ISymbol? symbol = null) =>
        LocalVariableOverrides.Push(name, value, symbol);

    public void PopOverrideScope(int cookie) =>
        LocalVariableOverrides.Pop(cookie);

    public bool TryGetOverride(string name, ISymbol? symbol, out OverrideValue value) =>
        LocalVariableOverrides.TryGet(name, symbol, out value);

    /// <summary>
    /// Resets AST identifier and statement temporary counters between kernel emission passes.
    /// </summary>
    public void ResetMethodCounters() => Counters = default;
}
