
using Kassyi.GSharp.Compiler.Transpiler;

namespace Kassyi.GSharp.Tests.Transpiler;

public class GSH_01_05_TranspilerIssuesTests
{
    [Fact]
    [Trait("FeatureId", "GSH-01")]
    [Trait("Category", "Transpiler")]
    public void Test_GSH01_ComplexConditionalExpression_EdgeCase_NestedAndPrecedence()
    {
        var source = """
        using Kassyi.GSharp.Core;

        public class TestClass_GSH01
        {
            [GpuKernel]
            public static void Kernel(int x, int y, int z, int[] outVal)
            {
                outVal[0] = x > 0 ? (y > 0 ? 1 : 2) : (z > 0 ? 3 : 4);
            }
        }
        """;

        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp11));
        var compilation = MegaCompilationCache.BaseCompilation.AddSyntaxTrees(tree);
        var model = compilation.GetSemanticModel(tree);
        var methodDecl = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();

        var cuda = MethodTranspiler.GenerateCudaSource(methodDecl, model);

        Assert.Contains("Kernel", cuda);
        Assert.NotNull(cuda);
    }

    [Fact(Skip = "未実装: GSH-02 (タプル構文の一般化)")]
    [Trait("FeatureId", "GSH-02")]
    [Trait("Category", "Transpiler")]
    public void Test_GSH02_GeneralTupleSupport()
    {
    }

    [Fact(Skip = "未実装: GSH-03 (ローカル関数の非サポート)")]
    [Trait("FeatureId", "GSH-03")]
    [Trait("Category", "Transpiler")]
    public void Test_GSH03_LocalFunctionsSupport()
    {
    }

    [Fact(Skip = "未実装: GSH-04 (スタック固定長配列・stackallocの限定性)")]
    [Trait("FeatureId", "GSH-04")]
    [Trait("Category", "Transpiler")]
    public void Test_GSH04_StackAllocFixedArray()
    {
    }

    [Fact(Skip = "未実装: GSH-05 (ジェネリクスの制約解決)")]
    [Trait("FeatureId", "GSH-05")]
    [Trait("Category", "Transpiler")]
    public void Test_GSH05_GenericsConstraintResolution()
    {
    }
}
