#nullable enable
using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

public partial class NullArrayFieldTests
{
    private const string KernelFile = "NullArrayFieldKernels.cu.cs";
    private const string NullReadMessage = "G#: a null array is read at ";
    private const string NullReadSiteMessage = "G#: a null array is read in ";

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Length_NullField_ThrowsNullReference(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullRead(
            forceManaged,
            "(28,16)",
            () => NullArrayFieldKernels.LaunchLengthKernel(new NullArrayHolder { Data = null }, o)
        );
        Assert.Equal([-1], o);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadElement_NullField_ThrowsNullReferenceNotIndexOutOfRange(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullRead(
            forceManaged,
            "(35,16)",
            () => NullArrayFieldKernels.LaunchReadElementKernel(new NullArrayHolder { Data = null }, o)
        );
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteElement_NullField_ThrowsNullReference(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullRead(
            forceManaged,
            "(41,9)",
            () => NullArrayFieldKernels.LaunchWriteElementKernel(new NullArrayHolder { Data = null }, o)
        );
        Assert.Equal([-1], o);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ForEach_NullField_ThrowsNullReference(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullRead(
            forceManaged,
            "(48,27)",
            () => NullArrayFieldKernels.LaunchForEachKernel(new NullArrayHolder { Data = null }, o)
        );
    }

    // The CPU paths lower a foreach that holds a barrier to a for loop over a copy of the source
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ForEachWithBarrier_NullField_ThrowsNullReference(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullRead(
            forceManaged,
            "(132,27)",
            () => NullArrayFieldKernels.LaunchForEachWithBarrierKernel(new NullArrayHolder { Data = null }, o)
        );
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalPassedToDeviceFunction_NullField_ThrowsInTheDeviceFunction(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullRead(
            forceManaged,
            "(126,41)",
            () => NullArrayFieldKernels.LaunchLocalToDeviceFunctionKernel(new NullArrayHolder { Data = null }, o),
            inDeviceFunction: true
        );
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultLocal_NullField_ThrowsNullReference(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullRead(forceManaged, "(66,16)", () => NullArrayFieldKernels.LaunchDefaultLocalKernel(o));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void NullOptionalArgumentToDeviceFunction_ThrowsInTheFunction(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullRead(
            forceManaged,
            "(126,41)",
            () => NullArrayFieldKernels.LaunchOptionalToDeviceFunctionKernel(o, null),
            inDeviceFunction: true
        );

        Run(forceManaged, () => NullArrayFieldKernels.LaunchOptionalToDeviceFunctionKernel(o, [1, 2, 3]));
        Assert.Equal([3], o);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void StructArray_NullFieldOfSecondElement_ThrowsNullReference(bool forceManaged)
    {
        var o = new[] { -1, -1 };
        NullArrayHolder[] holders = [new() { Data = [1] }, new() { Data = null }];

        AssertNullRead(forceManaged, "(113,16)", () => NullArrayFieldKernels.LaunchStructArrayKernel(holders, o));
    }

    // C# evaluates the index before it throws for the null array: Bump has written o[1] (#878 review)
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadElement_IndexWithSideEffect_RunsTheIndexBeforeThrowing(bool forceManaged)
    {
        var o = new[] { -1, -1 };

        AssertNullRead(
            forceManaged,
            "(143,16)",
            () => NullArrayFieldKernels.LaunchIndexSideEffectKernel(new NullArrayHolder { Data = null }, o)
        );
        Assert.Equal([-1, 5], o);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteElement_IndexWithSideEffect_RunsTheIndexBeforeThrowing(bool forceManaged)
    {
        var o = new[] { -1, -1 };

        AssertNullRead(
            forceManaged,
            "(149,9)",
            () => NullArrayFieldKernels.LaunchStoreIndexSideEffectKernel(new NullArrayHolder { Data = null }, o)
        );
        Assert.Equal([-1, 5], o);
    }

    // The array is read before the index replaces the struct it came from (the receiver and the index interfere)
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadElement_IndexReplacesTheReceiverStruct_ReadsTheArrayFirst(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullRead(
            forceManaged,
            "(190,16)",
            () => NullArrayFieldKernels.LaunchIndexReplacesReceiverKernel(
                new NullArrayHolder { Data = null },
                new NullArrayHolder { Data = [42] },
                o
            )
        );
        Assert.Equal([-1], o);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void WrittenParameterWithBarrier_NullField_ThrowsNullReference(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullRead(
            forceManaged,
            "(159,16)",
            () => NullArrayFieldKernels.LaunchWrittenParameterWithBarrierKernel(
                new NullArrayHolder { Data = null },
                [1, 2],
                o
            )
        );
    }

    // A read of an optional parameter that the kernel assigned follows C# on every path
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void WrittenOptional_NullField_ThrowsNullReference(bool forceManaged, bool biasIsNull)
    {
        var o = new[] { -1 };

        AssertNullRead(
            forceManaged,
            "(170,16)",
            () => NullArrayFieldKernels.LaunchWrittenOptionalKernel(
                new NullArrayHolder { Data = null },
                o,
                biasIsNull ? null : [1]
            )
        );
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalWithBarrier_NullThrowsInTheFunctionAndValueReadsItsLength(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullRead(
            forceManaged,
            "(126,41)",
            () => NullArrayFieldKernels.LaunchOptionalWithBarrierKernel(o, null),
            inDeviceFunction: true
        );

        Run(forceManaged, () => NullArrayFieldKernels.LaunchOptionalWithBarrierKernel(o, [1, 2]));
        Assert.Equal([2], o);
    }

    // The native CPU build names the place of the receiver: in a kernel body as the site of the kernel's error site
    // table (GSharp.SourceLocation), in a device function, which has no table, in the message. The managed path throws
    // .NET's own exception
    private void AssertNullRead(bool forceManaged, string place, Action launch, bool inDeviceFunction = false)
    {
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
}
