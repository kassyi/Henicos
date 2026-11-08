// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public static partial class SwitchExpressionKernels
{
    [GpuKernel]
    public static void SimpleConstantSwitchKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        int val = input[tid];
        output[tid] = val switch
        {
            1 => 100,
            2 => 200,
            3 => 300,
            _ => -1
        };
    }

    [GpuKernel]
    public static void FloatingPointReturnSwitchKernel(int[] input, float[] output)
    {
        int tid = ThreadIndex.X;
        int mode = input[tid];
        output[tid] = mode switch
        {
            0 => 0.0f,
            1 => 3.14159f,
            2 => 2.71828f,
            _ => -1.0f
        };
    }

    [GpuKernel]
    public static void MultipleArmsBoundarySwitchKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        int val = input[tid];
        output[tid] = val switch
        {
            int.MinValue => 1,
            -1 => 2,
            0 => 3,
            1 => 4,
            int.MaxValue => 5,
            _ => 0
        };
    }

#pragma warning disable CS8509 // Without a discard arm the switch ends with the trap of an unmatched input
    [GpuKernel]
    public static void NoDiscardArmSwitchKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = input[tid] switch
        {
            0 => 10,
            1 => 20
        } + NoDiscardArm(input[tid]);
    }

    [GpuDeviceFunction]
    public static int NoDiscardArm(int value) => value switch
    {
        0 => 1,
        1 => 2
    };
#pragma warning restore CS8509
}
