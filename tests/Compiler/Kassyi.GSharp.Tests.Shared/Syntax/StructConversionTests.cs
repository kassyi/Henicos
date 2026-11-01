using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct PropW
{
    public int V;

    public PropW(int v) => V = v;

    public static implicit operator PropW(int v) => new PropW(v * 2);

    public static implicit operator int(PropW w) => w.V + 10;

    public static explicit operator PropW(short s) => new PropW(s * 3);

    public static explicit operator short(PropW w) => (short)(w.V + 5);
}

public struct ScaledIntConv
{
    public int Value;
    public int Unset;

    public static implicit operator ScaledIntConv(int v) =>
        new ScaledIntConv { Value = (v * 4) + 3 };

    public static implicit operator int(ScaledIntConv s) =>
        s.Value + 100;
}

public struct BlockConvStruct
{
    public int A;
    public int B;

    public static implicit operator BlockConvStruct(int v)
    {
        int a = v + 1;
        int b = v * 3;
        return new BlockConvStruct { A = a, B = b };
    }

    public static implicit operator int(in BlockConvStruct s)
    {
        int sum = s.A + s.B;
        return sum + 50;
    }
}

public static partial class StructConversionKernels
{
    [GpuDeviceFunction]
    public static int TakesPropW(PropW p) => p.V;

    [GpuDeviceFunction]
    public static int TakesInPropW(in PropW p) => p.V;

    [GpuDeviceFunction]
    public static int TakesInt(int x) => x;

    [GpuDeviceFunction]
    public static PropW ReturnsPropW(int v) => v;

    [GpuDeviceFunction]
    public static int ReturnsInt(PropW p) => p;

    [GpuKernel]
    public static void PropWConversionKernel(int flag, int[] output)
    {
        PropW p = 4;
        output[0] = p.V;
        output[1] = p;

        PropW constructed = new PropW(4);
        output[2] = constructed.V;

        p = 7;
        output[3] = p.V;

        output[4] = TakesPropW(5);
        output[5] = TakesInt(p);
        output[6] = TakesInPropW(6);

        PropW retProp = ReturnsPropW(3);
        output[7] = retProp.V;
        output[8] = ReturnsInt(retProp);

        PropW castProp = (PropW)9;
        output[9] = castProp.V;
        output[10] = (int)castProp;

        PropW expProp = (PropW)(short)4;
        output[11] = expProp.V;
        output[12] = (short)expProp;

        PropW tern = flag != 0 ? p : 11;
        output[13] = tern.V;
    }

    [GpuKernel]
    public static void ScaledAndBlockConversionKernel(int[] inputs, int[] output)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        int v = inputs[tid];
        ScaledIntConv scaled = v;
        int back = scaled;

        BlockConvStruct blockConv = v;
        int blockBack = blockConv;

        int o = tid * 6;
        output[o + 0] = scaled.Value;
        output[o + 1] = scaled.Unset;
        output[o + 2] = back;
        output[o + 3] = blockConv.A;
        output[o + 4] = blockConv.B;
        output[o + 5] = blockBack;
    }
}

[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class StructConversionTests : TranspilerTestBase
{
    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_PropW_Conversions_NativeAndManaged()
    {
        int[] expected = [8, 18, 4, 14, 10, 24, 12, 6, 16, 18, 28, 12, 17, 22];

        var nativeOutput = new int[expected.Length];
        StructConversionKernels.LaunchPropWConversionKernel(0, nativeOutput, blockDimX: 1);
        Assert.Equal(expected, nativeOutput);

#if GSHARP_CPU
        var managedOutput = new int[expected.Length];
        ManagedKernelRun.Run(() => StructConversionKernels.LaunchPropWConversionKernel(0, managedOutput, blockDimX: 1));
        Assert.Equal(expected, managedOutput);
#endif
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_ScaledAndBlockConversions_NativeAndManaged()
    {
        int[] inputs = [-3, 0, 5, 12];
        var nativeOutput = new int[inputs.Length * 6];
        StructConversionKernels.LaunchScaledAndBlockConversionKernel(inputs, nativeOutput, blockDimX: (uint)inputs.Length);

        var expected = new int[inputs.Length * 6];
        for (var i = 0; i < inputs.Length; i++)
        {
            var v = inputs[i];
            var scaled = (v * 4) + 3;
            var o = i * 6;
            expected[o + 0] = scaled;
            expected[o + 1] = 0;
            expected[o + 2] = scaled + 100;
            expected[o + 3] = v + 1;
            expected[o + 4] = v * 3;
            expected[o + 5] = (v + 1) + (v * 3) + 50;
        }

        Assert.Equal(expected, nativeOutput);

#if GSHARP_CPU
        var managedOutput = new int[inputs.Length * 6];
        ManagedKernelRun.Run(() =>
            StructConversionKernels.LaunchScaledAndBlockConversionKernel(inputs, managedOutput, blockDimX: (uint)inputs.Length));
        Assert.Equal(expected, managedOutput);
#endif
    }
}
