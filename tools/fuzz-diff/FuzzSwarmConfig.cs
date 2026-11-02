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

    /// <summary>Parses a CLI <c>--swarm-focus</c> string into a <see cref="FuzzSwarmFocus"/> value.</summary>
    public static bool TryParseFocus(string? raw, out FuzzSwarmFocus focus)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            focus = FuzzSwarmFocus.Default;
            return true;
        }

        switch (raw.Trim().ToLowerInvariant())
        {
            case "default" or "none":
                focus = FuzzSwarmFocus.Default;
                return true;
            case "coalesce-assign-value" or "coalesceassignvalue":
                focus = FuzzSwarmFocus.CoalesceAssignValue;
                return true;
            case "tuple-deconstruct-complex" or "tupledeconstructcomplex":
                focus = FuzzSwarmFocus.TupleDeconstructComplex;
                return true;
            default:
                focus = FuzzSwarmFocus.Default;
                return false;
        }
    }
}

