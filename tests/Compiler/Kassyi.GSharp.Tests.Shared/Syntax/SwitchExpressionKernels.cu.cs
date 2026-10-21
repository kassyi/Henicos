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
}
