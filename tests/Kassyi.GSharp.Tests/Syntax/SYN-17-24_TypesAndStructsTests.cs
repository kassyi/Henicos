
namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_17_24_TypesAndStructsTests
{
    [Fact]
    [Trait("FeatureId", "SYN-17")]
    [Trait("Category", "Syntax")]
    public void Test_SYN17_TargetTypedNew_Transpilation()
    {
        // Happy Path: Target-typed new() variable declaration & method argument
        const string source = """
            using Kassyi.GSharp.Core;

            public struct MyPoint
            {
                public int X;
                public int Y;
            }

            public class KernelClass
            {
                private static void ConsumePoint(MyPoint p)
                {
                }

                [GpuKernel]
                public static void Kernel()
                {
                    MyPoint p1 = new();
                    MyPoint p2 = new() { X = 10, Y = 20 };
                    ConsumePoint(new());
                }
            }
            """;

        var (driver, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var tree = compilation.SyntaxTrees.Last();
        var cudaCode = tree.ToString();

        Assert.Contains("MyPoint p1", cudaCode);
        Assert.Contains("MyPoint p2", cudaCode);
        Assert.Contains("ConsumePoint(MyPoint", cudaCode);
    }

    [Fact]
    [Trait("FeatureId", "SYN-17")]
    [Trait("Category", "Syntax")]
    public void Test_SYN17_TargetTypedNew_SadPath()
    {
        // Sad Path: Context where target type cannot be resolved (var x = new();)
        const string source = """
            using Kassyi.GSharp.Core;

            public class KernelClass
            {
                [GpuKernel]
                public static void Kernel()
                {
                    var x = new();
                }
            }
            """;

        var (driver, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        Assert.NotEmpty(diagnostics);
        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Trait("FeatureId", "SYN-17")]
    [Trait("Category", "Syntax")]
    public void Test_SYN17_TargetTypedNew_EdgeCases()
    {
        // Edge Case: Nested Target-typed new() in struct initializer: new Parent { Child = new() }
        const string source = """
            using Kassyi.GSharp.Core;

            public struct ChildStruct
            {
                public int Value;
            }

            public struct ParentStruct
            {
                public ChildStruct Child;
            }

            public class KernelClass
            {
                [GpuKernel]
                public static void Kernel()
                {
                    ParentStruct parent = new()
                    {
                        Child = new() { Value = 42 }
                    };
                }
            }
            """;

        var (driver, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var tree = compilation.SyntaxTrees.Last();
        var cudaCode = tree.ToString();

        Assert.Contains("ParentStruct parent", cudaCode);
        Assert.Contains("ChildStruct", cudaCode);
    }

    [Fact(Skip = "未実装: SYN-18 (構造化束縛・タプル宣言分解)")]
    [Trait("FeatureId", "SYN-18")]
    [Trait("Category", "Syntax")]
    public void Test_SYN18_TupleDeconstruction_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-19 (構造体のプライマリコンストラクタ)")]
    [Trait("FeatureId", "SYN-19")]
    [Trait("Category", "Syntax")]
    public void Test_SYN19_PrimaryConstructor_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-20 (record struct と with 式)")]
    [Trait("FeatureId", "SYN-20")]
    [Trait("Category", "Syntax")]
    public void Test_SYN20_RecordStructWithExpression_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-21 (構造体内インデクサ this[int index])")]
    [Trait("FeatureId", "SYN-21")]
    [Trait("Category", "Syntax")]
    public void Test_SYN21_StructIndexer_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-22 (構造体内演算子オーバーロード)")]
    [Trait("FeatureId", "SYN-22")]
    [Trait("Category", "Syntax")]
    public void Test_SYN22_StructOperatorOverload_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-23 (readonly struct / readonly メソッド)")]
    [Trait("FeatureId", "SYN-23")]
    [Trait("Category", "Syntax")]
    public void Test_SYN23_ReadonlyStruct_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-24 (enum の基底型指定とビットフラグ [Flags])")]
    [Trait("FeatureId", "SYN-24")]
    [Trait("Category", "Syntax")]
    public void Test_SYN24_EnumBaseTypeAndFlags_Transpilation()
    {
    }
}
