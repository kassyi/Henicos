using System;
using Kassyi.GSharp.Core;

public struct MutatingGuard : IDisposable
{
    public int[] Log;
    public int State;

    public int StepState
    {
        get
        {
            State += 100;
            return State;
        }
    }

    public void Dispose()
    {
        Log[0] = State;
    }
}

public static partial class MutatingPropertyKernelHolder
{
    [GpuKernel]
    public static void Kernel(int[] r)
    {
        using var g = new MutatingGuard { Log = r, State = 5 };
        r[1] = g.StepState;
    }
}
