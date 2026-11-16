#nullable enable
using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

public partial class GpuNotNullTests
{
    private const string KernelFile = "GpuNotNullKernels.cu.cs";
    private const string SiteMessage = "G#: Gpu.NotNull was given a null array in ";
    private const string DeviceFunctionMessage = "G#: Gpu.NotNull was given a null array at ";

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Sum_NullField_ThrowsAtTheCall(bool forceManaged)
    {
        var o = new[] { -1, -1 };

        AssertNullAtCall(
            forceManaged,
            "(20,22)",
            () => GpuNotNullKernels.LaunchSumKernel(new NotNullHolder { Data = null, Offset = 10 }, o)
        );
        Assert.Equal([-1, -1], o);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void DeviceFunction_NullField_ThrowsWithThePlaceInTheMessage(bool forceManaged)
    {
        var o = new[] { -1 };

        AssertNullAtCall(
            forceManaged,
            "(49,52)",
            () => GpuNotNullKernels.LaunchDeviceFunctionKernel(new NotNullHolder { Data = null }, o),
            inDeviceFunction: true
        );
        Assert.Equal([-1], o);
    }

    // The launchers pass a null optional argument as an empty array, whose Length reads 0; Gpu.NotNull still sees null
    [Theory]
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
        var exception = Assert.Throws<NullReferenceException>(() => Run(forceManaged, launch));
        if (forceManaged)
        {
            Assert.Equal("Gpu.NotNull was given a null array", exception.Message);
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
}
