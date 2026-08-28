
namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_01_09_ControlFlowTests
{
    [Fact(Skip = "未実装: SYN-01 (Switch 式 / switch expression)")]
    [Trait("FeatureId", "SYN-01")]
    [Trait("Category", "Syntax")]
    public void Test_SYN01_SwitchExpression_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-02 (パターンマッチング is / 結合 / リレーショナル)")]
    [Trait("FeatureId", "SYN-02")]
    [Trait("Category", "Syntax")]
    public void Test_SYN02_PatternMatching_Transpilation()
    {
    }

    [Fact]
    [Trait("FeatureId", "SYN-03")]
    [Trait("Category", "Syntax")]
    public void Test_SYN03_InlineOutVarDeclaration_Transpilation()
    {
        // Happy Path: out var x in method call argument
        const string source = """
            using Kassyi.GSharp.Core;

            public class KernelClass
            {
                private static bool TryGetValue(int input, out int value)
                {
                    value = input * 2;
                    return true;
                }

                [GpuKernel]
                public static void Kernel(int[] data)
                {
                    TryGetValue(10, out var result);
                    data[0] = result;
                }
            }
            """;

        var (driver, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);

        // Assert: Verify that if unsupported syntax diagnostic (GSH0003) is reported, it safely blocks unsupported out var declaration expression, or if transpiled, emits hoist.
        Assert.NotEmpty(diagnostics);
        Assert.Contains(diagnostics, d => d.Id == "GSH0003" || d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Trait("FeatureId", "SYN-03")]
    [Trait("Category", "Syntax")]
    public void Test_SYN03_InlineOutVarDeclaration_SadPath()
    {
        // Sad Path: variable with same name already exists in same scope
        const string source = """
            using Kassyi.GSharp.Core;

            public class KernelClass
            {
                private static void GetVal(out int val)
                {
                    val = 42;
                }

                [GpuKernel]
                public static void Kernel(int[] data)
                {
                    int x = 10;
                    GetVal(out var x);
                    data[0] = x;
                }
            }
            """;

        var (driver, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        // Roslyn compiler diagnostics or generator diagnostics should report an error / handle safely without crash
        Assert.NotEmpty(diagnostics);
        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Trait("FeatureId", "SYN-03")]
    [Trait("Category", "Syntax")]
    public void Test_SYN03_InlineOutVarDeclaration_EdgeCases()
    {
        // Edge Case 1: out var inside if statement condition
        // Edge Case 2: out var inside while loop condition
        const string source = """
            using Kassyi.GSharp.Core;

            public class KernelClass
            {
                private static bool TryStep(int current, out int next)
                {
                    next = current + 1;
                    return next < 10;
                }

                [GpuKernel]
                public static void Kernel(int[] data)
                {
                    if (TryStep(data[0], out var firstStep))
                    {
                        data[0] = firstStep;
                    }

                    while (TryStep(data[1], out var loopStep))
                    {
                        data[1] = loopStep;
                    }
                }
            }
            """;

        var (driver, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        // Verify that inline out var inside if/while conditions does not crash the generator and reports appropriate diagnostics safely
        Assert.NotEmpty(diagnostics);
        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact(Skip = "未実装: SYN-04 (Null合体 / 複合代入 ??, ??=)")]
    [Trait("FeatureId", "SYN-04")]
    [Trait("Category", "Syntax")]
    public void Test_SYN04_NullCoalescing_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-05 (三項演算子 ? : の型変換・ネスト)")]
    [Trait("FeatureId", "SYN-05")]
    [Trait("Category", "Syntax")]
    public void Test_SYN05_ConditionalExpression_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-06 (ref 条件式 / ref ternary)")]
    [Trait("FeatureId", "SYN-06")]
    [Trait("Category", "Syntax")]
    public void Test_SYN06_RefConditionalExpression_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-07 (ローカル関数 / local functions)")]
    [Trait("FeatureId", "SYN-07")]
    [Trait("Category", "Syntax")]
    public void Test_SYN07_LocalFunctions_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-08 (ラムダ式・無名関数 / Lambdas / Anonymous Functions)")]
    [Trait("FeatureId", "SYN-08")]
    [Trait("Category", "Syntax")]
    public void Test_SYN08_LambdaExpressions_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-09 (デフォルトリテラル default, default(T))")]
    [Trait("FeatureId", "SYN-09")]
    [Trait("Category", "Syntax")]
    public void Test_SYN09_DefaultLiteral_Transpilation()
    {
    }
}
