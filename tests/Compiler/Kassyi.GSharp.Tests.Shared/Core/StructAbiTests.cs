using System.Runtime.InteropServices;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Pins the layout of the native shadow structs (<c>AotContexts.*</c>) that a <c>ref</c> struct parameter is marshaled
/// through, against the layout of the same struct in device code.
/// </summary>
public class StructAbiTests
{
    private static (int OffsetOfC, int Size) MeasureOnDevice(System.Action<int[]> launch)
    {
        var layout = new int[2];
        launch(layout);
        return (layout[0], layout[1]);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void PackedStruct_ShadowMatchesDevice()
    {
        var device = MeasureOnDevice(static layout => StructAbiKernels.LaunchMeasurePackedKernel(layout));

        Assert.Equal((9, 13), device);
        Assert.Equal(9, (int)Marshal.OffsetOf<StructAbiKernels.AotContexts.AbiPackedStruct>("C"));
        Assert.Equal(13, Marshal.SizeOf<StructAbiKernels.AotContexts.AbiPackedStruct>());
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void PackedStruct_RoundtripsThroughShadow()
    {
        var data = new AbiPackedStruct { A = 1, B = 2.5, C = 7 };

        StructAbiKernels.LaunchRoundtripPackedKernel(ref data);

        Assert.Equal(2, data.A);
        Assert.Equal(2.5, data.B);
        Assert.Equal(8, data.C);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void InlineArrayField_ShadowHoldsOneElement()
    {
        var device = MeasureOnDevice(static layout => StructAbiKernels.LaunchMeasureInlineArrayKernel(layout));

        Assert.Equal((20, 24), device);
        // Known mismatch (#468): the shadow struct sizes the inline array by its one declared field
        Assert.Equal(8, (int)Marshal.OffsetOf<StructAbiKernels.AotContexts.AbiInlineArrayStruct>("C"));
        Assert.Equal(12, Marshal.SizeOf<StructAbiKernels.AotContexts.AbiInlineArrayStruct>());
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void CoreFieldStruct_ShadowMatchesDevice()
    {
        var device = MeasureOnDevice(static layout => StructAbiKernels.LaunchMeasureCoreFieldKernel(layout));

        Assert.Equal((16, 20), device);
        Assert.Equal(16, (int)Marshal.OffsetOf<StructAbiKernels.AotContexts.AbiCoreFieldStruct>("C"));
        Assert.Equal(20, Marshal.SizeOf<StructAbiKernels.AotContexts.AbiCoreFieldStruct>());
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void CoreFieldStruct_RoundtripsThroughShadow()
    {
        var data = new AbiCoreFieldStruct { A = 1, B = new Kassyi.GSharp.Core.Float3(2f, 3f, 4f), C = 7 };

        StructAbiKernels.LaunchRoundtripCoreFieldKernel(ref data);

        Assert.Equal(2, data.A);
        Assert.Equal(new Kassyi.GSharp.Core.Float3(2f, 3f, 4f), data.B);
        Assert.Equal(8, data.C);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void ComputedPropertyStruct_DeviceDeclaresTheComputedProperty()
    {
        var device = MeasureOnDevice(static layout => StructAbiKernels.LaunchMeasureComputedPropertyKernel(layout));

        // Known mismatch (#468, to be fixed in the follow-up stage): the device declares the computed property Sum as
        // a member (C at 8, size 12) while the host layout holds only the real fields A and C (C at 4, size 8).
        Assert.Equal((8, 12), device);
        Assert.Equal(4, (int)Marshal.OffsetOf<StructAbiKernels.AotContexts.AbiComputedPropertyStruct>("C"));
        Assert.Equal(8, Marshal.SizeOf<StructAbiKernels.AotContexts.AbiComputedPropertyStruct>());
    }
}
