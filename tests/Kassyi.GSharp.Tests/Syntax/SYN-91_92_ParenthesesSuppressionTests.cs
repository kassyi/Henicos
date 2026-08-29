using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_91_92_ParenthesesSuppressionTests
{
    private static (string CudaSource, List<Diagnostic> Diagnostics) TranspileMethod(string code, string methodName)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp11);
        var syntaxTree = CSharpSyntaxTree.ParseText(code, parseOptions);
        var compilation = MegaCompilationCache.BaseCompilation.AddSyntaxTrees(syntaxTree);
        var semanticModel = compilation.GetSemanticModel(syntaxTree);

        var root = syntaxTree.GetRoot();
        var methodDecl = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == methodName);

        var diagnostics = new List<Diagnostic>();
        var cudaSource = MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel, d => diagnostics.Add(d), isDevice: true);
        return (cudaSource, diagnostics);
    }

    [Fact]
    [Trait("FeatureId", "SYN-91")]
    [Trait("Category", "Syntax")]
    public void Test_SYN91_RedundantParenthesesSuppression_HappyPath()
    {
        const string code = """
        using Kassyi.GSharp.Core;

        public static class TestClass
        {
            [GpuDeviceFunction]
            public static void CheckCondition(int a, int b)
            {
                if (a < b)
                {
                }
            }
        }
        """;

        var (cudaSource, diagnostics) = TranspileMethod(code, "CheckCondition");

        Assert.Empty(diagnostics);
        Assert.Contains("if", cudaSource);
        Assert.Contains("a", cudaSource);
        Assert.Contains("b", cudaSource);
    }

    [Fact]
    [Trait("FeatureId", "SYN-92")]
    [Trait("Category", "Syntax")]
    public void Test_SYN92_ParenthesesPreservation_EdgeCases_PrecedenceAndMixedOperators()
    {
        const string code = """
        using Kassyi.GSharp.Core;

        public static class TestClass
        {
            [GpuDeviceFunction]
            public static void ComplexOperators(int a, int b, int c)
            {
                bool r1 = (a & b) == c;
                int r2 = a * (b + c);
            }
        }
        """;

        var (cudaSource, diagnostics) = TranspileMethod(code, "ComplexOperators");

        Assert.Empty(diagnostics);
        Assert.Contains("r1", cudaSource);
        Assert.Contains("r2", cudaSource);
    }
}
