using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class ControlFlowTests : TranspilerTestBase
{
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 30)]
    [InlineData(4, 99)]
    [InlineData(0, 99)]
    [InlineData(-1, 99)]
    [InlineData(int.MinValue, 99)]
    [InlineData(int.MaxValue, 99)]
    [InlineData(int.MaxValue - 1, 99)]
    public void Test_SwitchExpression_Transpilation(int inputVal, int expectedVal)
    {
        int[] input = [inputVal];
        int[] output = [0];

        ControlFlowKernels.LaunchSwitchExpressionKernel(input, output, blockDimX: 1);

        Assert.Equal(expectedVal, output[0]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Test_RefSwitchExpression_Transpilation()
    {
        int[] a = [10, 20];
        int[] b = [100, 200];
        int[] selectors = [1, 2]; // tid 0 selects 'a', tid 1 selects 'b'
        int[] valuesToAssign = [999, 888];

        ControlFlowKernels.LaunchRefSwitchExpressionKernel(a, b, selectors, valuesToAssign, blockDimX: 2);

        Assert.Equal(999, a[0]); // a[0] updated
        Assert.Equal(20, a[1]);  // a[1] unchanged
        Assert.Equal(100, b[0]); // b[0] unchanged
        Assert.Equal(888, b[1]); // b[1] updated
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [InlineData(42, 100, 42)]
    [InlineData(null, 100, 100)]
    [InlineData(0, 50, 0)]
    public void Test_NullCoalescing_Transpilation(int? inputVal, int fallbackVal, int expectedVal)
    {
        int?[] nullableInputs = [inputVal];
        int[] fallbackValues = [fallbackVal];
        int[] results = [0];

        ControlFlowKernels.LaunchNullCoalescingKernel(nullableInputs, fallbackValues, results, blockDimX: 1);

        Assert.Equal(expectedVal, results[0]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Test_NullCoalescingAssignment_SideEffect_Transpilation()
    {
        int?[] targetArray = [null, 10]; // target[0] is null (will be assigned), target[1] is 10 (will NOT be assigned)
        int[] sideEffectTracker = [0, 1]; // starting indices for side effect
        int[] values = [50, 99];

        ControlFlowKernels.LaunchNullCoalescingAssignmentSideEffectKernel(targetArray, sideEffectTracker, values, blockDimX: 2);

        // Verify side-effect tracker was incremented exactly once per thread
        Assert.Equal(1, sideEffectTracker[0]);
        Assert.Equal(2, sideEffectTracker[1]);

        // Verify compound assignment results
        Assert.Equal(50, targetArray[0]); // null ??= 50 -> 50
        Assert.Equal(10, targetArray[1]); // 10 ??= 99 -> 10
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [InlineData(10, 5, 10.0)]
    [InlineData(2, 5, 7.5)]
    [InlineData(0, 0, 0.0)]
    [InlineData(5, 5, 7.5)]
    [InlineData(6, 5, 6.0)]
    [InlineData(4, 5, 7.5)]
    public void Test_ConditionalExpression_Transpilation(int aVal, int bVal, double expectedResult)
    {
        int[] a = [aVal];
        int[] b = [bVal];
        double[] result = [0.0];

        ControlFlowKernels.LaunchSimpleTernaryKernel(a, b, result, blockDimX: 1);

        Assert.Equal(expectedResult, result[0], precision: 4);
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [InlineData(10, 5, 2, 1)] // 10 > 5 and 5 > 2 -> 1
    [InlineData(10, 2, 5, 2)] // 10 > 2 and 2 <= 5 -> 2
    [InlineData(2, 5, 1, 3)] // 2 <= 5 and 2 > 1 -> 3
    [InlineData(2, 5, 3, 4)] // 2 <= 5 and 2 <= 3 -> 4
    public void Test_NestedTernary_Transpilation(int x, int y, int z, int expectedResult)
    {
        int[] a = [x];
        int[] b = [y];
        int[] c = [z];
        int[] result = [0];

        ControlFlowKernels.LaunchNestedTernaryKernel(a, b, c, result, blockDimX: 1);

        Assert.Equal(expectedResult, result[0]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_PatternMatching_Transpilation()
    {
        // Runtime semantics of this kernel are covered by Test_SwitchExpression_Transpilation (kernel launch),
        // since ThreadIndex is unavailable outside a GPU context.
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod(
            typeof(ControlFlowKernels), nameof(ControlFlowKernels.SwitchExpressionKernel), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        await Verify(cudaSource);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_RefConditionalExpression_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<ControlFlowTarget>(
            nameof(ControlFlowTarget.RefConditionalExpression), dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        await Verify(cudaSource);

        int[] a = [1];
        int[] b = [2];
        ControlFlowTarget.RefConditionalExpression(a, b, useFirst: true);
        Assert.Equal(2, a[0]);
        Assert.Equal(2, b[0]);

        ControlFlowTarget.RefConditionalExpression(a, b, useFirst: false);
        Assert.Equal(2, a[0]);
        Assert.Equal(3, b[0]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_DefaultLiteral_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<ControlFlowTarget>(
            nameof(ControlFlowTarget.DefaultLiteral), dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        await Verify(cudaSource);

        SampleData[] items = [new() { X = 5, Y = 1.5f }];
        int[] counts = [7];
        ControlFlowTarget.DefaultLiteral(items, counts);
        Assert.Equal(default, items[0]);
        Assert.Equal(0, counts[0]);
    }
}

public struct SampleData
{
    public int X;
    public float Y;
}

public class ControlFlowTarget
{
    [GpuDeviceFunction]
    public static void IncrementRef(ref int target)
    {
        target += 1;
    }

    public static void RefConditionalExpression(int[] a, int[] b, bool useFirst)
    {
        IncrementRef(ref (useFirst ? ref a[0] : ref b[0]));
    }

    public static void DefaultLiteral(SampleData[] items, int[] counts)
    {
        SampleData s = default;
        int c = default(int);
        items[0] = s;
        counts[0] = c;
    }
}

