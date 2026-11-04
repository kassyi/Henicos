namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Represents an in-scope scalar local variable inside a generated kernel or device function.
/// </summary>
public sealed record ScalarLocalVariable(string Name, FuzzTypeInfo Type);

/// <summary>
/// Represents an in-scope struct local variable inside a generated kernel.
/// </summary>
public sealed record StructLocalVariable(string Name, string StructTypeName, bool IsPacked1);

/// <summary>
/// Represents an in-scope <see cref="Nullable{T}"/> local variable (emitted when <c>#984</c> is not avoided)
/// or a <see cref="Nullable{T}"/> field access path on <c>FuzzNullableBag</c>.
/// </summary>
public sealed record NullableTargetSlot(string AccessExpression, FuzzTypeInfo UnderlyingType, bool IsLocalVariable);

/// <summary>
/// Represents an in-scope fixed-size <c>stackalloc</c> <see cref="Span{T}"/> or <see cref="ReadOnlySpan{T}"/> local variable.
/// </summary>
public sealed record SpanLocalVariable(string Name, FuzzTypeInfo ElementType, int Length, bool IsReadOnly);

/// <summary>
/// Describes a parameter of a generated <c>[GpuDeviceFunction]</c> helper method.
/// </summary>
public sealed record DeviceFunctionParam(string Name, FuzzTypeInfo Type, string Modifier);

/// <summary>
/// Describes a generated <c>[GpuDeviceFunction]</c> helper method in the per-kernel / per-batch topological call DAG.
/// </summary>
public sealed record DeviceFunctionSpec(
    string MethodName,
    int DagStage,
    FuzzTypeInfo ReturnType,
    IReadOnlyList<DeviceFunctionParam> Parameters,
    IReadOnlyList<string> TypeParameterNames,
    string MethodSource,
    bool IsInDefensiveCopyProbe = false);

/// <summary>
/// Mutable per-kernel state threaded through the scalar, composite, control-flow, and device-function emitters.
/// </summary>
public sealed class KernelGenerationState
{
    /// <summary>Initializes a new instance of the <see cref="KernelGenerationState"/> class.</summary>
    public KernelGenerationState(
        FuzzRandom rng,
        int size,
        double interferenceProbability,
        KnownIssueFilter issueFilter,
        FuzzSwarmConfig swarm,
        string kernelClassName,
        IReadOnlyList<DeviceFunctionSpec> deviceFunctions)
    {
        Rng = rng;
        Size = size;
        InterferenceProbability = interferenceProbability;
        IssueFilter = issueFilter;
        Swarm = swarm;
        KernelClassName = kernelClassName;
        DeviceFunctions = deviceFunctions;
    }

    /// <summary>Gets the deterministic PRNG for this kernel.</summary>
    public FuzzRandom Rng { get; }

    /// <summary>Gets the complexity scale factor (<c>1..8</c>).</summary>
    public int Size { get; }

    /// <summary>Gets the probability of injecting interfering side effects into sibling sub-expressions.</summary>
    public double InterferenceProbability { get; }

    /// <summary>Gets the active known-issue avoidance filter.</summary>
    public KnownIssueFilter IssueFilter { get; }

    /// <summary>Gets the Swarm Testing weight configuration for this kernel.</summary>
    public FuzzSwarmConfig Swarm { get; }

    /// <summary>Gets the enclosing kernel partial class name (used to qualify generic <c>[GpuDeviceFunction]</c> calls).</summary>
    public string KernelClassName { get; }

    /// <summary>Gets the topological <c>[GpuDeviceFunction]</c> helper methods available to this kernel.</summary>
    public IReadOnlyList<DeviceFunctionSpec> DeviceFunctions { get; }

    /// <summary>Gets the in-scope scalar local variables.</summary>
    public List<ScalarLocalVariable> Locals { get; } = [];

    /// <summary>Gets the in-scope composite struct local variables.</summary>
    public List<StructLocalVariable> StructLocals { get; } = [];

    /// <summary>Gets the in-scope <see cref="Nullable{T}"/> targets (struct fields and, when #984 is allowed, local variables).</summary>
    public List<NullableTargetSlot> NullableSlots { get; } = [];

    /// <summary>Gets the in-scope <see cref="Span{T}"/> / <see cref="ReadOnlySpan{T}"/> local variables.</summary>
    public List<SpanLocalVariable> SpanLocals { get; } = [];

    /// <summary>Gets the top-level statements emitted into the current statement sink.</summary>
    public List<string> Statements { get; private set; } = [];

    /// <summary>Gets or sets the current control-flow nesting depth (<c>0</c> at top level, up to <c>2</c> inside nested control flow).</summary>
    public int ControlFlowDepth { get; set; }

    /// <summary>Gets or sets whether statements are currently being emitted directly inside a <c>switch</c> section.</summary>
    public bool InSwitchSection { get; set; }

    /// <summary>Gets the active loop induction variable names that body statements are forbidden from mutating.</summary>
    public HashSet<string> ProtectedLoopVariables { get; } = new(StringComparer.Ordinal);

    /// <summary>Swaps the active statement buffer, returning the previous buffer so nested control-flow emitters can capture body statements.</summary>
    public List<string> SwapStatementsBuffer(List<string> nextBuffer)
    {
        ArgumentNullException.ThrowIfNull(nextBuffer);
        var previous = Statements;
        Statements = nextBuffer;
        return previous;
    }

    /// <summary>Gets or sets the monotonic FNV-1a checksum site counter.</summary>
    public ulong ChecksumSiteId { get; set; }

    /// <summary>Gets or sets the monotonic counter for unique temporary/switch/loop variable names.</summary>
    public int TempCounter { get; set; }

    /// <summary>Finds the first scalar local of <paramref name="kind"/>, or falls back to <c>Locals[0]</c>.</summary>
    public ScalarLocalVariable FindLocal(FuzzScalarKind kind) =>
        FindLocalOrNull(kind) ?? Locals[0];

    /// <summary>Finds the first scalar local of <paramref name="kind"/>, or <see langword="null"/>.</summary>
    public ScalarLocalVariable? FindLocalOrNull(FuzzScalarKind kind)
    {
        foreach (var local in Locals)
        {
            if (local.Type.Kind == kind)
            {
                return local;
            }
        }

        return null;
    }

    /// <summary>Picks two distinct scalar locals of <paramref name="preferredFirst"/> / <paramref name="preferredSecond"/>.</summary>
    public (ScalarLocalVariable First, ScalarLocalVariable Second) PickDistinctPair(
        FuzzScalarKind preferredFirst,
        FuzzScalarKind preferredSecond)
    {
        var first = FindLocal(preferredFirst);
        foreach (var local in Locals)
        {
            if (local.Name != first.Name && local.Type.Kind == preferredSecond)
            {
                return (first, local);
            }
        }

        if (preferredFirst == preferredSecond)
        {
            var typeInfo = FuzzTypeInfo.Get(preferredSecond);
            string extraName = $"v{Locals.Count}";
            Statements.Add($"{typeInfo.Keyword} {extraName} = ({first.Name} ^ {FuzzScalarExpressionEmitter.FormatLiteral(typeInfo, 0x2B)});");
            var extraLocal = new ScalarLocalVariable(extraName, typeInfo);
            Locals.Add(extraLocal);
            return (first, extraLocal);
        }

        foreach (var local in Locals)
        {
            if (local.Name != first.Name)
            {
                return (first, local);
            }
        }

        return (first, first);
    }
}

/// <summary>
/// Emits the composite struct declarations (nested structs up to depth 3, <c>readonly</c> methods/properties,
/// user-defined <c>implicit</c>/<c>explicit</c> conversion operators, <c>Pack = 1</c> / <c>Pack = 4</c> structs,
/// and <see cref="Nullable{T}"/> field containers) used by generated differential fuzz kernels.
/// </summary>
public static class FuzzCompositeModel
{
    private const string CompositeStructDeclarationsBlock =
        """
        public struct FuzzWrapInt
        {
        public int Value;
        public readonly int Shifted => Value ^ 0x5A5A;
        public readonly int MixWith(int mask) => (Value + mask) ^ (Value >> 3);
        public static implicit operator FuzzWrapInt(int v) => new FuzzWrapInt { Value = v };
        public static implicit operator int(FuzzWrapInt w) => w.Value;
        public static explicit operator long(FuzzWrapInt w) => w.Value;
        }

        public struct FuzzStructL1
        {
        public int A;
        public short B;
        public byte Tag;
        public readonly int CompA => A ^ ((int)B << 4) ^ Tag;
        public readonly int Combine(int delta) => (A + delta) ^ B;
        }

        public struct FuzzStructL2
        {
        public FuzzStructL1 Inner;
        public long C;
        public uint D;
        public readonly long Summary => C + Inner.CompA + D;
        public readonly int Project(int scale) => Inner.Combine(scale) ^ (int)D;
        }

        public struct FuzzStructL3
        {
        public FuzzStructL2 Mid;
        public FuzzWrapInt Wrap;
        public ushort Flags;
        public readonly long Total => Mid.Summary + Wrap.Shifted + Flags;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
        public struct FuzzPackedStruct1
        {
        public byte Head;
        public int MisalignedInt;
        public short Tail;
        public readonly int PackedSum => Head + MisalignedInt + Tail;
        public readonly int ReadSum() => Head + MisalignedInt + Tail;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
        public struct FuzzPackedStruct4
        {
        public short Low;
        public int AlignedInt;
        public long Wide;
        public readonly long PackedWide => Wide ^ AlignedInt ^ Low;
        public readonly long ReadWide() => Wide ^ AlignedInt ^ Low;
        }

        public struct FuzzNullableBag
        {
        public int? OptInt0;
        public int? OptInt1;
        public long? OptLong0;
        public short? OptShort0;
        }
        """;

    /// <summary>
    /// Appends all composite struct declarations used by generated fuzz kernels to <paramref name="writer"/>.
    /// </summary>
    public static void AppendCompositeStructDeclarations(SourceWriter writer) =>
        writer.AppendLine(CompositeStructDeclarationsBlock);
}
