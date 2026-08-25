using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.Math.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Tests.Helpers;

public static class RoslynTestUtility
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

    public static Compilation CreateCompilation(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp10);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        // Add typical GSharp references
        references.Add(MetadataReference.CreateFromFile(typeof(GpuBigIntExpr).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(GpuKernelAttribute).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(SharedMemory<int>).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(MethodImplOptions).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location));

        return CSharpCompilation.Create("TestCompilation")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddReferences(references)
            .AddSyntaxTrees(syntaxTree);
    }

    public static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source, DiagnosticAnalyzer analyzer)
    {
        var compilation = CreateCompilation(source);
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    public static (GeneratorDriver Driver, Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(string source)
    {
        var compilation = CreateCompilation(source);
        
        var assembly = System.AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Kassyi.GSharp.Compiler");
        if (assembly == null)
        {
            // Dynamically load compiler if not already loaded (typical for tests that don't directly reference it as library)
            var compilerPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\src\Compiler\Kassyi.GSharp.Compiler\bin\Debug\netstandard2.0\Kassyi.GSharp.Compiler.dll"));
            assembly = Assembly.LoadFrom(compilerPath);
        }

        var genType = assembly.GetType("Kassyi.GSharp.Compiler.GpuKernelGenerator");
        var generator = (IIncrementalGenerator)System.Activator.CreateInstance(genType)!;

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() },
            additionalTexts: null,
            parseOptions: new CSharpParseOptions(LanguageVersion.CSharp10),
            optionsProvider: null);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        return (driver, outputCompilation, diagnostics);
    }
}
