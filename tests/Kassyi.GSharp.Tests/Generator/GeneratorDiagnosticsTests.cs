using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VerifyXunit;
using Xunit;

namespace Kassyi.GSharp.Tests;

public class GeneratorDiagnosticsTests
{
    private (GeneratorDriver Driver, Compilation Compilation, ImmutableArray<Diagnostic>
        Diagnostics) RunGenerator(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp10);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(GpuKernelAttribute).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(SharedMemory<int>).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(MethodImplOptions).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location));

        var compilation = CSharpCompilation.Create("TestCompilation")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddReferences(references)
            .AddSyntaxTrees(syntaxTree);

        var assembly = System.AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Kassyi.GSharp.Compiler");
        if (assembly == null)
        {
            assembly = Assembly.LoadFrom(
                @"..\..\..\..\..\src\Compiler\Kassyi.GSharp.Compiler\bin\Debug\netstandard2.0\Kassyi.GSharp.Compiler.dll");
        }

        var genType = assembly.GetType("Kassyi.GSharp.Compiler.GpuKernelGenerator");
        var generator = (IIncrementalGenerator)System.Activator.CreateInstance(genType)!;

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() },
            additionalTexts: null,
            parseOptions: parseOptions,
            optionsProvider: null);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        return (driver, outputCompilation, diagnostics);
    }

    [Fact]
    [Trait("FeatureId", "GEN-05")]
    public Task TestSadPathDiagnostics()
    {
        var source = """
            using Kassyi.GSharp.Core;
            namespace TestSpace
            {
                public static class ErrorSpecKernel
                {
                    [GpuKernel]
                    public static int BadKernel()
                    {
                        string s = "hello";
                        object obj = new object();
                        return 0;
                    }
                }
            }
            """;
        var (_, _, diagnostics) = RunGenerator(source);
        var gsharpDiagnostics = diagnostics.Where(d => d.Id.StartsWith("GSH"));
        return Verifier.Verify(gsharpDiagnostics);
    }

    [Fact]
    [Trait("FeatureId", "GEN-06")]
    public Task TestComprehensiveSpecKernelGeneratedSource()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream =
            assembly.GetManifestResourceStream("Kassyi.GSharp.Tests.TestCases.ComprehensiveSpecKernel.cs");
        using var reader = new System.IO.StreamReader(stream!);
        var source = reader.ReadToEnd();

        var (driver, compilation, diagnostics) = RunGenerator(source);

        var runResult = driver.GetRunResult();
        var sb = new System.Text.StringBuilder();
        foreach (var tree in runResult.GeneratedTrees)
        {
            sb.AppendLine("TREE: " + tree.FilePath);
            sb.AppendLine(tree.GetText().ToString());
        }

        var compDiagnostics = compilation.GetDiagnostics();
        foreach (var result in runResult.Results)
        {
            if (result.Exception != null)
            {
                sb.AppendLine("GENERATOR EXCEPTION: " + result.Exception.ToString());
            }
        }

        if (sb.Length == 0)
        {
            sb.AppendLine("NO CODE GENERATED.");
            sb.AppendLine("Generator Diagnostics:");
            sb.AppendLine(string.Join("\n", diagnostics));
            sb.AppendLine("Compilation Diagnostics:");
            sb.AppendLine(string.Join("\n", compDiagnostics));
        }

        var generatedCode = sb.ToString().Replace("\r\n", "\n");
        return Verifier.Verify(generatedCode);
    }

    [Fact]
    [Trait("Category", "Diagnostics")]
    [Trait("FeatureId", "GEN-07")]
    public void Test_UnsupportedFeatures_ReportsDiagnostics()
    {
        var source = """
            using Kassyi.GSharp.Core;
            namespace TestSpace
            {
                public class ManagedClass {}

                public static class ErrorKernels
                {
                    [GpuKernel]
                    public static int NonVoidReturnKernel()
                    {
                        return 1;
                    }

                    [GpuKernel]
                    public static void ManagedParamKernel(ManagedClass obj)
                    {
                    }
                }
            }
            """;
        var (_, _, diagnostics) = RunGenerator(source);
        var gsharpDiagnostics = diagnostics.Where(d => d.Id.StartsWith("GSH")).ToList();

        Assert.NotEmpty(gsharpDiagnostics);
        Assert.True(gsharpDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));
    }
}
