using System.Collections.Immutable;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>The detectors a rule can name. The list is written out; detectors are never discovered by reflection.</summary>
internal static class DetectorRegistry
{
    /// <summary>Gets every detector in the order it is documented.</summary>
    public static ImmutableArray<ISmellDetector> All { get; } =
    [
        new MethodLengthDetector(),
        new ParameterCountDetector(),
        new NestingDepthDetector(),
    ];

    /// <summary>Finds the detector called <paramref name="name"/>.</summary>
    public static ISmellDetector? Find(string name)
    {
        foreach (var detector in All)
        {
            if (string.Equals(detector.Name, name, StringComparison.Ordinal))
            {
                return detector;
            }
        }

        return null;
    }

    /// <summary>Gets the detector names as one comma-separated text, for error messages.</summary>
    public static string NameList()
    {
        var names = new List<string>(All.Length);
        foreach (var detector in All)
        {
            names.Add(detector.Name);
        }

        return string.Join(", ", names);
    }
}
