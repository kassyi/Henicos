namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Optional Swarm Testing focus override used to target specific grammar shapes.
/// </summary>
public enum FuzzSwarmFocus
{
    /// <summary>Default per-seed Swarm Testing weight sampling across all grammar categories.</summary>
    Default = 0,

    /// <summary>Forces emission of value-consuming <c>??=</c> expressions where the right-hand side is a non-nullable value.</summary>
    CoalesceAssignValue = 1,

    /// <summary>Forces emission of complex tuple deconstructions (nested tuple targets and non-literal tuple RHS).</summary>
    TupleDeconstructComplex = 2,
}

/// <summary>
/// Options controlling single-kernel and batch generation in <see cref="ScalarFuzzGenerator"/>.
/// </summary>
public sealed record FuzzOptions
{
    /// <summary>Gets the 64-bit master fuzz seed determining the AST structure and operator weights.</summary>
    public ulong FuzzSeed { get; init; } = 1UL;

    /// <summary>Gets the optional explicit runtime data input seed; <see langword="null"/> runs <c>DifferentialSeeds.Fixed</c> (<c>0..8</c>).</summary>
    public int? InputSeed { get; init; }

    /// <summary>Gets the number of kernels emitted per batch (<c>1..256</c>, default <c>64</c>).</summary>
    public int BatchSize { get; init; } = 64;

    /// <summary>Gets the complexity scale factor (<c>1..8</c>) when generating a single kernel.</summary>
    public int Size { get; init; } = 4;

    /// <summary>Gets the namespace for emitted kernel and case types.</summary>
    public string Namespace { get; init; } = "Kassyi.GSharp.Tests.Shared.Differential";

    /// <summary>Gets the partial class name for emitted kernels.</summary>
    public string KernelClassName { get; init; } = "FuzzBatchKernels";

    /// <summary>Gets the class name for emitted differential cases.</summary>
    public string CasesClassName { get; init; } = "FuzzBatchCases";

    /// <summary>Gets whether to emit a standalone <c>Program.Main</c> JSONL batch runner inside <c>FuzzBatchCases.cs</c>.</summary>
    public bool EmitProgramMain { get; init; } = true;

    /// <summary>Gets the probability <c>[0.25, 0.60]</c> of injecting interfering read/write side effects into sibling sub-expressions.</summary>
    public double InterferenceProbability { get; init; } = 0.45;

    /// <summary>Gets the target execution profile (<c>"M"</c>, <c>"C"</c>, or <c>"G"</c>) controlling active known-issue avoidance switches.</summary>
    public string Profile { get; init; } = "C";

    /// <summary>Gets the set of issue numbers explicitly allowed (re-enabled) via <c>--allow-issue &lt;n&gt;</c>.</summary>
    public IReadOnlySet<int> AllowedIssues { get; init; } = new HashSet<int>();

    /// <summary>Gets the optional Swarm Testing focus mode.</summary>
    public FuzzSwarmFocus SwarmFocus { get; init; } = FuzzSwarmFocus.Default;
}

/// <summary>
/// Represents a single generated kernel and its companion differential test case metadata.
/// </summary>
public sealed record GeneratedKernelSpec(
    string KernelName,
    ulong KernelSeed,
    int Size,
    string KernelMethodSource,
    string CasePropertySource,
    IReadOnlyList<string> LocalVariableNames,
    IReadOnlyList<string> StatementTexts);

/// <summary>
/// Represents the complete generated source files for a batch of differential fuzz kernels.
/// </summary>
public sealed record GeneratedBatch(
    ulong FuzzSeed,
    int? InputSeed,
    string KernelsSource,
    string CasesSource,
    IReadOnlyList<GeneratedKernelSpec> Kernels);
