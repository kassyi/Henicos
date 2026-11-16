using System;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Reads of a null array field follow C# (#706): <c>Length</c>, an element and <c>foreach</c> throw
/// <see cref="NullReferenceException"/> on the native CPU build (with the place in the user's source) and on the managed
/// path, while an empty array reads as length 0 and is not null on every path. A null field that the kernel does not read
/// is no error, and a null kernel argument is read as C# reads it: it throws as well.
/// </summary>
/// <remarks>
/// CUDA checks a null array read on every build and reports its place through the device error record (#1409), but the
/// trap leaves the CUDA context of the test process unusable, so the error cases are in <c>Tests.Cpu</c>; the
/// differential null cases (<c>BoundsAndMemoryDifferentialTests</c>) run them in a child process.
/// </remarks>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public partial class NullArrayFieldTests : TranspilerTestBase
{
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ForEachWithBarrier_Field_SumsElements(bool forceManaged)
    {
        var o = new[] { 0 };

        Run(forceManaged, () =>
            NullArrayFieldKernels.LaunchForEachWithBarrierKernel(new NullArrayHolder { Data = [1, 2, 3] }, o));

        Assert.Equal([6], o);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyField_ReadsLengthZeroAndIsNotNull(bool forceManaged)
    {
        var o = new[] { -1, -1 };

        Run(forceManaged, () => NullArrayFieldKernels.LaunchEmptyFieldKernel(new NullArrayHolder { Data = [] }, o));

        Assert.Equal([7, 0], o);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void UnreadNullField_LaunchSucceedsAndIsNull(bool forceManaged)
    {
        var o = new[] { -1, -1 };

        Run(forceManaged, () =>
            NullArrayFieldKernels.LaunchUnreadNullFieldKernel(new NullArrayHolder { Data = null, X = 5 }, o));

        Assert.Equal([5, 1], o);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void NullFieldToSpan_IsEmptySpan(bool forceManaged)
    {
        var o = new[] { -1 };

        Run(forceManaged, () => NullArrayFieldKernels.LaunchSpanKernel(new NullArrayHolder { Data = null }, o));

        Assert.Equal([0], o);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedRegionAndEmptyField_WriteSeenAndEmptyIsNotNull(bool forceManaged)
    {
        var x = new[] { 0, 0 };
        var o = new[] { -1, -1 };

        Run(forceManaged, () => NullArrayFieldKernels.LaunchSharedEmptyKernel(new NullArrayPair { A = x, B = [] }, o));

        Assert.Equal([9, 0], x);
        Assert.Equal([0, 0], o);
    }

    // The managed execution path exists only on the CPU dialects
    private void Run(bool forceManaged, Action launch)
    {
        Skip.If(forceManaged && IsCudaDialect, "The managed execution path exists only on the CPU dialects.");

        using (GpuExecution.Use(forceManaged ? KernelExecution.Managed : KernelExecution.Default))
        {
            launch();
        }
    }
}
