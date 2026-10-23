namespace Kassyi.GSharp.Compiler.Dialects;

/// <summary>
/// Maps the MSBuild property <c>GSharpTargetDialect</c> to the dialect it selects, for the generator and for the
/// analyzers whose checks depend on the dialect's capabilities.
/// </summary>
internal static class TranspilerDialects
{
    /// <summary>The analyzer config key of <c>GSharpTargetDialect</c>.</summary>
    public const string TargetDialectProperty = "build_property.GSharpTargetDialect";

    /// <summary>The dialect of a project that does not set <c>GSharpTargetDialect</c>.</summary>
    public const string DefaultName = "Cpu";

    /// <summary>The value of <c>GSharpTargetDialect</c>, or <see cref="DefaultName"/> when it is not set.</summary>
    public static string GetName(AnalyzerConfigOptions globalOptions) =>
        globalOptions.TryGetValue(TargetDialectProperty, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : DefaultName;

    /// <summary>Whether <paramref name="name"/> selects the CUDA dialect; any other value selects the C++ dialect.</summary>
    public static bool IsCuda(string name) => name.Equals("Cuda", StringComparison.OrdinalIgnoreCase);

    /// <summary>Creates the dialect that <paramref name="name"/> selects.</summary>
    public static ITranspilerDialect Create(string name) => IsCuda(name) ? new GSharpCudaDialect() : new StandardCppDialect();
}
