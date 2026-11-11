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
/// CUDA checks a null array read on every build and reports its place through the device error record (#1409), but the
/// trap leaves the CUDA context of the test process unusable, so the error cases are skipped on CUDA here; the
/// differential null cases (<c>BoundsAndMemoryDifferentialTests</c>) run them in a child process.
/// </remarks>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class NullArrayFieldTests : TranspilerTestBase
{
    private const string KernelFile = "NullArrayFieldKernels.cu.cs";
    private const string NullReadMessage = "G#: a null array is read at ";
    private const string NullReadSiteMessage = "G#: a null array is read in ";

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Length_NullField_ThrowsNullReference(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullRead(forceManaged, "(28,16)",
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

        AssertNullRead(forceManaged, "(35,16)",
            () => NullArrayFieldKernels.LaunchReadElementKernel(new NullArrayHolder { Data = null }, o));
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteElement_NullField_ThrowsNullReference(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullRead(forceManaged, "(41,9)",
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

        AssertNullRead(forceManaged, "(48,27)",
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

        AssertNullRead(forceManaged, "(132,27)",
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

        AssertNullRead(forceManaged, "(126,41)",
            () => NullArrayFieldKernels.LaunchLocalToDeviceFunctionKernel(new NullArrayHolder { Data = null }, o),
            inDeviceFunction: true);
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultLocal_NullField_ThrowsNullReference(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullRead(forceManaged, "(66,16)", () => NullArrayFieldKernels.LaunchDefaultLocalKernel(o));
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

        AssertNullRead(forceManaged, "(113,16)",
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

    // C# evaluates the index before it throws for the null array: Bump has written o[1] (#878 review)
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadElement_IndexWithSideEffect_RunsTheIndexBeforeThrowing(bool forceManaged)
    {
        var o = new[] { -1, -1 };

        AssertNullRead(forceManaged, "(143,16)",
            () => NullArrayFieldKernels.LaunchIndexSideEffectKernel(new NullArrayHolder { Data = null }, o));
        Assert.Equal([-1, 5], o);
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteElement_IndexWithSideEffect_RunsTheIndexBeforeThrowing(bool forceManaged)
    {
        var o = new[] { -1, -1 };

        AssertNullRead(forceManaged, "(149,9)",
            () => NullArrayFieldKernels.LaunchStoreIndexSideEffectKernel(new NullArrayHolder { Data = null }, o));
        Assert.Equal([-1, 5], o);
    }

    // The array is read before the index replaces the struct it came from (the receiver and the index interfere)
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadElement_IndexReplacesTheReceiverStruct_ReadsTheArrayFirst(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullRead(forceManaged, "(190,16)",
            () => NullArrayFieldKernels.LaunchIndexReplacesReceiverKernel(
                new NullArrayHolder { Data = null },
                new NullArrayHolder { Data = [42] },
                o
            ));
        Assert.Equal([-1], o);
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void WrittenParameterWithBarrier_NullField_ThrowsNullReference(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullRead(forceManaged, "(159,16)",
            () => NullArrayFieldKernels.LaunchWrittenParameterWithBarrierKernel(new NullArrayHolder { Data = null }, [1, 2], o));
    }

    // The managed path rewrote bias.Length to 0 for any optional parameter (#838); once the kernel assigns it, the
    // read follows C# on every path
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void WrittenOptional_NullField_ThrowsNullReference(bool forceManaged, bool biasIsNull)
    {
        var o = new[] { -1 };

        AssertNullRead(forceManaged, "(170,16)",
            () => NullArrayFieldKernels.LaunchWrittenOptionalKernel(new NullArrayHolder { Data = null }, o, biasIsNull ? null : [1]));
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalWithBarrier_NullAndValue_ReadsItsLength(bool forceManaged)
    {
        Skip.If(forceManaged, "The managed path reads a null optional argument as null outside the kernel itself (#852).");
        var o = new[] { -1 };

        Run(forceManaged, () => NullArrayFieldKernels.LaunchOptionalWithBarrierKernel(o, null));
        Assert.Equal([0], o);

        Run(forceManaged, () => NullArrayFieldKernels.LaunchOptionalWithBarrierKernel(o, [1, 2]));
        Assert.Equal([4], o);
    }

    // The native CPU build names the place of the receiver: in a kernel body as the site of the kernel's error site table
    // (GSharp.SourceLocation), in a device function, which has no table, in the message. The managed path throws .NET's
    // own exception
    private void AssertNullRead(bool forceManaged, string place, Action launch, bool inDeviceFunction = false)
    {
        Skip.If(IsCudaDialect,
            "A CUDA trap leaves the context of the test process unusable; the differential null cases run it in a child process (#1409).");

        var exception = Assert.Throws<NullReferenceException>(() => Run(forceManaged, launch));
        if (forceManaged)
        {
            return;
        }

        if (inDeviceFunction)
        {
            Assert.Equal(NullReadMessage + KernelFile + place, exception.Message);
            return;
        }

        Assert.Equal(KernelFile + place, exception.Data["GSharp.SourceLocation"]);
        Assert.StartsWith(NullReadSiteMessage, exception.Message, StringComparison.Ordinal);
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
