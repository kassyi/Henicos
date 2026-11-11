#nullable enable
using System.Globalization;
using Kassyi.ZeroAlloc.Linq;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>Who reported a diagnostic in a <see cref="KernelDiagnostics.RunAsync"/> run.</summary>
public enum DiagnosticOrigin
{
    Analyzer,
    Generator
}

/// <summary>
/// A diagnostic of a <see cref="KernelDiagnostics.RunAsync"/> run: its reporter, ID, range and message. Lines and columns
/// are 1-based; a diagnostic without a source location has 0 for all four.
/// </summary>
public readonly record struct ReportedDiagnostic(
    DiagnosticOrigin Origin,
    string Id,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    string Message)
{
    public bool HasLocation => StartLine > 0;

    public static ReportedDiagnostic From(DiagnosticOrigin origin, Diagnostic diagnostic)
    {
        if (!diagnostic.Location.IsInSource)
        {
            return new ReportedDiagnostic(origin, diagnostic.Id, 0, 0, 0, 0, diagnostic.GetMessage());
        }

        var span = diagnostic.Location.GetLineSpan().Span;
        return new ReportedDiagnostic(
            origin,
            diagnostic.Id,
            span.Start.Line + 1,
            span.Start.Character + 1,
            span.End.Line + 1,
            span.End.Character + 1,
            diagnostic.GetMessage());
    }

    public override string ToString()
    {
        var range = HasLocation ? $"({StartLine},{StartColumn})-({EndLine},{EndColumn})" : "<no location>";
        var originText = Origin == DiagnosticOrigin.Analyzer ? "analyzer" : "generator";
        return $"{range} {Id} {originText}: {Message}";
    }
}

/// <summary>One run of the kernel analyzers and the generator: the input and output compilations and each one's diagnostics.</summary>
public sealed record KernelDiagnosticsRun(
    Compilation Input,
    Compilation Output,
    ImmutableArray<Diagnostic> Analyzer,
    ImmutableArray<Diagnostic> Generator);

/// <summary>
/// Runs the G# kernel analyzers and <see cref="GpuKernelGenerator"/> on one source, as a build of a project with the
/// given <c>GSharpTargetDialect</c> does, and returns every diagnostic of both with its reporter.
/// </summary>
/// <remarks>
/// A test that pins where a diagnostic comes from, and how many times it is reported, uses this instead of running the
/// analyzer or the generator alone. Compiler errors of the source itself (CSxxxx) are not included.
/// </remarks>
public static class KernelDiagnostics
{
    /// <summary>The analyzers that check kernels and device functions.</summary>
    public static ImmutableArray<DiagnosticAnalyzer> Analyzers { get; } =
    [
        new GpuKernelAnalyzer(),
        new KernelClassAnalyzer(),
        new BarrierUsageAnalyzer(),
        new ParallelDependentCallAnalyzer()
    ];

    /// <summary>
    /// Runs the analyzers and the generator on <paramref name="source"/> for <paramref name="dialect"/> and returns the
    /// diagnostics of each, with the syntax tree of the source.
    /// </summary>
    public static async Task<(SyntaxTree Tree, ImmutableArray<Diagnostic> Analyzer, ImmutableArray<Diagnostic> Generator)>
        GetDiagnosticsAsync(string source, string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(source);
        var run = await RunCompilationAsync(compilation, dialect);
        return (compilation.SyntaxTrees.Last(), run.Analyzer, run.Generator);
    }

    /// <summary>Runs the analyzers and the generator on <paramref name="compilation"/> for <paramref name="dialect"/>.</summary>
    public static async Task<KernelDiagnosticsRun> RunCompilationAsync(Compilation compilation, string dialect)
    {
        var options = new TestAnalyzerConfigOptionsProvider(dialect);

        var analyzerOptions = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, options);
        var analyzerDiagnostics = await compilation
            .WithAnalyzers(Analyzers, analyzerOptions)
            .GetAnalyzerDiagnosticsAsync();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new GpuKernelGenerator().AsSourceGenerator()],
            additionalTexts: null,
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
            optionsProvider: options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        return new KernelDiagnosticsRun(compilation, output, analyzerDiagnostics, generatorDiagnostics);
    }

    /// <summary>
    /// The failures that the generator states as <c>#error</c> in its output (kernel sources and device code), as
    /// <c>path(line,column): ID</c>, each paired with whether the analyzers report the same ID at the same place.
    /// </summary>
    public static IReadOnlyList<(string Failure, bool IsReported)> FindStatedFailures(KernelDiagnosticsRun run)
    {
        var inputTrees = run.Input.SyntaxTrees.ToHashSet();
        var failures = new List<(string, bool)>();
        const string ErrorMarker = "#error \"\"";

        foreach (var tree in run.Output.SyntaxTrees.Where(tree => !inputTrees.Contains(tree)))
        {
            var text = tree.ToString();
            foreach (var lineSpan in text.AsSpan().EnumerateLines())
            {
                var trimmed = lineSpan.Trim();
                int markerIdx = trimmed.IndexOf(ErrorMarker, StringComparison.Ordinal);
                if (markerIdx < 0)
                {
                    continue;
                }

                var rest = trimmed[(markerIdx + ErrorMarker.Length)..];
                if (rest.StartsWith("<no original span>: ", StringComparison.Ordinal))
                {
                    var idSpan = rest["<no original span>: ".Length..];
                    int colonIdx = idSpan.IndexOf(':');
                    var id = colonIdx >= 0 ? idSpan[..colonIdx].Trim().ToString() : idSpan.Trim().ToString();
                    failures.Add(($"{TranspileFailure.NoOriginalSpan}: {id}", false));
                    continue;
                }

                int openParen = rest.IndexOf('(');
                int comma = rest.IndexOf(',');
                int closeParen = rest.IndexOf(')');
                int colon = rest.IndexOf(": ", StringComparison.Ordinal);

                if (openParen >= 0 && comma > openParen && closeParen > comma && colon > closeParen)
                {
                    var path = rest[..openParen].ToString().Replace("\\\\", "\\", StringComparison.Ordinal);
                    var line = int.Parse(rest[(openParen + 1)..comma], CultureInfo.InvariantCulture);
                    var column = int.Parse(rest[(comma + 1)..closeParen], CultureInfo.InvariantCulture);
                    var afterColon = rest[(colon + 2)..];
                    int idEnd = afterColon.IndexOf(':');
                    var id = idEnd >= 0 ? afterColon[..idEnd].Trim().ToString() : afterColon.Trim().ToString();

                    var isReported = run.Analyzer.AsSpan().AsStackEnumerable().Any(d =>
                        d.Id == id &&
                        d.Location.IsInSource &&
                        d.Location.GetLineSpan() is var span &&
                        span.Path == path &&
                        span.StartLinePosition.Line + 1 == line &&
                        span.StartLinePosition.Character + 1 == column);
                    failures.Add(($"{path}({line},{column}): {id}", isReported));
                }
            }
        }

        return failures.Distinct().ToList();
    }

    /// <summary>Runs the analyzers and the generator on <paramref name="source"/> for <paramref name="dialect"/>.</summary>
    /// <returns>The diagnostics ordered by position, then ID, then reporter.</returns>
    public static async Task<ImmutableArray<ReportedDiagnostic>> RunAsync(string source, string dialect)
    {
        var (_, analyzerDiagnostics, generatorDiagnostics) = await GetDiagnosticsAsync(source, dialect);

        return analyzerDiagnostics
            .Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Analyzer, d))
            .Concat(generatorDiagnostics.Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Generator, d)))
            .OrderBy(static d => d.StartLine)
            .ThenBy(static d => d.StartColumn)
            .ThenBy(static d => d.EndLine)
            .ThenBy(static d => d.EndColumn)
            .ThenBy(static d => d.Id, StringComparer.Ordinal)
            .ThenBy(static d => d.Origin)
            .ThenBy(static d => d.Message, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    /// <summary>Runs <see cref="RunAsync"/> on a test case file, given relative to the TestCases directory.</summary>
    public static Task<ImmutableArray<ReportedDiagnostic>> RunTestCaseAsync(string relativePathToTestCases, string dialect) =>
        RunAsync(RoslynTestUtility.ReadTestCase(relativePathToTestCases), dialect);

    /// <summary>
    /// The reporter and range (start line, start column, end line, end column) of each diagnostic with
    /// <paramref name="id"/>, and with <paramref name="messagePart"/> in its message when one is given, in order.
    /// </summary>
    public static IReadOnlyList<(DiagnosticOrigin Origin, int StartLine, int StartColumn, int EndLine, int EndColumn)> Ranges(
        this ImmutableArray<ReportedDiagnostic> diagnostics, string id, string? messagePart = null) =>
        diagnostics
            .Where(d => d.Id == id && (messagePart == null || d.Message.Contains(messagePart, StringComparison.Ordinal)))
            .Select(static d => (d.Origin, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn))
            .ToList();
}
