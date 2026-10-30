#nullable enable
using System;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Struct arguments whose array fields hold the same array: in C# they are one array, so a write through one field is
/// seen through the other. The CUDA launcher copies such an array to one device region (#707); the CPU paths pass the
/// array itself.
/// </summary>
/// <remarks>
/// Each kernel writes element 0 through one field and then reads it through the other, so a launcher that copies the
/// array twice gives a different element 1, and its copy back of the second region loses element 0.
/// </remarks>
[Category(TestCategory.Runtime)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class SharedArrayFieldTests : TranspilerTestBase
{
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoFields_SameArray_ShareWrites(bool forceManaged)
    {
        var x = new int[2];

        Run(forceManaged, () => SharedArrayFieldKernels.LaunchTwoFieldsKernel(new SharedArrayPair { A = x, B = x }));

        Assert.Equal([1, 2], x);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoFields_DifferentArrays_StayApart(bool forceManaged)
    {
        var a = new int[2];
        var b = new int[2];

        Run(forceManaged, () => SharedArrayFieldKernels.LaunchTwoFieldsKernel(new SharedArrayPair { A = a, B = b }));

        Assert.Equal([1, 0], a);
        Assert.Equal([0, 1], b);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoStructArguments_SameArray_ShareWrites(bool forceManaged)
    {
        var x = new int[2];

        Run(forceManaged, () => SharedArrayFieldKernels.LaunchTwoStructsKernel(
            new SharedArrayHolder { Data = x },
            new SharedArrayHolder { Data = x }
        ));

        Assert.Equal([3, 6], x);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ConvertedElements_SameArray_ShareWrites(bool forceManaged)
    {
        var x = new int[2];
        var bags = new[]
        {
            new ConvertBag { N = 1, Data = x },
            new ConvertBag { N = 2, Data = x }
        };

        Run(forceManaged, () => SharedArrayFieldKernels.LaunchElementFieldsKernel(bags));

        Assert.Equal([5, 10], x);
        Assert.Same(x, bags[0].Data);
        Assert.Same(x, bags[1].Data);
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
