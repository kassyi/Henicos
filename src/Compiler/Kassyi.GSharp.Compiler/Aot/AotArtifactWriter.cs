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
    /// <param name="errorSites">The error sites recorded in the module, or null.</param>
    /// <param name="partitionJson">The partition plan JSON string, or null.</param>
    public static void Write(in AotTarget target, GeneratorConfig config, string bodySource,
        SourceProductionContext context, Func<string, string?>? findImportedFullName = null,
        IReadOnlyList<GSharpNameEncoding.SymbolErrorSiteRecord>? errorSites = null,
        string? partitionJson = null)
    {
        var ext = config.IsCuda ? ".g.cu" : ".g.cpp";
        var fileName = AotModuleNames.GetArtifactFileStem(target.ModuleName) + ext;
        var files = new NativeModuleFiles(null, [new NativeModuleFile(fileName, bodySource, IncludeErrorSites: true)]);
        Write(in target, config, files, context, findImportedFullName, errorSites, partitionJson);
    }

    /// <summary>
    /// Writes the split or single-unit <paramref name="files"/> with sidecar <c>.gsym.json</c> files, optional
    /// partition JSON, and the unit manifest, unless this is an IDE design-time compilation.
    /// </summary>
    public static void Write(in AotTarget target, GeneratorConfig config, NativeModuleFiles files,
        SourceProductionContext context, Func<string, string?>? findImportedFullName = null,
        IReadOnlyList<GSharpNameEncoding.SymbolErrorSiteRecord>? errorSites = null,
        string? partitionJson = null) =>
        WriteCore(in target, config, files, context.ReportDiagnostic, findImportedFullName, errorSites, partitionJson);

    /// <summary>
    /// Writes <paramref name="files"/> and reports I/O failures through <paramref name="reportDiagnostic"/>.
    /// </summary>
    internal static void WriteCore(in AotTarget target, GeneratorConfig config, NativeModuleFiles files,
        Action<Diagnostic> reportDiagnostic, Func<string, string?>? findImportedFullName = null,
        IReadOnlyList<GSharpNameEncoding.SymbolErrorSiteRecord>? errorSites = null,
        string? partitionJson = null)
    {
        if (target.IsDesignTimeCompilation || config.IntermediateOutputPath is not { Length: > 0 } directory)
        {
            return;
        }

        if (files.Units.IsDefaultOrEmpty)
        {
            throw new ArgumentException("At least one translation unit is required.", nameof(files));
        }

        var dialectName = config.IsCuda ? "Cuda" : "Cpu";
        var ext = config.IsCuda ? ".g.cu" : ".g.cpp";
        var stem = AotModuleNames.GetArtifactFileStem(target.ModuleName);

        var fileCount = (files.Header.HasValue ? 2 : 0)
            + files.Units.Length * 2
            + (partitionJson is not null ? 1 : 0)
            + 1;
        var writeItems = new (string Path, string Content)[fileCount];
        var itemIndex = 0;

        if (files.Header is { } header)
        {
            var (sourceItem, symbolItem) = BuildArtifactPair(
                directory, header, dialectName, findImportedFullName, errorSites);
            writeItems[itemIndex++] = sourceItem;
            writeItems[itemIndex++] = symbolItem;
        }

        using var manifestWriter = new SourceWriter();
        foreach (var unit in files.Units)
        {
            var (sourceItem, symbolItem) = BuildArtifactPair(
                directory, unit, dialectName, findImportedFullName, errorSites);
            writeItems[itemIndex++] = sourceItem;
            writeItems[itemIndex++] = symbolItem;
            manifestWriter.Append(unit.FileName).Append('\n');
        }

        if (partitionJson is not null)
        {
            writeItems[itemIndex++] = (Path.Combine(directory, stem + ".partition.json"), partitionJson);
        }

        // The manifest goes last: when an earlier write fails, the manifest still names the sources of the earlier
        // build instead of sources that were not written
        writeItems[itemIndex] = (Path.Combine(directory, ManifestFileName), manifestWriter.ToString());

        WriteFiles(directory, reportDiagnostic, writeItems, stem, ext, files);
    }

    private static ((string Path, string Content) Source, (string Path, string Content) SymbolFile) BuildArtifactPair(
        string directory,
        NativeModuleFile file,
        string dialectName,
        Func<string, string?>? findImportedFullName,
        IReadOnlyList<GSharpNameEncoding.SymbolErrorSiteRecord>? errorSites)
    {
        var sourceHash = GSharpNameEncoding.ComputeSourceHash(file.BodySource);
        var fullSource = GSharpNameEncoding.FormatSymbolHeaderLine(sourceHash) + "\n" + file.BodySource;
        var gsymContent = GSharpNameEncoding.BuildSymbolFileContent(
            file.BodySource,
            dialectName,
            id => GlobalCppNames.TryGetFullName(id, out var full) ? full : findImportedFullName?.Invoke(id),
            file.IncludeErrorSites ? errorSites : null);
        var filePath = Path.Combine(directory, file.FileName);
        return ((filePath, fullSource), (filePath + ".gsym.json", gsymContent));
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

        var stem = AotModuleNames.GetArtifactFileStem(target.ModuleName);
        WriteFiles(
            directory,
            context.ReportDiagnostic,
            [(Path.Combine(directory, ManifestFileName), "")],
            stem: stem,
            removePartitionFile: true);
    }

    // Writes the files in order, deletes any stale split or partition files when requested, and stops at first failure
    private static void WriteFiles(
        string directory,
        Action<Diagnostic> reportDiagnostic,
        (string Path, string Content)[] files,
        string stem = "",
        string ext = "",
        NativeModuleFiles? splitFiles = null,
        bool removePartitionFile = false)
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
                if (!HasSameContent(path, content))
                {
                    File.WriteAllText(path, content);
                }
            }

            if (splitFiles is not null)
            {
                CleanStaleSplitFiles(directory, stem, ext, splitFiles, ref currentPath);
            }

            if (removePartitionFile)
            {
                DeleteIfExists(Path.Combine(directory, stem + ".partition.json"), ref currentPath);
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
            reportDiagnostic(diagnostic);
        }
#pragma warning restore RS1035
    }

#pragma warning disable RS1035 // Do not do file IO in analyzers
    private static void CleanStaleSplitFiles(
        string directory,
        string stem,
        string ext,
        NativeModuleFiles splitFiles,
        ref string currentPath)
    {
        if (splitFiles.Header is null)
        {
            var staleHeaderPath = Path.Combine(directory, stem + ".g.hpp");
            DeleteIfExists(staleHeaderPath, ref currentPath);
            DeleteIfExists(staleHeaderPath + ".gsym.json", ref currentPath);
        }

        for (var i = Math.Max(1, splitFiles.Units.Length); ; i++)
        {
            var staleUnitPath = Path.Combine(directory, $"{stem}.{i}{ext}");
            var staleSymbolPath = staleUnitPath + ".gsym.json";
            var unitExists = File.Exists(staleUnitPath);
            var symbolExists = File.Exists(staleSymbolPath);
            if (!unitExists && !symbolExists && i >= NativeModulePartitioner.MaxUnits)
            {
                break;
            }

            if (unitExists)
            {
                currentPath = staleUnitPath;
                File.Delete(staleUnitPath);
            }

            if (symbolExists)
            {
                currentPath = staleSymbolPath;
                File.Delete(staleSymbolPath);
            }
        }
    }

    private static void DeleteIfExists(string path, ref string currentPath)
    {
        if (File.Exists(path))
        {
            currentPath = path;
            File.Delete(path);
        }
    }
#pragma warning restore RS1035

#pragma warning disable RS1035 // Do not do file IO in analyzers
    private static bool HasSameContent(string path, string content)
    {
        var info = new FileInfo(path);
        return info.Exists &&
               info.Length == Encoding.UTF8.GetByteCount(content) &&
               string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal);
    }
#pragma warning restore RS1035

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
