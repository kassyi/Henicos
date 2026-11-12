using System.Linq;
using Kassyi.GSharp.Compiler.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Compiler.Generators.Tests;

/// <summary>The result of one run of the generator tool's entry point.</summary>
internal sealed record GeneratorResult(int ExitCode, string StandardError);

/// <summary>
/// Runs <see cref="Program.Main"/> in process, the way the MSBuild targets run the tool, with standard error captured.
/// Console redirection is process-wide, so every test class that uses it is in
/// <see cref="TestCollections.GlobalStateSequential"/>.
/// </summary>
internal static class GeneratorRun
{
    public static GeneratorResult Run(params string[] args)
    {
        var error = new StringWriter();
        var previous = Console.Error;
        Console.SetError(error);
        try
        {
            var exitCode = Program.Main(args);
            return new GeneratorResult(exitCode, error.ToString());
        }
        finally
        {
            Console.SetError(previous);
        }
    }

    /// <summary>Parses generated C# and fails with the syntax errors, if any.</summary>
    public static CompilationUnitSyntax ParseWithoutErrors(string source)
    {
#pragma warning disable RS0030 // Category 8: Test tooling parses external generated C# source file
        var tree = CSharpSyntaxTree.ParseText(source);
#pragma warning restore RS0030
        var errors = tree.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString())
            .ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors) + Environment.NewLine + source);
        return tree.GetCompilationUnitRoot();
    }

    /// <summary>The values of every string literal in <paramref name="root"/>, as the C# compiler reads them.</summary>
    public static List<string> StringLiteralValues(SyntaxNode root) =>
        root.DescendantNodes()
            .OfType<LiteralExpressionSyntax>()
            .Where(static l => l.IsKind(SyntaxKind.StringLiteralExpression))
            .Select(static l => l.Token.ValueText)
            .ToList();
}

/// <summary>A directory under the system temporary directory, deleted on dispose.</summary>
internal sealed class ScratchDirectory : IDisposable
{
    public ScratchDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gsharp-generator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    // Full path with the platform separator, as the generators print paths they found by a directory search.
    public string Combine(params string[] parts) => System.IO.Path.GetFullPath(System.IO.Path.Combine([Path, .. parts]));

    /// <summary>Writes <paramref name="text"/> to <paramref name="relativePath"/>, creating its directory.</summary>
    public string Write(string relativePath, string text)
    {
        var path = Combine(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? Path);
        File.WriteAllText(path, text);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
