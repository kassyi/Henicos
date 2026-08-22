using System.Reflection;
using Kassyi.GSharp.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VerifyXunit;
using Xunit;
using System.Linq;

namespace Kassyi.GSharp.Tests;

public class GeneratorDiagnosticsTests
{
    [Fact]
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
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(GpuKernelAttribute).Assembly.Location)
        };

        var compilation = CSharpCompilation.Create("TestCompilation")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddReferences(references)
            .AddSyntaxTrees(syntaxTree);

        var assembly = System.AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Kassyi.Generators.GSharp");
        if (assembly == null)
        {
            assembly = Assembly.LoadFrom(
                @"..\..\..\..\..\src\Kassyi.Generators.GSharp\bin\Debug\netstandard2.0\Kassyi.Generators.GSharp.dll");
        }

        var genType = assembly.GetType("Kassyi.Generators.GSharp.GpuKernelGenerator");
        var generator = (IIncrementalGenerator)System.Activator.CreateInstance(genType)!;

        var driver = CSharpGeneratorDriver.Create(generator);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        var gsharpDiagnostics = diagnostics.Where(d => d.Id.StartsWith("GSH"));
        return Verifier.Verify(gsharpDiagnostics);
    }

    }
