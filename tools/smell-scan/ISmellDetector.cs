using System.Collections.Immutable;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Measures one kind of smell. A detector only reports numbers (<see cref="Measure"/>); whether a number is too big is
/// decided by <see cref="Exceeds"/> with the parameters of the rule, so the output stage never decides anything.
/// </summary>
internal interface ISmellDetector
{
    /// <summary>Gets the name that the <c>detector</c> key of a rule refers to.</summary>
    string Name { get; }

    /// <summary>Gets the input this detector needs.</summary>
    DetectorTier Tier { get; }

    /// <summary>Gets the parameters a rule may set, with defaults and minimum values.</summary>
    ImmutableArray<DetectorParameter> Parameters { get; }

    /// <summary>Gets the name of the parameter that holds the threshold.</summary>
    string ThresholdParameter { get; }

    /// <summary>Measures every place of the file that this detector looks at.</summary>
    void Measure(DetectorContext context, MeasurementSink sink);

    /// <summary>Returns true when <paramref name="measurement"/> is a finding under <paramref name="parameters"/>.</summary>
    bool Exceeds(Measurement measurement, RuleParameters parameters);
}
