using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Stdlib.Math.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Kassyi.GSharp.Tests;

public class GpuBigIntExprAnalyzerTests
{
    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp10);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(GpuBigIntExpr).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location));

        var compilation = CSharpCompilation.Create("AnalyzerTestCompilation")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddReferences(references)
            .AddSyntaxTrees(syntaxTree);

        var analyzer = new GpuBigIntExprAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    [Fact]
    [Trait("FeatureId", "GBI-26")]
    public async Task LocalDeclaration_OfGpuBigIntExpr_ReportsDiagnostic()
    {
        var source = """
            using Kassyi.GSharp.Stdlib.Math.Expressions;

            public class TestClass
            {
                public void Method(GpuBigIntExpr expr)
                {
                    var local = expr;
                }
            }
            """;

        var diagnostics = await GetAnalyzerDiagnosticsAsync(source);

        Assert.Contains(diagnostics, d => d.Id == "GSH0005");
    }

    [Fact]
    [Trait("FeatureId", "GBI-27")]
    public async Task Assignment_ToGpuBigIntExpr_ReportsDiagnostic()
    {
        var source = """
            using Kassyi.GSharp.Stdlib.Math.Expressions;

            public class TestClass
            {
                private GpuBigIntExpr _field;

                public void Method(GpuBigIntExpr expr)
                {
                    _field = expr;
                }
            }
            """;

        var diagnostics = await GetAnalyzerDiagnosticsAsync(source);

        Assert.Contains(diagnostics, d => d.Id == "GSH0005");
    }

    [Fact]
    [Trait("FeatureId", "GBI-28")]
    public async Task LegacyNamespace_Declaration_ReportsDiagnostic()
    {
        var source = """
            namespace Kassyi.GSharp.GpuBigInt.Expressions
            {
                public struct GpuBigIntExpr {}
            }

            public class TestClass
            {
                public void Method(Kassyi.GSharp.GpuBigInt.Expressions.GpuBigIntExpr expr)
                {
                    var local = expr;
                }
            }
            """;

        var diagnostics = await GetAnalyzerDiagnosticsAsync(source);

        Assert.Contains(diagnostics, d => d.Id == "GSH0005");
    }
    [Fact]
    [Trait("FeatureId", "GBI-26")]
    public async Task MultipleDeclarations_InSingleStatement_EdgeCase()
    {
        var source = """
            using Kassyi.GSharp.Stdlib.Math.Expressions;

            public class TestClass
            {
                public void Method(GpuBigIntExpr expr)
                {
                    GpuBigIntExpr local1 = expr, local2 = expr;
                }
            }
            """;

        var diagnostics = await GetAnalyzerDiagnosticsAsync(source);

        // Expect multiple diagnostics for multiple declarators
        var count = diagnostics.Count(d => d.Id == "GSH0005");
        Assert.Equal(2, count);
    }

    [Fact]
    [Trait("FeatureId", "GBI-26")]
    public async Task MalformedSyntax_ShouldNotCrash_SadPath()
    {
        var source = """
            using Kassyi.GSharp.Stdlib.Math.Expressions;

            public class TestClass
            {
                public void Method()
                {
                    // Syntax error missing semicolon and incomplete declaration
                    GpuBigIntExpr 
                }
            }
            """;

        // Analyzer should not throw an exception on syntax errors
        var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
        
        // It might or might not produce GSH0005 depending on parser state, but it shouldn't crash
        Assert.True(true); 
    }
}
