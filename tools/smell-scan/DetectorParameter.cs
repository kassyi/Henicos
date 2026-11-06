namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>An integer parameter that a detector declares, so the configuration can be checked against it.</summary>
/// <param name="Name">The key in the YAML <c>parameters</c> map.</param>
/// <param name="Default">The value used when a rule does not set it.</param>
/// <param name="Min">The smallest accepted value.</param>
internal sealed record DetectorParameter(string Name, int Default, int Min);
