using System;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Reads of a null array field follow C# (#706): <c>Length</c>, an element and <c>foreach</c> throw
/// <see cref="NullReferenceException"/> on the native CPU build (with the place in the user's source) and on the managed
/// path, while an empty array reads as length 0 and is not null on every path. A null field that the kernel does not read
/// is no error, and a null optional kernel argument keeps reading as an empty array (#838).
/// </summary>
/// <remarks>
/// CUDA checks a null array read only in the checked build (#716), and reports its place only with the device error
/// record (#729); until then a trap also poisons the CUDA context of the test process, so the error cases are skipped on
/// CUDA.
/// </remarks>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class NullArrayFieldTests : TranspilerTestBase
{
    private const string KernelFile = "NullArrayFieldKernels.cu.cs";
    private const string NullReadMessage = "G#: a null array is read at ";

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Length_NullField_ThrowsNullReference(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullRead(forceManaged, "(26,16)",
            () => NullArrayFieldKernels.LaunchLengthKernel(new NullArrayHolder { Data = null }, o));
        Assert.Equal([-1], o);
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadElement_NullField_ThrowsNullReferenceNotIndexOutOfRange(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullRead(forceManaged, "(33,16)",
            () => NullArrayFieldKernels.LaunchReadElementKernel(new NullArrayHolder { Data = null }, o));
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteElement_NullField_ThrowsNullReference(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullRead(forceManaged, "(39,9)",
            () => NullArrayFieldKernels.LaunchWriteElementKernel(new NullArrayHolder { Data = null }, o));
        Assert.Equal([-1], o);
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ForEach_NullField_ThrowsNullReference(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullRead(forceManaged, "(46,27)",
            () => NullArrayFieldKernels.LaunchForEachKernel(new NullArrayHolder { Data = null }, o));
    }

    // The CPU paths lower a foreach that holds a barrier to a for loop over a copy of the source
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ForEachWithBarrier_NullField_ThrowsNullReference(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullRead(forceManaged, "(129,27)",
            () => NullArrayFieldKernels.LaunchForEachWithBarrierKernel(new NullArrayHolder { Data = null }, o));
    }

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
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalPassedToDeviceFunction_NullField_ThrowsInTheDeviceFunction(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullRead(forceManaged, "(123,40)",
            () => NullArrayFieldKernels.LaunchLocalToDeviceFunctionKernel(new NullArrayHolder { Data = null }, o));
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultLocal_NullField_ThrowsNullReference(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullRead(forceManaged, "(64,16)", () => NullArrayFieldKernels.LaunchDefaultLocalKernel(o));
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
    public void NullOptionalArgumentToDeviceFunction_ReadsLengthZero(bool forceManaged)
    {
        Skip.If(forceManaged, "The managed path reads a null optional argument as null outside the kernel itself (#852).");
        var o = new[] { -1 };

        Run(forceManaged, () => NullArrayFieldKernels.LaunchOptionalToDeviceFunctionKernel(o, null));
        Assert.Equal([0], o);

        Run(forceManaged, () => NullArrayFieldKernels.LaunchOptionalToDeviceFunctionKernel(o, [1, 2, 3]));
        Assert.Equal([3], o);
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void StructArray_NullFieldOfSecondElement_ThrowsNullReference(bool forceManaged)
    {
        var o = new[] { -1, -1 };
        NullArrayHolder[] holders = [new() { Data = [1] }, new() { Data = null }];

        AssertNullRead(forceManaged, "(110,16)",
            () => NullArrayFieldKernels.LaunchStructArrayKernel(holders, o));
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

    // The native CPU build names the place of the receiver; the managed path throws .NET's own exception
    private void AssertNullRead(bool forceManaged, string place, Action launch)
    {
        Skip.If(IsCudaDialect,
            "CUDA checks a null array read only in the checked build (#716) and reports it only with the device error record (#729).");

        var exception = Assert.Throws<NullReferenceException>(() => Run(forceManaged, launch));
        if (!forceManaged)
        {
            Assert.Equal(NullReadMessage + KernelFile + place, exception.Message);
        }
    }

    // The managed execution path exists only on the CPU dialects
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
