using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Arrays that C# fills with zeros read zeros on the device as well (#623). Where a kernel does not read a thread
/// index, the expected values come from running the same kernel body as C# on the host.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class ZeroInitTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_StackAllocWithoutInitializer_ReadsZerosAfterASiblingBlock()
    {
        const int Threads = 32;
        int[] output = new int[Threads * 2];
        Array.Fill(output, -1);

        ZeroInitKernels.LaunchStackAllocInSiblingBlocksKernel(output, blockDimX: Threads);

        int[] expected = new int[Threads * 2];
        for (int t = 0; t < Threads; t++)
        {
            expected[t * 2] = 28 + t;
            expected[t * 2 + 1] = 0;
        }

        Assert.Equal(expected, output);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_NewArrayWithConstantSize_IsZeroedAndKeepsItsLength()
    {
        int[] output = [-1, -1, -1];
        int[] expected = [-1, -1, -1];
        ZeroInitKernels.NewArrayKernel(expected);

        ZeroInitKernels.LaunchNewArrayKernel(output, blockDimX: 1);

        Assert.Equal([4, 10, 9], expected);
        Assert.Equal(expected, output);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_StackAllocOfEachElementType_ReadsTheDefaultValue()
    {
        int[] output = [-1, -1, -1, -1, -1, -1];
        int[] sink = [0];
        int[] expected = [-1, -1, -1, -1, -1, -1];
        ZeroInitKernels.ElementTypesKernel(expected, [0]);

        ZeroInitKernels.LaunchElementTypesKernel(output, sink, blockDimX: 1);

        Assert.Equal([0, 0, 0, 0, 0, 0], expected);
        Assert.Equal(expected, output);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_StackAllocInALoopBody_IsZeroedOnEveryIteration()
    {
        int[] output = [-1, -1, -1, -1];
        int[] expected = [-1, -1, -1, -1];
        ZeroInitKernels.StackAllocInLoopKernel(expected);

        ZeroInitKernels.LaunchStackAllocInLoopKernel(output, blockDimX: 1);

        Assert.Equal([0, 2, 4, 6], expected);
        Assert.Equal(expected, output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(5)]
    public void Test_StackAllocOfATypeParameter_IsZeroedInEachInstantiation(int index)
    {
        int[] output = [-1, -1, -1, -1];
        float[] floats = [-1f];
        int[] expected = [-1, -1, -1, -1];
        float[] expectedFloats = [-1f];
        ZeroInitKernels.GenericStackAllocKernel(expected, expectedFloats, index);

        ZeroInitKernels.LaunchGenericStackAllocKernel(output, floats, index, blockDimX: 1);

        Assert.Equal([0, 4, 14, 18], expected);
        Assert.Equal([0f], expectedFloats);
        Assert.Equal(expected, output);
        Assert.Equal(expectedFloats, floats);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ReadOnlySpanOverStackAlloc_BuildsAndReadsZeros()
    {
        int[] output = [-1, -1];
        int[] expected = [-1, -1];
        ZeroInitKernels.ReadOnlyStackAllocKernel(expected);

        ZeroInitKernels.LaunchReadOnlyStackAllocKernel(output, blockDimX: 1);

        Assert.Equal([0, 4], expected);
        Assert.Equal(expected, output);
    }
}
