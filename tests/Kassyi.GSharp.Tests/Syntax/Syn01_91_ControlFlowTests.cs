using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

public class Syn01_91_ControlFlowTests
{
    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-90")]
    [InlineData(5, 2, 10)]       // 5 <= 10 -> else branch 5 times: 5 * 2 = 10
    [InlineData(9, 2, 10)]       // 9 <= 10 -> else branch 5 times: 5 * 2 = 10
    [InlineData(10, 2, 10)]      // 10 <= 10 (Strict Boundary) -> else branch: 5 * 2 = 10
    [InlineData(11, 2, 55)]      // 11 > 10 (Strict Boundary) -> if branch: 5 * 11 = 55
    [InlineData(15, 2, 75)]      // 15 > 10 -> if branch: 5 * 15 = 75
    [InlineData(-5, 3, 15)]      // Negative -> else branch: 5 * 3 = 15
    [InlineData(0, 0, 0)]        // Zero case
    public void TestComplexLogicKernel_BoundaryDataDriven(int a, int b, int expectedC)
    {
        int[] aArray = [a];
        int[] bArray = [b];
        int[] cArray = [0];

        Syn01_91_ControlFlowKernelsWrapper.Launch_ComplexLogicKernel(aArray, bArray, cArray);
        Assert.Equal(expectedC, cArray[0]);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-91")]
    public void TestControlFlowKernel()
    {
        // Test with 8 threads across multiple warps/lanes
        int[] dataArray = new int[8];

        Syn01_91_ControlFlowKernelsWrapper.Launch_ControlFlowKernel(dataArray, blockDimX: 8);

        // tid 0: 10 + 3 + 100 = 113
        Assert.Equal(113, dataArray[0]);
        // tid 1: 20 + 3 + 200 = 223
        Assert.Equal(223, dataArray[1]);
        // tid 2: 20 + 3 + 200 = 223
        Assert.Equal(223, dataArray[2]);
        // tid 3..7: default branch -> 30 + 3 = 33
        for (int tid = 3; tid < 8; tid++)
        {
            Assert.Equal(33, dataArray[tid]);
        }
    }

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

    [Fact(Skip = "未実装: SYN-03 (インライン out var 変数宣言)")]
    [Trait("FeatureId", "SYN-03")]
    [Trait("Category", "Syntax")]
    public void Test_SYN03_InlineOutVarDeclaration_Transpilation()
    {
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


