#nullable enable
using System;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Array arguments that hold the same array, also together with a struct field: in C# they are one array, so a write
/// through one holder is seen through the others. The CUDA launcher copies such an array to one device region, copies it
/// in when any holder needs it and back when any holder needs it (#721); the CPU launcher gives every holder the same
/// array, a copy only when no holder sends the writes back.
/// </summary>
[Category(TestCategory.Runtime)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class SharedArrayArgumentTests : TranspilerTestBase
{
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoArguments_SameArray_ShareWrites(bool forceManaged)
    {
        var x = new int[2];

        Run(forceManaged, () => SharedArrayArgumentKernels.LaunchTwoArgumentsKernel(x, x));

        Assert.Equal([5, 10], x);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoArguments_DifferentArrays_StayApart(bool forceManaged)
    {
        var a = new int[2];
        var b = new int[2];

        Run(forceManaged, () => SharedArrayArgumentKernels.LaunchTwoArgumentsKernel(a, b));

        Assert.Equal([5, 0], a);
        Assert.Equal([0, 5], b);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalArguments_SameEmptyArray_ReadAsEmpty(bool forceManaged)
    {
        var x = Array.Empty<int>();
        int[] lengths = [-1, -1];

        Run(forceManaged, () => SharedArrayArgumentKernels.LaunchOptionalArgumentsKernel(x, x, lengths));

        Assert.Equal([0, 0], lengths);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalArguments_BothNull_ReadAsEmpty(bool forceManaged)
    {
        int[] lengths = [-1, -1];

        Run(forceManaged, () => SharedArrayArgumentKernels.LaunchOptionalArgumentsKernel(null, null, lengths));

        Assert.Equal([0, 0], lengths);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void FieldAndArgument_SameArray_ShareWrites(bool forceManaged)
    {
        var x = new int[2];

        Run(forceManaged, () => SharedArrayArgumentKernels.LaunchFieldAndArgumentKernel(
            new SharedArrayHolder { Data = x },
            x
        ));

        Assert.Equal([4, 8], x);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadOnlyAndWritable_SameArray_ReadOnlySeesWrite(bool forceManaged)
    {
        var x = new int[2];

        Run(forceManaged, () => SharedArrayArgumentKernels.LaunchReadOnlyAndWritableKernel(x, x));

        Assert.Equal([6, 7], x);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void InAndWritable_SameArray_InSeesWriteAndArrayIsCopiedBack(bool forceManaged)
    {
        var x = new int[2];

        Run(forceManaged, () => SharedArrayArgumentKernels.LaunchInAndWritableKernel(x, x));

        Assert.Equal([2, 3], x);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void OutBeforeIn_SameArray_IsCopiedIn(bool forceManaged)
    {
        int[] x = [7, 0];

        Run(forceManaged, () => SharedArrayArgumentKernels.LaunchOutBeforeInKernel(x, x));

        Assert.Equal([7, 8], x);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoIn_SameArray_ShareWritesButNotCopiedBack(bool forceManaged)
    {
        var x = new int[1];
        var result = new int[1];

        Run(forceManaged, () => SharedArrayArgumentKernels.LaunchTwoInKernel(x, x, result));

        Assert.Equal([9], result);
        Assert.Equal([0], x);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ThreeHolders_SameArray_ShareWrites(bool forceManaged)
    {
        int[] x = [0, 0, 0];

        Run(forceManaged, () => SharedArrayArgumentKernels.LaunchThreeHoldersKernel(
            new SharedArrayHolder { Data = x },
            x,
            x
        ));

        Assert.Equal([1, 2, 4], x);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ConvertedArguments_SameArray_ShareWrites(bool forceManaged)
    {
        var bags = new ConvertBag[2];

        Run(forceManaged, () => SharedArrayArgumentKernels.LaunchConvertedArgumentsKernel(bags, bags));

        Assert.Equal(7, bags[0].N);
        Assert.Equal(14, bags[1].N);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void InIntAndWritableUInt_SameArray_InSeesWrite(bool forceManaged)
    {
        var x = new int[2];

        Run(forceManaged, () => SharedArrayArgumentKernels.LaunchInIntAndWritableUIntKernel(x, (uint[])(object)x));

        Assert.Equal([5, 1], x);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void InEnumAndWritableInt_SameArray_InSeesWrite(bool forceManaged)
    {
        var x = new int[2];

        Run(forceManaged, () => SharedArrayArgumentKernels.LaunchInEnumAndWritableIntKernel((SharedArrayTag[])(object)x, x));

        Assert.Equal([3, 4], x);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoInDifferentTypes_SameArray_ShareWritesButNotCopiedBack(bool forceManaged)
    {
        var x = new int[1];
        var result = new uint[1];

        Run(forceManaged, () => SharedArrayArgumentKernels.LaunchTwoInDifferentTypesKernel(x, (uint[])(object)x, result));

        Assert.Equal([9u], result);
        Assert.Equal([0], x);
    }

    private void Run(bool forceManaged, Action launch)
    {
        Skip.If(forceManaged && IsCudaDialect, "The managed execution path exists only on the CPU dialects.");

        var previous = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = forceManaged;
        try
        {
            launch();
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previous;
        }
    }
}
