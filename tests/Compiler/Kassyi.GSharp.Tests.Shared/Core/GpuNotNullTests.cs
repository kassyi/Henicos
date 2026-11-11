using System;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// <c>Gpu.NotNull</c> returns its array, and throws <see cref="NullReferenceException"/> at the call when the array is
/// null: on the native CPU build with the place of the call, on the managed path from the C# method. A null optional
/// kernel argument, which reads as an empty array, is null there too, and so is an empty one, which the launchers pass
/// as null.
/// </summary>
/// <remarks>
/// The null cases are skipped on CUDA, as in <see cref="NullArrayFieldTests"/>; the differential case
/// <c>NullValidation_GpuNotNull_ThrowsAtTheCall</c> runs them on CUDA with the place of the call.
/// </remarks>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class GpuNotNullTests : TranspilerTestBase
{
    private const string KernelFile = "GpuNotNullKernels.cu.cs";
    private const string SiteMessage = "G#: Gpu.NotNull is given a null array in ";
    private const string DeviceFunctionMessage = "G#: Gpu.NotNull is given a null array at ";

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Sum_NonNullField_ReadsTheArrayThroughTheLocal(bool forceManaged)
    {
        var o = new[] { -1, -1 };

        Run(forceManaged, () => GpuNotNullKernels.LaunchSumKernel(new NotNullHolder { Data = [1, 2, 3], Offset = 10 }, o));

        Assert.Equal([16, 1], o);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void DeviceFunction_NonNullField_ReturnsTheFirstElement(bool forceManaged)
    {
        var o = new[] { -1 };

        Run(forceManaged, () => GpuNotNullKernels.LaunchDeviceFunctionKernel(new NotNullHolder { Data = [7, 8] }, o));

        Assert.Equal([7], o);
    }

    // An empty array is not null
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Length_EmptyField_IsNotNull(bool forceManaged)
    {
        var o = new[] { -1 };

        Run(forceManaged, () => GpuNotNullKernels.LaunchLengthKernel(new NotNullHolder { Data = [] }, o));

        Assert.Equal([0], o);
    }

    // The launchers pass an empty optional kernel argument as null on every path (#838), so Gpu.NotNull throws for it
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Optional_EmptyArgument_IsPassedAsNullAndThrows(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullAtCall(forceManaged, "(42,16)", () => GpuNotNullKernels.LaunchOptionalKernel(o, []));
        Assert.Equal([-1], o);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Optional_NonNullArgument_ReadsItsLength(bool forceManaged)
    {
        var o = new[] { -1 };

        Run(forceManaged, () => GpuNotNullKernels.LaunchOptionalKernel(o, [1, 2, 3]));

        Assert.Equal([3], o);
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Sum_NullField_ThrowsAtTheCall(bool forceManaged)
    {
        var o = new[] { -1, -1 };

        AssertNullAtCall(forceManaged, "(20,22)",
            () => GpuNotNullKernels.LaunchSumKernel(new NotNullHolder { Data = null, Offset = 10 }, o));
        Assert.Equal([-1, -1], o);
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void DeviceFunction_NullField_ThrowsWithThePlaceInTheMessage(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullAtCall(forceManaged, "(49,52)",
            () => GpuNotNullKernels.LaunchDeviceFunctionKernel(new NotNullHolder { Data = null }, o),
            inDeviceFunction: true);
        Assert.Equal([-1], o);
    }

    // The launchers pass a null optional argument as an empty array, whose Length reads 0; Gpu.NotNull still sees null
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Optional_NullArgument_ThrowsAtTheCall(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullAtCall(forceManaged, "(42,16)", () => GpuNotNullKernels.LaunchOptionalKernel(o, null));
        Assert.Equal([-1], o);
    }

    // The native CPU build names the place of the call: in a kernel body as the site of the kernel's error site table
    // (GSharp.SourceLocation), in a device function in the message. The managed path runs the C# method
    private void AssertNullAtCall(bool forceManaged, string place, Action launch, bool inDeviceFunction = false)
    {
        Skip.If(IsCudaDialect,
            "The CUDA null cases run as a differential case (NullValidation_GpuNotNull_ThrowsAtTheCall).");

        var exception = Assert.Throws<NullReferenceException>(() => Run(forceManaged, launch));
        if (forceManaged)
        {
            Assert.Equal("Gpu.NotNull was given a null array.", exception.Message);
            return;
        }

        if (inDeviceFunction)
        {
            Assert.Equal(DeviceFunctionMessage + KernelFile + place, exception.Message);
            return;
        }

        Assert.Equal(KernelFile + place, exception.Data["GSharp.SourceLocation"]);
        Assert.StartsWith(SiteMessage, exception.Message, StringComparison.Ordinal);
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
