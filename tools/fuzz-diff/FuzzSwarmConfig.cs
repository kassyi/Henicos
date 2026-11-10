namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Per-seed Swarm Testing weight configuration controlling operator, type, statement, and composite feature selection.
/// Derived deterministically from <see cref="FuzzRandom"/> and <see cref="FuzzSwarmFocus"/>.
/// </summary>
public sealed record FuzzSwarmConfig(
    int ShiftWeight,
    int BitwiseWeight,
    int DivisionWeight,
    int TernaryWeight,
    int InterferenceWeight,
    int StructOpsWeight,
    int NullableOpsWeight,
    int SpanOpsWeight,
    int SwitchExprWeight,
    int RefTernaryWeight,
    int TupleDeconstructWeight,
    int ControlFlowWeight,
    int LoopWeight,
    int UnrollWeight,
    int DeviceCallWeight,
    FuzzSwarmFocus Focus)
{
    /// <summary>
    /// Samples a deterministic Swarm Testing configuration from <paramref name="rng"/> and <paramref name="focus"/>.
    /// Each seed suppresses a random subset of optional categories while amplifying others so small batches
    /// explore distinct corners of the G# transpiler grammar.
    /// </summary>
    public static FuzzSwarmConfig Create(FuzzRandom rng, FuzzSwarmFocus focus = FuzzSwarmFocus.Default)
    {
        ArgumentNullException.ThrowIfNull(rng);

        int SampleWeight(int min, int max) => rng.Chance(0.20) ? 1 : rng.NextRange(min, max);

        int shiftW = SampleWeight(5, 30);
        int bitW = SampleWeight(5, 30);
        int divW = SampleWeight(5, 25);
        int ternW = SampleWeight(5, 25);
        int interfW = SampleWeight(10, 40);
        int structW = SampleWeight(10, 35);
        int nullW = SampleWeight(8, 30);
        int spanW = SampleWeight(8, 25);
        int switchExprW = SampleWeight(5, 25);
        int refTernW = SampleWeight(5, 20);
        int tupleW = SampleWeight(8, 25);
        int cfW = SampleWeight(8, 25);
        int loopW = SampleWeight(5, 20);
        int unrollW = SampleWeight(5, 20);
        int callW = SampleWeight(10, 35);

        if (focus == FuzzSwarmFocus.CoalesceAssignValue)
        {
            nullW = 80;
        }
        else if (focus == FuzzSwarmFocus.TupleDeconstructComplex)
        {
            tupleW = 80;
        }

        return new FuzzSwarmConfig(
            shiftW,
            bitW,
            divW,
            ternW,
            interfW,
            structW,
            nullW,
            spanW,
            switchExprW,
            refTernW,
            tupleW,
            cfW,
            loopW,
            unrollW,
            callW,
            focus);
    }

    /// <summary>The <c>--swarm-focus</c> names, each with its compact spelling, in the order the help text lists
    /// them.</summary>
    internal static IReadOnlyList<(string Name, FuzzSwarmFocus Focus)> FocusNames { get; } =
    [
        ("default", FuzzSwarmFocus.Default),
        ("coalesce-assign-value", FuzzSwarmFocus.CoalesceAssignValue),
        ("tuple-deconstruct-complex", FuzzSwarmFocus.TupleDeconstructComplex),
        ("warp-shuffle", FuzzSwarmFocus.WarpShuffle),
        ("barrier", FuzzSwarmFocus.Barrier),
        ("iterator", FuzzSwarmFocus.Iterator),
        ("suspending-call", FuzzSwarmFocus.SuspendingCall),
        ("split-points", FuzzSwarmFocus.SplitPoints),
    ];

    /// <summary>Gets the message for a <c>--swarm-focus</c> value that names no mode.</summary>
    internal static string FocusErrorMessage { get; } = BuildFocusErrorMessage();

    /// <summary>
    /// Parses a CLI <c>--swarm-focus</c> string into a <see cref="FuzzSwarmFocus"/> value: a name of <see
    /// cref="FocusNames"/>, the same name without hyphens, or <c>none</c>.
    /// </summary>
    public static bool TryParseFocus(string? raw, out FuzzSwarmFocus focus)
    {
        focus = FuzzSwarmFocus.Default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        ReadOnlySpan<char> trimmed = raw.AsSpan().Trim();
        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var (name, value) in FocusNames)
        {
            string compact = name.Replace("-", string.Empty, StringComparison.Ordinal);
            if (trimmed.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals(compact, StringComparison.OrdinalIgnoreCase))
            {
                focus = value;
                return true;
            }
        }

        return false;
    }

    private static string BuildFocusErrorMessage()
    {
        var quoted = new List<string>(FocusNames.Count);
        foreach (var (name, _) in FocusNames)
        {
            quoted.Add($"'{name}'");
        }

        return $"--swarm-focus requires one of {string.Join(", ", quoted)}.";
    }
}
