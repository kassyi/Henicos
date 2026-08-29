using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_90_StructAggregateInitTests
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
    [Trait("FeatureId", "SYN-90")]
    [Trait("Category", "Syntax")]
    public void Test_SYN90_StructAggregateInitialization_HappyPath()
    {
        const string code = """
        using Kassyi.GSharp.Core;

        public struct MyStruct
        {
            public int A;
            public int B;
        }

        public static class TestClass
        {
            [GpuDeviceFunction]
            public static void InitStruct()
            {
                MyStruct s = new MyStruct { A = 1, B = 2 };
            }
        }
        """;

        var (cudaSource, diagnostics) = TranspileMethod(code, "InitStruct");

        Assert.Empty(diagnostics);
        Assert.Contains("MyStruct _tmp{};", cudaSource);
        Assert.Contains("_tmp.A = 1;", cudaSource);
        Assert.Contains("_tmp.B = 2;", cudaSource);
    }

    [Fact]
    [Trait("FeatureId", "SYN-90")]
    [Trait("Category", "Syntax")]
    public void Test_SYN90_StructAggregateInitialization_EdgeCase_DeeplyNested()
    {
        const string code = """
        using Kassyi.GSharp.Core;

        public struct BStruct
        {
            public int X;
        }

        public struct AStruct
        {
            public BStruct B;
        }

        public static class TestClass
        {
            [GpuDeviceFunction]
            public static void InitNestedStruct()
            {
                AStruct a = new AStruct { B = new BStruct { X = 1 } };
            }
        }
        """;

        var (cudaSource, diagnostics) = TranspileMethod(code, "InitNestedStruct");

        Assert.Empty(diagnostics);
        Assert.Contains("AStruct _tmp{};", cudaSource);
        Assert.Contains("_tmp.B =", cudaSource);
        Assert.Contains("BStruct _tmp{};", cudaSource);
        Assert.Contains("_tmp.X = 1;", cudaSource);
    }
}
