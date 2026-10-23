using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public struct LoopMacroState
{
    public int Sum;
}

// Forms of the loop macros, the unroll hint and stack arrays that nothing reports
public static partial class SupportedMacroAndArrayFormKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void LoopMacroFormsKernel(int[] data, int mode)
    {
        var state = new LoopMacroState();
        Gpu.ForUnroll(0, 4, (i) =>
        {
            data[i] += 1;
        });
        Gpu.ForUnroll(ref state, 0, 4, (ref LoopMacroState s, int i) =>
        {
            s.Sum += i;
        });
        Gpu.GridStrideLoop(data.Length, i =>
        {
            data[i] += 2;
        });
        Gpu.GridStrideLoop(ref state, data.Length, (ref LoopMacroState s, int i) =>
        {
            s.Sum += data[i];
        });

        switch (mode)
        {
            case 0:
                Gpu.Hint.Unroll(2);
                for (int i = 0; i < 4; i++)
                {
                    data[0] += i;
                }

                break;
        }

        data[1] = state.Sum;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void StackArrayWithInitializerKernel(int[] data)
    {
        Span<int> values = stackalloc int[] { 1, 2, 3 };
        data[0] = values[0];
    }
}
