#nullable enable
using System.IO;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>
/// Builds a G# library in memory, as its own build does (the generator runs on it and writes its device code export
/// records), and returns it as a metadata reference for a consumer compilation (cross-assembly-device-code.md §3.11).
/// </summary>
public static class DeviceLibraryBuilder
{
    private static readonly CSharpParseOptions s_parseOptions = new(LanguageVersion.Preview);

    /// <summary>The library compiled from <paramref name="sources"/>, with the generator's output, as a reference.</summary>
    /// <param name="assemblyName">The assembly name of the library.</param>
    /// <param name="options">Build properties for the generator (<c>build_property.GSharpExportDeviceCode</c>, ...).</param>
    /// <param name="sources">The sources of the library.</param>
    public static MetadataReference Build(string assemblyName, Dictionary<string, string>? options, params string[] sources) =>
        BuildFiles(assemblyName, options, sources.Select((source, i) => ($"{assemblyName}_{i}.cs", source)).ToArray());

    /// <summary>The library compiled from <paramref name="sources"/> against other libraries.</summary>
    public static MetadataReference BuildWithReferences(string assemblyName, Dictionary<string, string>? options,
        MetadataReference[] references, params string[] sources) =>
        BuildFiles(assemblyName, options, references,
            sources.Select((source, i) => ($"{assemblyName}_{i}.cs", source)).ToArray());

    /// <summary>
    /// The library compiled from <paramref name="files"/> in that order, each at its path (the declaration order of a
    /// partial type follows the paths, its metadata the order of the files).
    /// </summary>
    public static MetadataReference BuildFiles(string assemblyName, Dictionary<string, string>? options,
        params (string Path, string Source)[] files) =>
        BuildFiles(assemblyName, options, [], files);

    private static MetadataReference BuildFiles(string assemblyName, Dictionary<string, string>? options,
        MetadataReference[] references, (string Path, string Source)[] files)
    {
        var compilation = CSharpCompilation.Create(assemblyName)
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true))
            .AddReferences(RoslynTestUtility.BaseReferences)
            .AddReferences(references)
            .AddSyntaxTrees(files.Select(static file =>
                CSharpSyntaxTree.ParseText(file.Source, s_parseOptions, path: file.Path)));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new GpuKernelGenerator().AsSourceGenerator()],
            additionalTexts: null,
            parseOptions: s_parseOptions,
            optionsProvider: new TestAnalyzerConfigOptionsProvider("Cuda", options));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        using var image = new MemoryStream();
        var result = output.Emit(image);
        if (!result.Success)
        {
            throw new InvalidOperationException("The test library does not compile: " +
                string.Join(Environment.NewLine, result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));
        }

        return MetadataReference.CreateFromImage(image.ToArray());
    }

    /// <summary>A consumer compilation of <paramref name="source"/> that references <paramref name="libraries"/>.</summary>
    public static CSharpCompilation CreateConsumer(string source, params MetadataReference[] libraries) =>
        CSharpCompilation.Create("DeviceImportConsumer")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true))
            .AddReferences(RoslynTestUtility.BaseReferences)
            .AddReferences(libraries)
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(source, s_parseOptions, path: "consumer.cs"));
}
