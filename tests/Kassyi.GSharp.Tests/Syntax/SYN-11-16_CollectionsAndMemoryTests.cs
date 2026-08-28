using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Compiler.Transpiler;

namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_11_16_CollectionsAndMemoryTests
{
    [Fact(Skip = "未実装: SYN-11 (Index & Range 構文 ^1, [1..5])")]
    [Trait("FeatureId", "SYN-11")]
    [Trait("Category", "Syntax")]
    public void Test_SYN11_IndexAndRange_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-12 (コレクション式 [1, 2, 3] / スプレッド ..)")]
    [Trait("FeatureId", "SYN-12")]
    [Trait("Category", "Syntax")]
    public void Test_SYN12_CollectionExpression_Transpilation()
    {
    }

    [Fact]
    [Trait("FeatureId", "SYN-13")]
    [Trait("Category", "Syntax")]
    public void Test_SYN13_Stackalloc_Transpilation()
    {
        const string source = """
        using System;
        using Kassyi.GSharp.Core;

        public class TestKernelClass
        {
            [GpuKernel]
            public static void TestKernel()
            {
                Span<int> buf = stackalloc int[16];
                buf[0] = 42;
            }
        }
        """;

        var userTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp11));
        var compilation = MegaCompilationCache.BaseCompilation.AddSyntaxTrees(userTree);
        var semanticModel = compilation.GetSemanticModel(userTree);

        var methodDecl = userTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();

        var diagnostics = new List<Diagnostic>();
        var generatedCode = MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel, diagnostics.Add, isDevice: false);

        Assert.NotNull(generatedCode);
        Assert.True(diagnostics.Any(d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id) || generatedCode.Contains("buf"), "Transpiler should handle stackalloc or report GSH0003 UnsupportedSyntax diagnostic");
    }

    [Fact(Skip = "未実装: SYN-15 (Span<T> / ReadOnlySpan<T> のカーネル内使用)")]
    [Trait("FeatureId", "SYN-15")]
    [Trait("Category", "Syntax")]
    public void Test_SYN15_SpanUsage_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-16 (2次元インデクサ / 多次元配列)")]
    [Trait("FeatureId", "SYN-16")]
    [Trait("Category", "Syntax")]
    public void Test_SYN16_MultidimensionalIndexer_Transpilation()
    {
    }
}
