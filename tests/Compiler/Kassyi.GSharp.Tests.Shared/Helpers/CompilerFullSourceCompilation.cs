#nullable enable
using System.Xml.Linq;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>
/// Builds the compilation of every source file compiled into <c>Kassyi.GSharp.Compiler</c> for
/// <see cref="RoslynTestUtility.CreateCompilerFullSourceCompilation"/>.
/// </summary>
internal static class CompilerFullSourceCompilation
{
    internal static CSharpCompilation Build()
    {
        var compilerRoot = RepositoryPaths.CompilerProject;
        var stackLinqEmbeddedDir = Path.Combine(compilerRoot, "StackLinqEmbedded");
        if (!Directory.Exists(stackLinqEmbeddedDir) ||
            !Directory.EnumerateFiles(stackLinqEmbeddedDir, "*.cs").Any())
        {
            throw new InvalidOperationException(
                $"Embedded StackLinq sources were not found under {stackLinqEmbeddedDir}. " +
                "Build Kassyi.GSharp.Compiler first.");
        }

        var pathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddProjectSources(compilerRoot, pathSet);
        AddLinkedSharedSources(compilerRoot, pathSet);
        AddGeneratedSources(ResolveCompilerIntermediateOutputDirectory(compilerRoot), pathSet);

        var sortedPaths = pathSet.Order(StringComparer.Ordinal).ToArray();
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: ["GSHARP_COMPILER"]);
        var trees = new SyntaxTree[sortedPaths.Length];
        Parallel.For(0, sortedPaths.Length, i =>
        {
#pragma warning disable RS0030 // The single place that parses test sources; tests reach it through RoslynTestUtility
            trees[i] = CSharpSyntaxTree.ParseText(File.ReadAllText(sortedPaths[i]), parseOptions, path: sortedPaths[i]);
#pragma warning restore RS0030
        });

        var references = RoslynTestUtility.BaseReferences
            .Where(static reference =>
            {
                var fileName = Path.GetFileName(reference.Display);
                return fileName is null ||
                    (!fileName.StartsWith("Kassyi.", StringComparison.OrdinalIgnoreCase) &&
                     !fileName.StartsWith("Henicos.", StringComparison.OrdinalIgnoreCase));
            })
            .ToArray();

        return CSharpCompilation.Create(
            "Kassyi.GSharp.Compiler.FullSourceAnalysis",
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    // Every .cs file of the project folder outside obj/ and bin/
    private static void AddProjectSources(string compilerRoot, HashSet<string> pathSet)
    {
        foreach (var file in Directory.EnumerateFiles(compilerRoot, "*.cs"))
        {
            pathSet.Add(Path.GetFullPath(file));
        }

        foreach (var directory in Directory.EnumerateDirectories(compilerRoot))
        {
            if (Path.GetFileName(directory) is "obj" or "bin")
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                pathSet.Add(Path.GetFullPath(file));
            }
        }
    }

    // The src/Shared files the project links with <Compile Include>
    private static void AddLinkedSharedSources(string compilerRoot, HashSet<string> pathSet)
    {
        var csprojPath = Path.Combine(compilerRoot, "Kassyi.GSharp.Compiler.csproj");
        var csproj = XDocument.Load(csprojPath);
        var separator = Path.DirectorySeparatorChar;
        var sharedPrefix = $"..{separator}..{separator}Shared{separator}";
        foreach (var element in csproj.Descendants())
        {
            if (!string.Equals(element.Name.LocalName, "Compile", StringComparison.Ordinal))
            {
                continue;
            }

            var include = element.Attribute("Include")?.Value;
            if (string.IsNullOrEmpty(include))
            {
                continue;
            }

            var normalizedInclude = NormalizeDirectorySeparators(include);
            if (!normalizedInclude.StartsWith(sharedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var linkedPath = Path.GetFullPath(Path.Combine(compilerRoot, normalizedInclude));
            if (!File.Exists(linkedPath))
            {
                throw new InvalidOperationException($"Linked compiler source file not found: {linkedPath}");
            }

            pathSet.Add(linkedPath);
        }
    }

    // The sources the build generates into obj/; GeneratedNativeApi is not read: the project removes it from its
    // Compile items
    private static void AddGeneratedSources(string intermediateDir, HashSet<string> pathSet)
    {
        string[] generatedSubdirectories =
        [
            "GeneratedDialects",
            "GeneratedDiagnostics",
            "GeneratedPrelude",
            "GeneratedInternalNames"
        ];
        foreach (var subdir in generatedSubdirectories)
        {
            var generatedDir = Path.Combine(intermediateDir, subdir);
            if (!Directory.Exists(generatedDir))
            {
                throw new InvalidOperationException($"Generated compiler directory not found: {generatedDir}");
            }

            var anyGenerated = false;
            foreach (var file in Directory.EnumerateFiles(generatedDir, "*.g.cs", SearchOption.AllDirectories))
            {
                pathSet.Add(Path.GetFullPath(file));
                anyGenerated = true;
            }

            if (!anyGenerated)
            {
                throw new InvalidOperationException($"No generated *.g.cs files found in: {generatedDir}");
            }
        }
    }

    private static string ResolveCompilerIntermediateOutputDirectory(string compilerRoot)
    {
        var tfmDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        var configDir = Path.GetDirectoryName(tfmDir);
        var outputDir = configDir is not null ? Path.GetDirectoryName(configDir) : null;
        if (configDir is not null && outputDir is not null)
        {
            var configName = Path.GetFileName(configDir);
            var gsharpOutputDir = Path.GetFileName(outputDir);
            if (!string.IsNullOrEmpty(configName) && !string.IsNullOrEmpty(gsharpOutputDir))
            {
                var candidate = Path.Combine(compilerRoot, "obj", gsharpOutputDir, configName, "netstandard2.0");
                if (HasRequiredGeneratedDirectories(candidate))
                {
                    return candidate;
                }
            }
        }

        var hostOs = OperatingSystem.IsWindows()
            ? "win"
            : OperatingSystem.IsLinux()
                ? "linux"
                : OperatingSystem.IsMacOS()
                    ? "osx"
                    : "unix";
#if DEBUG
        const string FallbackConfig = "Debug";
#else
        const string FallbackConfig = "Release";
#endif
        var fallback = Path.Combine(compilerRoot, "obj", hostOs, FallbackConfig, "netstandard2.0");
        if (HasRequiredGeneratedDirectories(fallback))
        {
            return fallback;
        }

        var objRoot = Path.Combine(compilerRoot, "obj");
        if (Directory.Exists(objRoot))
        {
            var latest = Directory.EnumerateDirectories(objRoot, "netstandard2.0", SearchOption.AllDirectories)
                .Where(HasRequiredGeneratedDirectories)
                .OrderByDescending(static dir =>
                    Directory.GetLastWriteTimeUtc(Path.Combine(dir, "GeneratedDiagnostics")))
                .FirstOrDefault();
            if (latest is not null)
            {
                return latest;
            }
        }

        throw new InvalidOperationException(
            $"Could not locate active compiler intermediate generated directories under {objRoot}.");
    }

    private static bool HasRequiredGeneratedDirectories(string intermediateDir) =>
        Directory.Exists(Path.Combine(intermediateDir, "GeneratedDialects")) &&
        Directory.Exists(Path.Combine(intermediateDir, "GeneratedDiagnostics")) &&
        Directory.Exists(Path.Combine(intermediateDir, "GeneratedPrelude")) &&
        Directory.Exists(Path.Combine(intermediateDir, "GeneratedInternalNames"));

    private static string NormalizeDirectorySeparators(string path) =>
        string.Create(path.Length, path, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = source[i] is '\\' or '/' ? Path.DirectorySeparatorChar : source[i];
            }
        });
}
