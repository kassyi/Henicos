using System.Collections.Immutable;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>A detector whose finding is "the number is larger than the threshold parameter".</summary>
internal abstract class ThresholdSmellDetector : ISmellDetector
{
    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public DetectorTier Tier => DetectorTier.Syntax;

    /// <inheritdoc />
    public abstract ImmutableArray<DetectorParameter> Parameters { get; }

    /// <inheritdoc />
    public abstract string ThresholdParameter { get; }

    /// <inheritdoc />
    public abstract void Measure(DetectorContext context, MeasurementSink sink);

    /// <inheritdoc />
    public bool Exceeds(Measurement measurement, RuleParameters parameters) =>
        measurement.Value > parameters.Get(ThresholdParameter);
}
