using System;
using Kassyi.GSharp.Core;

namespace SharedMemberErrorSiteCases;

public struct DisposeSwitchResource : IDisposable
{
    public int[] Log;
    public int Slot;
    public int Mode;

    public void Dispose()
    {
        Log[Slot] = Log[Slot] + 1;
#pragma warning disable CS8509
        Log[Slot + 8] = Mode switch { 0 => 1 };
#pragma warning restore CS8509
    }
}

public static partial class DisposeSwitchKernels
{
    [GpuKernel]
    public static void FirstKernel(int[] log, int[] outs, int mode)
    {
        using (var r = new DisposeSwitchResource { Log = log, Slot = 1, Mode = mode })
        {
            outs[0] = 1;
        }
    }

    [GpuKernel]
    public static void SecondKernel(int[] log, int[] outs, int mode)
    {
        using (var r = new DisposeSwitchResource { Log = log, Slot = 2, Mode = mode })
        {
            outs[0] = 2;
        }
    }
}

public struct CtorSwitchValue
{
    public int Result;

    public CtorSwitchValue(int mode)
    {
#pragma warning disable CS8509
        Result = mode switch { 0 => 10, 1 => 20 };
#pragma warning restore CS8509
    }
}

public static partial class CtorSwitchKernels
{
    [GpuKernel]
    public static void FirstKernel(int[] outs, int mode)
    {
        var value = new CtorSwitchValue(mode);
        outs[0] = value.Result;
    }

    [GpuKernel]
    public static void SecondKernel(int[] outs, int mode)
    {
        var value = new CtorSwitchValue(mode);
        outs[0] = value.Result + 1;
    }
}

public struct DisposeThrowResource : IDisposable
{
    public int[] Log;
    public int Mode;

    public void Dispose()
    {
        if (Mode < 0)
        {
            throw new InvalidOperationException("bad mode");
        }

        Log[0] = Log[0] + 1;
    }
}

public static partial class DisposeThrowKernels
{
    [GpuKernel]
    public static void FirstKernel(int[] log, int[] outs, int mode)
    {
        using (var r = new DisposeThrowResource { Log = log, Mode = mode })
        {
            outs[0] = 1;
        }
    }

    [GpuKernel]
    public static void SecondKernel(int[] log, int[] outs, int mode)
    {
        using (var r = new DisposeThrowResource { Log = log, Mode = mode })
        {
            outs[0] = 2;
        }
    }
}
