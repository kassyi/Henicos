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
    public void InlineArrayField_ShadowMatchesDevice()
    {
        var device = MeasureOnDevice(static layout => StructAbiKernels.LaunchMeasureInlineArrayKernel(layout));

        Assert.Equal((20, 24), device);
        Assert.Equal(20, (int)Marshal.OffsetOf<StructAbiKernels.AotContexts.AbiInlineArrayStruct>("C"));
        Assert.Equal(24, Marshal.SizeOf<StructAbiKernels.AotContexts.AbiInlineArrayStruct>());
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void InlineArrayField_RoundtripsThroughShadow()
    {
        var data = new AbiInlineArrayStruct { A = 1, C = 7 };
        data.B[2] = 5;

        StructAbiKernels.LaunchRoundtripInlineArrayKernel(ref data);

        Assert.Equal(2, data.A);
        Assert.Equal(5, data.B[2]);
        Assert.Equal(8, data.C);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void PrivateInlineArrayField_ShadowMatchesDevice()
    {
        var device = MeasureOnDevice(static layout => StructAbiKernels.LaunchMeasurePrivateInlineArrayKernel(layout));

        Assert.Equal((20, 24), device);
        Assert.Equal(20, (int)Marshal.OffsetOf<StructAbiKernels.AotContexts.AbiPrivateInlineArrayStruct>("C"));
        Assert.Equal(24, Marshal.SizeOf<StructAbiKernels.AotContexts.AbiPrivateInlineArrayStruct>());
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void PrivateInlineArrayField_RoundtripsThroughShadow()
    {
        var data = new AbiPrivateInlineArrayStruct { A = 1, C = 7 };
        data.B[3] = 9;

        StructAbiKernels.LaunchRoundtripPrivateInlineArrayKernel(ref data);

        Assert.Equal(2, data.A);
        Assert.Equal(9, data.B[3]);
        Assert.Equal(8, data.C);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void FixedBuffer_ShadowMatchesDevice()
    {
        var device = MeasureOnDevice(static layout => StructAbiKernels.LaunchMeasureFixedBufferKernel(layout));

        Assert.Equal((20, 24), device);
        Assert.Equal(20, (int)Marshal.OffsetOf<StructAbiKernels.AotContexts.AbiFixedBufferStruct>("C"));
        Assert.Equal(24, Marshal.SizeOf<StructAbiKernels.AotContexts.AbiFixedBufferStruct>());
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public unsafe void FixedBuffer_RoundtripsThroughShadow()
    {
        var data = new AbiFixedBufferStruct { A = 1, C = 7 };
        data.B[1] = 6;

        StructAbiKernels.LaunchRoundtripFixedBufferKernel(ref data);

        Assert.Equal(2, data.A);
        Assert.Equal(6, data.B[1]);
        Assert.Equal(8, data.C);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void AutoProperty_ShadowMatchesDevice()
    {
        var device = MeasureOnDevice(static layout => StructAbiKernels.LaunchMeasureAutoPropertyKernel(layout));

        Assert.Equal((16, 24), device);
        Assert.Equal(16, (int)Marshal.OffsetOf<StructAbiKernels.AotContexts.AbiAutoPropertyStruct>("C"));
        Assert.Equal(24, Marshal.SizeOf<StructAbiKernels.AotContexts.AbiAutoPropertyStruct>());
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void AutoProperty_RoundtripsThroughShadow()
    {
        var data = new AbiAutoPropertyStruct { A = 1, B = 2.5, C = 7 };

        StructAbiKernels.LaunchRoundtripAutoPropertyKernel(ref data);

        Assert.Equal(2, data.A);
        Assert.Equal(3.5, data.B);
        Assert.Equal(8, data.C);
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
    public void ComputedPropertyStruct_ShadowMatchesDevice()
    {
        var device = MeasureOnDevice(static layout => StructAbiKernels.LaunchMeasureComputedPropertyKernel(layout));

        Assert.Equal((4, 8), device);
        Assert.Equal(4, (int)Marshal.OffsetOf<StructAbiKernels.AotContexts.AbiComputedPropertyStruct>("C"));
        Assert.Equal(8, Marshal.SizeOf<StructAbiKernels.AotContexts.AbiComputedPropertyStruct>());
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void ComputedPropertyStruct_RoundtripsThroughShadow()
    {
        var data = new AbiComputedPropertyStruct { A = 1, C = 7 };

        StructAbiKernels.LaunchRoundtripComputedPropertyKernel(ref data);

        Assert.Equal(1, data.A);
        Assert.Equal(8, data.C);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void ComputedProperty_ReadsOnDevice()
    {
        var result = new int[4];

        StructAbiKernels.LaunchReadComputedPropertyKernel(result);

        // A = 4, C = 7: Sum = A + 1, Twice = Sum * 2, Product = A * C, Difference = C - Sum
        Assert.Equal([5, 10, 28, 2], result);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void ComputedProperty_ReadsThroughRefOnDevice()
    {
        var data = new AbiComputedPropertyStruct { A = 2, C = 9 };
        var result = new int[4];

        StructAbiKernels.LaunchReadComputedPropertyThroughRefKernel(ref data, result);

        Assert.Equal([3, 6, 18, 6], result);
        Assert.Equal(2, data.A);
        Assert.Equal(9, data.C);
    }
}
