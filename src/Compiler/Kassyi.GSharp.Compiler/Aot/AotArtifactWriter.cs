using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Aot;

/// <summary>
/// Writes the AOT artifact (<c>Kassyi.GSharp.Kernels_{assembly}.g.cu</c> / <c>.g.cpp</c> with its source hash header,
/// the <c>.gsym.json</c> symbol file and the manifest <see cref="ManifestFileName"/>) to <c>$(IntermediateOutputPath)</c>.
/// The only place in the generator that does file I/O; see generator-invariants §7 for why IDE design-time compilations
/// skip the write.
/// </summary>
internal static class AotArtifactWriter
{
    /// <summary>
    /// The file in <c>$(IntermediateOutputPath)</c> that names the native sources of the last generator run, one file
    /// name (relative to <c>$(IntermediateOutputPath)</c>) per line; empty when the assembly has no kernels.
    /// <c>Kassyi.GSharp.Compiler.targets</c> compiles only these sources and adds them to <c>FileWrites</c>, so that
    /// MSBuild's <c>IncrementalClean</c> deletes the sources of an earlier build (another dialect, another assembly name).
    /// </summary>
    public const string ManifestFileName = "Kassyi.GSharp.Kernels.artifacts";

    /// <summary>Writes the artifact for <paramref name="bodySource"/>, unless this is an IDE design-time compilation.</summary>
    /// <param name="target">Where the artifact goes.</param>
    /// <param name="config">The generator options.</param>
    /// <param name="bodySource">The module.</param>
    /// <param name="context">The output context.</param>
    /// <param name="findImportedFullName">The full name of a shortened name that another assembly recorded, or null.</param>
    public static void Write(in AotTarget target, GeneratorConfig config, string bodySource,
        SourceProductionContext context, Func<string, string?>? findImportedFullName = null)
    {
        if (target.IsDesignTimeCompilation)
        {
            return;
        }

        var safeAssemblyName = AotHostNames.ToSafeIdentifier(target.AssemblyName);
        var sourceHash = GSharpNameEncoding.ComputeSourceHash(bodySource);
        var fullSource = GSharpNameEncoding.FormatSymbolHeaderLine(sourceHash) + "\n" + bodySource;
        var dialectName = config.IsCuda ? "Cuda" : "Cpu";
        var gsymContent = GSharpNameEncoding.BuildSymbolFileContent(bodySource, dialectName,
            id => GlobalCppNames.TryGetFullName(id, out var full) ? full : findImportedFullName?.Invoke(id));

        var ext = config.IsCuda ? ".g.cu" : ".g.cpp";
        var fileName = $"Kassyi.GSharp.Kernels_{safeAssemblyName}{ext}";
        var safeIntermediateOutputPath = config.IntermediateOutputPath ?? "";
        if (string.IsNullOrEmpty(safeIntermediateOutputPath))
        {
            return;
        }

        var filePath = Path.Combine(safeIntermediateOutputPath, fileName);
        var gsymFilePath = filePath + ".gsym.json";
        var manifestPath = Path.Combine(safeIntermediateOutputPath, ManifestFileName);

        // The manifest goes last: when an earlier write fails, the manifest still names the source of the earlier build
        // instead of a source that was not written
        WriteFiles(safeIntermediateOutputPath, context,
            (filePath, fullSource),
            (gsymFilePath, gsymContent),
            (manifestPath, fileName + "\n")
        );
    }

    /// <summary>
    /// Writes an empty manifest for an assembly without kernels, unless this is an IDE design-time compilation, so that
    /// the build compiles no native source of an earlier build.
    /// </summary>
    /// <param name="target">Where the manifest goes.</param>
    /// <param name="config">The generator options.</param>
    /// <param name="context">The output context.</param>
    public static void WriteEmptyManifest(in AotTarget target, GeneratorConfig config, SourceProductionContext context)
    {
        if (target.IsDesignTimeCompilation || config.IntermediateOutputPath is not { Length: > 0 } directory)
        {
            return;
        }

        WriteFiles(directory, context, (Path.Combine(directory, ManifestFileName), ""));
    }

    // Writes the files in order and stops at the first failure, which it reports as an info diagnostic naming that file
    private static void WriteFiles(string directory, SourceProductionContext context,
        params (string Path, string Content)[] files)
    {
        var currentPath = files[0].Path;
#pragma warning disable RS1035 // Do not do file IO in analyzers
        try
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            foreach (var (path, content) in files)
            {
                currentPath = path;
                File.WriteAllText(path, content);
            }
        }
        catch (Exception ex)
        {
            // Ignore IO errors on best-effort basis, but report them as an info diagnostic
            var diagnostic = Diagnostic.Create(
                GSharpDiagnostics.AotArtifactWriteFailed,
                Location.None,
                currentPath,
                ex.Message);
            context.ReportDiagnostic(diagnostic);
        }
#pragma warning restore RS1035
    }

    /// <summary>Whether <paramref name="compilation"/> runs in an IDE worker, which must not overwrite the artifacts.</summary>
    public static bool IsDesignTimeCompilation(Compilation compilation)
    {
        if (IsToolHost(AppDomain.CurrentDomain.FriendlyName, AppDomain.CurrentDomain.BaseDirectory))
        {
            return true;
        }

        foreach (var reference in compilation.References)
        {
            if (reference is CompilationReference)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the host process, named by its app domain and base directory, is an IDE worker or Stryker.NET. Such a host
    /// must not write the artifacts: Stryker compiles mutated trees in process, and the files it would write replace the real ones.
    /// </summary>
    internal static bool IsToolHost(string domainName, string baseDir)
    {
        return domainName.IndexOf("Roslyn.Worker", StringComparison.OrdinalIgnoreCase) >= 0 ||
               domainName.IndexOf("JetBrains", StringComparison.OrdinalIgnoreCase) >= 0 ||
               domainName.IndexOf("ServiceHub", StringComparison.OrdinalIgnoreCase) >= 0 ||
               domainName.IndexOf("LanguageServer", StringComparison.OrdinalIgnoreCase) >= 0 ||
               domainName.IndexOf("OmniSharp", StringComparison.OrdinalIgnoreCase) >= 0 ||
               domainName.IndexOf("Stryker", StringComparison.OrdinalIgnoreCase) >= 0 ||
               baseDir.IndexOf("resharper", StringComparison.OrdinalIgnoreCase) >= 0 ||
               baseDir.IndexOf("jetbrains", StringComparison.OrdinalIgnoreCase) >= 0 ||
               baseDir.IndexOf("ServiceHub", StringComparison.OrdinalIgnoreCase) >= 0 ||
               baseDir.IndexOf("stryker", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
