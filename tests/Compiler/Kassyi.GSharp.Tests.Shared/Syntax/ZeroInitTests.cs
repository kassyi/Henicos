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
    public void StackAlloc_WithoutInitializerAfterSiblingBlock_ReadsZeros()
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
    public void NewArray_ConstantSize_IsZeroedAndKeepsLength()
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
    public void StackAlloc_VariousPrimitiveTypes_ReadsDefaultValue()
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
    public void StackAlloc_LoopBody_IsZeroedOnEveryIteration()
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
    public void StackAlloc_TypeParameter_IsZeroedInEachInstantiation(int index)
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
    public void ReadOnlySpan_OverStackAlloc_ReadsZerosAndLength()
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
    public void ReadOnlySpan_UnderSkipLocalsInit_ReadsZerosAndLength()
    {
        int[] output = [-1, -1];

        ZeroInitKernels.LaunchReadOnlyStackAllocSkipLocalsInitKernel(output, blockDimX: 1);

        Assert.Equal([0, 4], output);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void StackArray_SpanOrArrayParameter_PassesLengthAndElements()
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
    public void StackArray_NullChecks_EvaluatesToNotNull()
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
    public void StackArray_NullCheckThroughCast_EvaluatesToNotNull()
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
    public void StackArray_SpanMembersAndStackLinq_ProducesExpectedResults()
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
    public void StackArray_LocalAliasOrRefParameterInScope_PreservesValues()
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
    public void SkipLocalsInit_ConstructorsAndInitializers_PreservesDefaultAndExplicitValues()
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
    public void SkipLocalsInit_StructAndInlineArrayDefaults_PreservesZeroedFields(int index)
    {
        int[] output = [-1, -1];
        int[] expected = [-1, -1];
        ZeroInitKernels.SkipLocalsInitDefaultKernel(expected, index);

        ZeroInitKernels.LaunchSkipLocalsInitDefaultKernel(output, index, blockDimX: 1);

        Assert.Equal([0, 28], expected);
        Assert.Equal(expected, output);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void StackAlloc_CollectionExpressionsInArguments_PassesLengthsAndElements()
    {
        int[] output = Enumerable.Repeat(-1, 8).ToArray();
        int[] expected = Enumerable.Repeat(-1, 8).ToArray();
        ZeroInitKernels.StackAllocArgumentKernel(expected);

        ZeroInitKernels.LaunchStackAllocArgumentKernel(output, blockDimX: 1);

        Assert.Equal([4, 3, 7, 11, 0, 0, 1, 15], expected);
        Assert.Equal(expected, output);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void StackAllocInitializer_ArgumentEvaluation_PreservesEvaluationOrder()
    {
        int[] output = [-1, -1];
        int[] expected = [-1, -1];
        ZeroInitKernels.StackAllocInitializerOrderKernel(expected);

        ZeroInitKernels.LaunchStackAllocInitializerOrderKernel(output, blockDimX: 1);

        Assert.Equal([1 * 10 + (2 + 3), 3], expected);
        Assert.Equal(expected, output);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void StackAlloc_PointerAccess_PointsToZeroedOrInitializedMemory()
    {
        int[] output = [-1, -1];
        int[] expected = [-1, -1];
        ZeroInitKernels.StackAllocPointerKernel(expected);

        ZeroInitKernels.LaunchStackAllocPointerKernel(output, blockDimX: 1);

        Assert.Equal([5, 13], expected);
        Assert.Equal(expected, output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(255)]
    public void StackAlloc_LargeBufferInArgument_IsZeroedToLastElement(int index)
    {
        int[] output = [-1];
        int[] expected = [-1];
        ZeroInitKernels.LargeStackAllocKernel(expected, index);

        ZeroInitKernels.LaunchLargeStackAllocKernel(output, index, blockDimX: 1);

        Assert.Equal([0], expected);
        Assert.Equal(expected, output);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void StackAlloc_ArgumentInLoop_IsZeroedOnEveryEvaluation()
    {
        int[] output = [-1];
        int[] expected = [-1];
        ZeroInitKernels.StackAllocArgumentInLoopKernel(expected);

        ZeroInitKernels.LaunchStackAllocArgumentInLoopKernel(output, blockDimX: 1);

        Assert.Equal([0], expected);
        Assert.Equal(expected, output);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void StackAlloc_ControlFlowHeaders_IsDeclaredAndZeroed()
    {
        int[] output = [-1, -1, -1];
        int[] expected = [-1, -1, -1];
        ZeroInitKernels.StackAllocInLoopHeadersKernel(expected);

        ZeroInitKernels.LaunchStackAllocInLoopHeadersKernel(output, blockDimX: 1);

        Assert.Equal([3, 3, 1], expected);
        Assert.Equal(expected, output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1, 4)]
    [InlineData(0, 8)]
    public void StackAlloc_ConditionalBranches_HasBranchSpecificLength(int c, int length)
    {
        int[] output = [-1];
        int[] expected = [-1];
        ZeroInitKernels.StackAllocConditionalKernel(expected, c);

        ZeroInitKernels.LaunchStackAllocConditionalKernel(output, c, blockDimX: 1);

        Assert.Equal([length], expected);
        Assert.Equal(expected, output);
    }
}
