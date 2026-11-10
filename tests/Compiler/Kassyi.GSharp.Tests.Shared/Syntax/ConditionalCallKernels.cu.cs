using System.Diagnostics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

// C# removes a call of a [Conditional] method when none of its symbols is defined in the file of the call, its
// arguments included. CONDITIONAL_CALL_NEVER_DEFINED is defined nowhere; NETCOREAPP is defined by the target framework
// of both test projects. Debug.Assert is not used: the build configuration decides whether DEBUG is defined
public static partial class ConditionalCallKernels
{
    private const uint FullMask = 0xFFFFFFFFu;

    [GpuDeviceFunction]
    [Conditional("CONDITIONAL_CALL_NEVER_DEFINED")]
    public static void Overwrite(int[] data, int i)
    {
        data[i] = 99;
    }

    [GpuDeviceFunction]
    [Conditional("CONDITIONAL_CALL_NEVER_DEFINED")]
    public static void Trace(int value)
    {
    }

    // Not a device function: C# removes every call, so nothing is left to compile
    [Conditional("CONDITIONAL_CALL_NEVER_DEFINED")]
    public static void HostTrace(int[] data, int i)
    {
        data[i] = 77;
    }

    [GpuDeviceFunction]
    [Conditional("NETCOREAPP")]
    public static void AddWhenDefined(int[] data, int i)
    {
        data[i] += 1000;
    }

    [GpuDeviceFunction]
    [Conditional("CONDITIONAL_CALL_NEVER_DEFINED")]
    [Conditional("NETCOREAPP")]
    public static void AddWhenEitherDefined(int[] data, int i)
    {
        data[i] += 2000;
    }

    [GpuDeviceFunction]
    public static int Bump(int[] data, int i)
    {
        data[i] += 100;
        return data[i];
    }

    [GpuDeviceFunction]
    public static void AddOne(int[] data, int i)
    {
        data[i] += 1;
        Overwrite(data, i);
    }

    // Reaches a block barrier, so a call of it splits the state machine of its caller on the CPU
    [GpuDeviceFunction]
    public static int BumpAfterBarrier(int[] data, int i)
    {
        Gpu.SyncThreads();
        data[i] += 100;
        return data[i];
    }

    [GpuKernel]
    public static void RemovedInKernel(int[] data)
    {
        int i = ThreadIndex.X;
        data[i] += 1;
        Overwrite(data, i);
        HostTrace(data, i);
    }

    [GpuKernel]
    public static void RemovedInDeviceFunction(int[] data)
    {
        int i = ThreadIndex.X;
        AddOne(data, i);
    }

    // Neither x++ nor Bump runs: data[i] becomes 11 times its value
    [GpuKernel]
    public static void RemovedArguments(int[] data)
    {
        int i = ThreadIndex.X;
        int x = data[i];
        Trace(x++);
        Trace(Bump(data, i));
        data[i] += x * 10;
    }

    // The removed call is the body of an expression-bodied lambda and of a local function, and an incrementor of a for
    // loop: two loop iterations and +10, no Bump
    [GpuKernel]
    public static void RemovedInExpressionBodies(int[] data)
    {
        int i = ThreadIndex.X;
        void Forget(int value) => Trace(Bump(data, i) + value);

        Gpu.ForUnroll(0, 2, k => Trace(Bump(data, i) + k));
        Forget(3);
        for (int k = 0; k < 2; k++, Trace(Bump(data, i)))
        {
            data[i] += 1;
        }

        data[i] += 10;
    }

    // The argument suspends (a block barrier) or is a warp shuffle: neither runs, nor splits the kernel on the CPU
    [GpuKernel]
    public static void RemovedSplitArguments(int[] data)
    {
        int i = ThreadIndex.X;
        Trace(BumpAfterBarrier(data, i));
        Trace(Warp.ShuffleXorSync(FullMask, Bump(data, i), 1));
        data[i] += 1;
    }

    // Removed calls as the embedded statements of an if, an else and a loop: +1
    [GpuKernel]
    public static void RemovedAsEmbeddedStatements(int[] data)
    {
        int i = ThreadIndex.X;
        if (i == 0) Overwrite(data, i); else Overwrite(data, i);
        while (i < 0) Trace(Bump(data, i));
        data[i] += 1;
    }

    // One symbol of the call is defined, so C# keeps it: +1000 and +2000. The generator snapshot is compiled without
    // preprocessor symbols and shows both calls removed
    [GpuKernel]
    public static void KeptWhenDefined(int[] data)
    {
        int i = ThreadIndex.X;
        data[i] += 1;
        AddWhenDefined(data, i);
        AddWhenEitherDefined(data, i);
    }
}
