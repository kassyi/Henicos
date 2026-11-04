using System;
using Kassyi.GSharp.Core;

public struct OperatorMacroVal
{
    public int V;

    [GpuOperatorMacro(typeof(OperatorMacroOps), nameof(OperatorMacroOps.Add))]
    public static OperatorMacroVal operator +(OperatorMacroVal a, OperatorMacroVal b) => throw new GpuOnlyException();
}

public static partial class OperatorMacroOps
{
    [GpuDeviceFunction]
    public static void Add(out OperatorMacroVal res, OperatorMacroVal a, OperatorMacroVal b)
    {
        res = new OperatorMacroVal { V = a.V + b.V };
    }
}

public struct DisposableWithMacro : IDisposable
{
    public int[] Log;
    public int X;

    public void Dispose()
    {
        OperatorMacroVal a = new OperatorMacroVal { V = X };
        OperatorMacroVal b = new OperatorMacroVal { V = 5 };
        OperatorMacroVal c = default;
        c = a + b;
        Log[0] = c.V;
    }
}

public static partial class MacroKernelHolder
{
    [GpuKernel]
    public static void Kernel(int[] r)
    {
        using var d = new DisposableWithMacro { Log = r, X = 10 };
    }
}
