using System.Collections.Immutable;
using System.IO;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using VerifyXunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

public abstract class AnalyzerTestBase
{
    protected async Task VerifyAnalyzerFromFileAsync(string relativeFilePath, DiagnosticAnalyzer analyzer,
        string testName, string assemblyName = "TestAssembly")
    {
        var (analyzerDiagnostics, _) = await GetAnalyzerDiagnosticsFromFileAsync(
            relativeFilePath,
            analyzer,
            assemblyName);

        var output = analyzerDiagnostics.Count > 0
            ? string.Join(
                Environment.NewLine,
                analyzerDiagnostics.Select(static d => $"{d.Id} at {d.Location.GetLineSpan().Span}: {d.GetMessage()}"))
            : "No Diagnostics";

        await Verifier.Verify(output, "txt")
            .UseDirectory("Snapshots")
            .UseFileName($"Snap.{testName}");
    }

    protected Task<(IReadOnlyList<Diagnostic> AnalyzerDiagnostics, IReadOnlyList<Diagnostic> CompilerErrors)>
        GetAnalyzerDiagnosticsFromFileAsync(
            string relativeFilePath,
            DiagnosticAnalyzer analyzer,
            string assemblyName = "TestAssembly") =>
        GetAnalyzerDiagnosticsAsync(
            RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase("Analyzers/" + relativeFilePath)),
            analyzer,
            assemblyName);

    // The test case alone, without the global usings of the shared test sources, as an assembly without unsafe code
    protected static async Task<(IReadOnlyList<Diagnostic> AnalyzerDiagnostics, IReadOnlyList<Diagnostic> CompilerErrors)>
        GetAnalyzerDiagnosticsAsync(
            CSharpCompilation compilation,
            DiagnosticAnalyzer analyzer,
            string assemblyName = "TestAssembly")
    {
        compilation = compilation
            .WithAssemblyName(assemblyName)
            .WithOptions(compilation.Options.WithAllowUnsafe(false));

        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
        var diagnostics = await compilationWithAnalyzers.GetAllDiagnosticsAsync();

        var analyzerDiagnostics = diagnostics
            .Where(IsAnalyzerDiagnostic)
            .OrderBy(static d => d.Location.SourceSpan.Start)
            .ToList();

        var compilerErrors = diagnostics
            .Where(static d => d.Severity == DiagnosticSeverity.Error && !IsAnalyzerDiagnostic(d))
            .ToList();

        return (analyzerDiagnostics, compilerErrors);
    }

    private static bool IsAnalyzerDiagnostic(Diagnostic diagnostic) =>
        diagnostic.Id.StartsWith("GSH") || diagnostic.Id.StartsWith("HNT") || diagnostic.Id.StartsWith("SLQ");
}
