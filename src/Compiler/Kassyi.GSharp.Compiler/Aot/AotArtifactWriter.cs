using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Aot;

/// <summary>
/// Writes the AOT artifact (<c>Kassyi.GSharp.Kernels_{assembly}.g.cu</c> / <c>.g.cpp</c> with its source hash header,
/// and the <c>.gsym.json</c> symbol file) to <c>$(IntermediateOutputPath)</c>. The only place in the generator that
/// does file I/O; see generator-invariants §7 for why IDE design-time compilations skip the write.
/// </summary>
internal static class AotArtifactWriter
{
    /// <summary>Writes the artifact for <paramref name="bodySource"/>, unless this is an IDE design-time compilation.</summary>
    public static void Write(Compilation compilation, GeneratorConfig config, string bodySource,
        SourceProductionContext context)
    {
        if (IsDesignTimeCompilation(compilation))
        {
            return;
        }

        var safeAssemblyName = AotHostNames.ToSafeIdentifier(compilation.AssemblyName ?? "UnknownAssembly");
        var sourceHash = GSharpNameEncoding.ComputeSourceHash(bodySource);
        var fullSource = GSharpNameEncoding.FormatSymbolHeaderLine(sourceHash) + "\n" + bodySource;
        var dialectName = config.IsCuda ? "Cuda" : "Cpu";
        var gsymContent = GSharpNameEncoding.BuildSymbolFileContent(bodySource, dialectName, static id => GlobalCppNames.TryGetFullName(id, out var full) ? full : null);

        var ext = config.IsCuda ? ".g.cu" : ".g.cpp";
        var fileName = $"Kassyi.GSharp.Kernels_{safeAssemblyName}{ext}";
        var safeIntermediateOutputPath = config.IntermediateOutputPath ?? "";
        if (string.IsNullOrEmpty(safeIntermediateOutputPath))
        {
            return;
        }

        var filePath = Path.Combine(safeIntermediateOutputPath, fileName);
        var gsymFilePath = filePath + ".gsym.json";

#pragma warning disable RS1035 // Do not do file IO in analyzers
        try
        {
            if (!Directory.Exists(safeIntermediateOutputPath))
            {
                Directory.CreateDirectory(safeIntermediateOutputPath);
            }

            File.WriteAllText(filePath, fullSource);
            File.WriteAllText(gsymFilePath, gsymContent);
        }
        catch (Exception ex)
        {
            // Ignore IO errors on best-effort basis, but report them as an info diagnostic
            var diagnostic = Diagnostic.Create(
                GSharpDiagnostics.AotArtifactWriteFailed,
                Location.None,
                filePath,
                ex.Message);
            context.ReportDiagnostic(diagnostic);
        }
#pragma warning restore RS1035
    }

    private static bool IsDesignTimeCompilation(Compilation compilation)
    {
        var domainName = AppDomain.CurrentDomain.FriendlyName;
        if ((domainName.IndexOf("Roslyn.Worker", StringComparison.OrdinalIgnoreCase) >= 0 ||
             domainName.IndexOf("JetBrains", StringComparison.OrdinalIgnoreCase) >= 0 ||
             domainName.IndexOf("ServiceHub", StringComparison.OrdinalIgnoreCase) >= 0 ||
             domainName.IndexOf("LanguageServer", StringComparison.OrdinalIgnoreCase) >= 0 ||
             domainName.IndexOf("OmniSharp", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return true;
        }

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        if ((baseDir.IndexOf("resharper", StringComparison.OrdinalIgnoreCase) >= 0 ||
             baseDir.IndexOf("jetbrains", StringComparison.OrdinalIgnoreCase) >= 0 ||
             baseDir.IndexOf("ServiceHub", StringComparison.OrdinalIgnoreCase) >= 0))
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
}
