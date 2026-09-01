namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_01_09_ControlFlowTests
{
    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-01")]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 30)]
    [InlineData(4, 99)]
    [InlineData(0, 99)]
    [InlineData(-1, 99)]
    [InlineData(int.MinValue, 99)]
    [InlineData(int.MaxValue, 99)]
    [InlineData(int.MaxValue - 1, 99)]
    public void Test_SYN01_SwitchExpression_Transpilation(int inputVal, int expectedVal)
    {
        int[] input = [inputVal];
        int[] output = [0];

        SYN_01_09_ControlFlowKernelsWrapper.Launch_SwitchExpressionKernel(input, output, blockDimX: 1);

        Assert.Equal(expectedVal, output[0]);
    }

    [Fact(Skip = "C# は ref switch 式を未サポートのためスキップ (代わりにif/else等を利用)")]
    [Trait("FeatureId", "SYN-01")]
    public void Test_SYN01_RefSwitchExpression_Transpilation()
    {
        int[] a = [10, 20];
        int[] b = [100, 200];
        int[] selectors = [1, 2]; // tid 0 selects 'a', tid 1 selects 'b'
        int[] valuesToAssign = [999, 888];

        SYN_01_09_ControlFlowKernelsWrapper.Launch_RefSwitchExpressionKernel(a, b, selectors, valuesToAssign, blockDimX: 2);

        Assert.Equal(999, a[0]); // a[0] updated
        Assert.Equal(20, a[1]);  // a[1] unchanged
        Assert.Equal(100, b[0]); // b[0] unchanged
        Assert.Equal(888, b[1]); // b[1] updated
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
        // Verified by SYN_03_InlineOutVarTests
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-04")]
    [InlineData(42, 100, 42)]
    [InlineData(null, 100, 100)]
    [InlineData(0, 50, 0)]
    public void Test_SYN04_NullCoalescing_Transpilation(int? inputVal, int fallbackVal, int expectedVal)
    {
        int?[] nullableInputs = [inputVal];
        int[] fallbackValues = [fallbackVal];
        int[] results = [0];

        SYN_01_09_ControlFlowKernelsWrapper.Launch_NullCoalescingKernel(nullableInputs, fallbackValues, results, blockDimX: 1);

        Assert.Equal(expectedVal, results[0]);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-04")]
    public void Test_SYN04_NullCoalescingAssignment_SideEffect_Transpilation()
    {
        int?[] targetArray = [null, 10]; // target[0] is null (will be assigned), target[1] is 10 (will NOT be assigned)
        int[] sideEffectTracker = [0, 1]; // starting indices for side effect
        int[] values = [50, 99];

        SYN_01_09_ControlFlowKernelsWrapper.Launch_NullCoalescingAssignmentSideEffectKernel(targetArray, sideEffectTracker, values, blockDimX: 2);

        // Verify side-effect tracker was incremented exactly once per thread
        Assert.Equal(1, sideEffectTracker[0]);
        Assert.Equal(2, sideEffectTracker[1]);

        // Verify compound assignment results
        Assert.Equal(50, targetArray[0]); // null ??= 50 -> 50
        Assert.Equal(10, targetArray[1]); // 10 ??= 99 -> 10
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-05")]
    [InlineData(10, 5, 10.0)]
    [InlineData(2, 5, 7.5)]
    [InlineData(0, 0, 0.0)]
    [InlineData(5, 5, 7.5)]
    [InlineData(6, 5, 6.0)]
    [InlineData(4, 5, 7.5)]
    public void Test_SYN05_ConditionalExpression_Transpilation(int aVal, int bVal, double expectedResult)
    {
        int[] a = [aVal];
        int[] b = [bVal];
        double[] result = [0.0];

        SYN_01_09_ControlFlowKernelsWrapper.Launch_SimpleTernaryKernel(a, b, result, blockDimX: 1);

        Assert.Equal(expectedResult, result[0], precision: 4);
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-05")]
    [InlineData(10, 5, 2, 1)] // 10 > 5 and 5 > 2 -> 1
    [InlineData(10, 2, 5, 2)] // 10 > 2 and 2 <= 5 -> 2
    [InlineData(2, 5, 1, 3)] // 2 <= 5 and 2 > 1 -> 3
    [InlineData(2, 5, 3, 4)] // 2 <= 5 and 2 <= 3 -> 4
    public void Test_SYN05_NestedTernary_Transpilation(int x, int y, int z, int expectedResult)
    {
        int[] a = [x];
        int[] b = [y];
        int[] c = [z];
        int[] result = [0];

        SYN_01_09_ControlFlowKernelsWrapper.Launch_NestedTernaryKernel(a, b, c, result, blockDimX: 1);

        Assert.Equal(expectedResult, result[0]);
    }

    [Fact(Skip = "未実装: SYN-06 (ref 条件式 / ref ternary)")]
    [Trait("FeatureId", "SYN-06")]
    [Trait("Category", "Syntax")]
    public void Test_SYN06_RefConditionalExpression_Transpilation()
    {
    }


    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-09")]
    public void Test_SYN09_DefaultLiteral_Transpilation()
    {
        int[] valOut = [99, 99];
        float[] vecOut = [99.0f, 99.0f];
        int[] condOut = [99, 99];

        SYN_01_09_ControlFlowKernelsWrapper.Launch_DefaultLiteralKernel(valOut, vecOut, condOut, blockDimX: 2);

        Assert.Equal(0, valOut[0]);
        Assert.Equal(0.0f, vecOut[0]);
        Assert.Equal(0, condOut[0]);
        Assert.Equal(100, condOut[1]);
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "GEN-21")]
    [InlineData(10, 10, 115)] // condA=10 (>0, >5), condB=10 (>5) -> 10 + 100 + 3 + 2 = 115
    [InlineData(10, 2, 215)]  // condA=10 (>0, >5), condB=2 (<=5) -> 10 + 200 + 3 + 2 = 215
    [InlineData(2, 10, 15)]    // condA=2 (>0, <=5), condB=10 -> 10 + 0 + 3 + 2 = 15
    [InlineData(-5, 10, 25)]   // condA=-5 (<=0, <=5), condB=10 -> 20 + 0 + 3 + 2 = 25
    [InlineData(-1, 10, 25)]   // condA=-1 (boundary <=0) -> 20 + 0 + 5 = 25
    [InlineData(0, 10, 25)]    // condA=0 (boundary <=0) -> 20 + 0 + 5 = 25
    [InlineData(1, 10, 15)]    // condA=1 (boundary >0, <=5) -> 10 + 0 + 5 = 15
    [InlineData(4, 10, 15)]    // condA=4 (>0, <=5) -> 10 + 0 + 5 = 15
    [InlineData(5, 10, 15)]    // condA=5 (boundary >0, <=5) -> 10 + 0 + 5 = 15
    [InlineData(6, 6, 115)]    // condA=6 (boundary >5), condB=6 (>5) -> 10 + 100 + 5 = 115
    [InlineData(6, 5, 215)]    // condA=6 (boundary >5), condB=5 (boundary <=5) -> 10 + 200 + 5 = 215
    public void Test_GEN21_SingleStatementWrapping_Transpilation(int ca, int cb, int expected)
    {
        int[] condA = [ca];
        int[] condB = [cb];
        int[] results = [0];

        SYN_01_09_ControlFlowKernelsWrapper.Launch_SingleStatementWrappingKernel(condA, condB, results, blockDimX: 1);

        Assert.Equal(expected, results[0]);
    }
}
