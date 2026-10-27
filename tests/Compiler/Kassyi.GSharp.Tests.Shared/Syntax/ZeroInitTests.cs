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

    // The contents of a stackalloc under [SkipLocalsInit] are unspecified in C#, so the host gives no reference; G#
    // zeroes a const array because C++ requires its initializer
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ReadOnlySpanOverStackAllocUnderSkipLocalsInit_BuildsAndReadsZeros()
    {
        int[] output = [-1, -1];

        ZeroInitKernels.LaunchReadOnlyStackAllocSkipLocalsInitKernel(output, blockDimX: 1);

        Assert.Equal([0, 4], output);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_StackArray_PassesAsSpanOrArrayArgumentWithItsLength()
    {
        int[] output = Enumerable.Repeat(-1, 6).ToArray();
        int[] expected = Enumerable.Repeat(-1, 6).ToArray();
        ZeroInitKernels.StackArrayArgumentKernel(expected);

        ZeroInitKernels.LaunchStackArrayArgumentKernel(output, blockDimX: 1);

        Assert.Equal([4, 4, 4, 4, 2, 10], expected);
        Assert.Equal(expected, output);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_StackArray_IsNeverNull()
    {
        int[] output = Enumerable.Repeat(-1, 6).ToArray();
        int[] expected = Enumerable.Repeat(-1, 6).ToArray();
        ZeroInitKernels.StackArrayNullTestKernel(expected);

        ZeroInitKernels.LaunchStackArrayNullTestKernel(output, blockDimX: 1);

        Assert.Equal([0, 1, 0, 1, 0, 0], expected);
        Assert.Equal(expected, output);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_NullTestThroughACast_TestsTheArray()
    {
        int[] input = [5];
        int[] output = [-1, -1];
        int[] expected = [-1, -1];
        ZeroInitKernels.CastNullTestKernel(input, expected);

        ZeroInitKernels.LaunchCastNullTestKernel(input, output, blockDimX: 1);

        Assert.Equal([0, 1], expected);
        Assert.Equal(expected, output);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_StackArray_IsTheReceiverOfSpanMembersAndStackLinq()
    {
        int[] output = Enumerable.Repeat(-1, 5).ToArray();
        int[] expected = Enumerable.Repeat(-1, 5).ToArray();
        ZeroInitKernels.StackArrayMembersKernel(expected);

        ZeroInitKernels.LaunchStackArrayMembersKernel(output, blockDimX: 1);

        Assert.Equal([2, 3, 3, 10, 8], expected);
        Assert.Equal(expected, output);
    }

    // The forms GSH0091 reports (an alias or a ref argument of an outer scope) are rejected before they run; within the
    // scope, the alias and the ref argument see the array of the same iteration
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_StackArrayHeldByALocalOrARefArgumentInItsScope_KeepsItsValues()
    {
        int[] seed = [100];
        int[] output = [-1, -1];
        int[] expected = [-1, -1];
        ZeroInitKernels.StackArrayAliasKernel(expected, seed);

        ZeroInitKernels.LaunchStackArrayAliasKernel(output, seed, blockDimX: 1);

        Assert.Equal([22, 44], expected);
        Assert.Equal(expected, output);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SkipLocalsInit_KeepsDefaultConstructorAndObjectInitializers()
    {
        int[] output = Enumerable.Repeat(-1, 11).ToArray();
        int[] expected = Enumerable.Repeat(-1, 11).ToArray();
        ZeroInitKernels.SkipLocalsInitInitializersKernel(expected);

        ZeroInitKernels.LaunchSkipLocalsInitInitializersKernel(output, blockDimX: 1);

        Assert.Equal([0, 0, 0, 0, 0, 0, 1, 2, 5, 0, 45], expected);
        Assert.Equal(expected, output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(5)]
    public void Test_SkipLocalsInit_KeepsDefaultOfStructAndInlineArray(int index)
    {
        int[] output = [-1, -1];
        int[] expected = [-1, -1];
        ZeroInitKernels.SkipLocalsInitDefaultKernel(expected, index);

        ZeroInitKernels.LaunchSkipLocalsInitDefaultKernel(output, index, blockDimX: 1);

        Assert.Equal([0, 28], expected);
        Assert.Equal(expected, output);
    }
}
