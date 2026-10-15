namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class AtomicIntrinsicsTests : TranspilerTestBase
{
    private const int WarpSizedBlock = 32;

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_AtomicArithmetic_AllThreadsContributeToSingleAddress()
    {
        EnsureCapability(DialectCapabilities.HardwareAtomics);
        int[] intState = [0, int.MaxValue];
        uint[] uintState = [uint.MaxValue, 0u, uint.MaxValue, 0u];
        ulong[] ulongState = [0ul, ulong.MaxValue, ulong.MaxValue];
        float[] floatState = [0.0f, 0.0f];

        AtomicIntrinsicsKernels.LaunchAtomicArithmeticKernel(
            intState, uintState, ulongState, floatState, blockDimX: WarpSizedBlock);

        Assert.Equal(-WarpSizedBlock, intState[0]);
        Assert.Equal(-5, intState[1]);

        Assert.Equal(0u, uintState[0]);
        Assert.Equal(uint.MaxValue, uintState[1]);
        Assert.Equal(0u, uintState[2]);
        Assert.Equal(uint.MaxValue, uintState[3]);

        Assert.Equal(0xFFFFFFFF_00000000ul, ulongState[0]);
        Assert.Equal(0ul, ulongState[1]);
        Assert.Equal(0xFFFFFFFF_00000000ul, ulongState[2]);

        Assert.Equal(WarpSizedBlock * 0.5f, floatState[0]);
        Assert.Equal(3.25f, floatState[1]);
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0u, 0u)]
    [InlineData(0u, 1u)]
    [InlineData(0u, 9u)]
    [InlineData(0u, 31u)]
    [InlineData(0u, 32u)]
    [InlineData(0u, uint.MaxValue)]
    [InlineData(100u, 9u)]
    [InlineData(uint.MaxValue, uint.MaxValue)]
    public void Test_AtomicIncDec_WrapsAroundLimit(uint initial, uint limit)
    {
        EnsureCapability(DialectCapabilities.HardwareAtomics);
        uint[] state = [initial, initial];

        AtomicIntrinsicsKernels.LaunchAtomicIncDecWrapKernel(state, limit, blockDimX: WarpSizedBlock);

        // Every thread applies the same operation, so the result is independent of execution order.
        uint expectedInc = initial;
        uint expectedDec = initial;
        for (int i = 0; i < WarpSizedBlock; i++)
        {
            expectedInc = expectedInc >= limit ? 0u : expectedInc + 1u;
            expectedDec = expectedDec == 0u || expectedDec > limit ? limit : expectedDec - 1u;
        }

        Assert.Equal(expectedInc, state[0]);
        Assert.Equal(expectedDec, state[1]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stress)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_AtomicFloatAdd_HighContention()
    {
        EnsureCapability(DialectCapabilities.HardwareAtomics);
        float[] globalSum = [0.0f];
        const uint threadsPerBlock = 256;
        const uint blocksPerGrid = 16;

        AtomicIntrinsicsKernels.LaunchAtomicFloatAddStressKernel(
            globalSum, gridDimX: blocksPerGrid, blockDimX: threadsPerBlock);

        // 4096 is exactly representable as float, so summation order introduces no rounding error.
        Assert.Equal(threadsPerBlock * blocksPerGrid, globalSum[0]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_AtomicThroughRefParameter_UpdatesCallerElement()
    {
        EnsureCapability(DialectCapabilities.HardwareAtomics);
        int[] counter = [0];

        AtomicIntrinsicsKernels.LaunchAtomicRefParameterKernel(counter, blockDimX: WarpSizedBlock);

        Assert.Equal(WarpSizedBlock * (WarpSizedBlock + 1) / 2, counter[0]);
    }
}
