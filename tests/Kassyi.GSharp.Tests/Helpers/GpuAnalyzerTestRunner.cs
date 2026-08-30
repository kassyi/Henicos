using System.Collections.Immutable;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Tests.Helpers;

public static class GpuAnalyzerTestRunner
{
    public static string GetTestCasesDirectory([CallerFilePath] string callerFilePath = "")
    {
        var fileDir = Path.GetDirectoryName(callerFilePath);
        if (string.IsNullOrEmpty(fileDir))
        {
            throw new DirectoryNotFoundException("Could not determine test project directory.");
        }
        var projectDir = Path.GetFullPath(Path.Combine(fileDir, ".."));
        var testCasesDir = Path.Combine(projectDir, "TestCases");
        if (!Directory.Exists(testCasesDir))
        {
            throw new DirectoryNotFoundException($"TestCases directory not found at: {testCasesDir}");
        }
        return testCasesDir;
    }

    public static async Task<(SyntaxTree SyntaxTree, SemanticModel SemanticModel)> GetParsedModelAsync(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp11);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = MegaCompilationCache.BaseCompilation.AddSyntaxTrees(syntaxTree);
        var semanticModel = compilation.GetSemanticModel(syntaxTree);

        return await Task.FromResult((syntaxTree, semanticModel));
    }

    public static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source, DiagnosticAnalyzer analyzer)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp11);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = MegaCompilationCache.BaseCompilation.AddSyntaxTrees(syntaxTree);
        
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
