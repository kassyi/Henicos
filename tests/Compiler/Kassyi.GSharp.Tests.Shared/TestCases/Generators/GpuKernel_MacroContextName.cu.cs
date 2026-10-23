using Kassyi.GSharp.Core;

namespace TestNamespace.MacroContextName;

// The kernel names its context 'bigCtx'. [GpuOperatorMacro] has to pass that variable to the macro target.
public struct MacroCtxState
{
    public int State;
}

public static partial class MacroCtxOps
{
    [GpuDeviceFunction]
    public static void MacroCtxAdd(ref MacroCtxState ctx, uint[] result, uint[] left, uint[] right)
    {
        result[0] = left[0] + right[0] + (uint)ctx.State;
    }
}

public readonly ref struct MacroCtxRef
{
    public readonly uint[] Data;

    public MacroCtxRef(uint[] data) => Data = data;

    [GpuOperatorMacro(typeof(MacroCtxOps), nameof(MacroCtxOps.MacroCtxAdd))]
    public static MacroCtxRef operator +(MacroCtxRef a, MacroCtxRef b) => throw new System.Exception();

    public static implicit operator MacroCtxRef(uint[] data) => new MacroCtxRef(data);
    public static implicit operator uint[](MacroCtxRef data) => data.Data;
}

public static partial class MacroCtxKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static void MacroCtxKernel(MacroCtxState bigCtx, uint[] dA, uint[] dB, uint[] dResult)
    {
        MacroCtxRef a = dA;
        MacroCtxRef b = dB;
        MacroCtxRef c = dResult;

        c = a + b;
    }
}
