using System.Collections.Immutable;
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
        var compilation = RoslynTestUtility.GetTestCaseContext("Analyzers/" + relativeFilePath)
            .Compilation
            .WithAssemblyName(assemblyName);

        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
        var diagnostics = await compilationWithAnalyzers.GetAllDiagnosticsAsync();

        var analyzerDiagnostics = diagnostics
            .Where(d => d.Id.StartsWith("GSH") || d.Id.StartsWith("HNT") || d.Id.StartsWith("SLQ"))
            .OrderBy(d => d.Location.SourceSpan.Start)
            .Select(d => $"{d.Id} at {d.Location.GetLineSpan().Span}: {d.GetMessage()}")
            .ToList();

        var output = analyzerDiagnostics.Count > 0
            ? string.Join(Environment.NewLine, analyzerDiagnostics)
            : "No Diagnostics";

        await Verifier.Verify(output, "txt")
            .UseDirectory("Snapshots")
            .UseFileName($"Snap.{testName}");
    }
}
