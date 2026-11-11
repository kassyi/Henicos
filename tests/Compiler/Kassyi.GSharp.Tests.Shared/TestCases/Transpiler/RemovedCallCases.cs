#define FILE_ON
#undef UNDEFINED_HERE
#if NEVER_ACTIVE
#define INACTIVE_DEFINE
#endif
using System.Diagnostics;
using Kassyi.GSharp.Core;

namespace RemovedCallCase;

// Parsed with the preprocessor symbols OPTION_ON and UNDEFINED_HERE (RemovedCallsTests)
public static partial class RemovedCallOps
{
    [Conditional("NEVER_DEFINED")]
    public static void Never()
    {
    }

    [Conditional("OPTION_ON")]
    public static void OptionOn()
    {
    }

    [Conditional("UNDEFINED_HERE")]
    public static void UndefinedHere()
    {
    }

    [Conditional("FILE_ON")]
    public static void FileOn()
    {
    }

    [Conditional("INACTIVE_DEFINE")]
    public static void InactiveDefine()
    {
    }

    [Conditional("NEVER_DEFINED")]
    [Conditional("OPTION_ON")]
    public static void Either()
    {
    }

    static partial void NoImplementation();

    static partial void WithImplementation();

    static partial void WithImplementation()
    {
    }

    public static void Plain()
    {
    }

    [GpuDeviceFunction]
    [Conditional("NEVER_DEFINED")]
    public static void Trace(int value)
    {
    }

    public static void CallsNever() => Never();

    public static void CallsOptionOn() => OptionOn();

    public static void CallsUndefinedHere() => UndefinedHere();

    public static void CallsFileOn() => FileOn();

    public static void CallsInactiveDefine() => InactiveDefine();

    public static void CallsEither() => Either();

    public static void CallsNoImplementation() => NoImplementation();

    public static void CallsWithImplementation() => WithImplementation();

    public static void CallsPlain() => Plain();

    // DEBUG is not among the symbols
    public static void CallsDebugAssert() => Debug.Assert(true);

    [GpuDeviceFunction]
    public static int BumpAfterBarrier(int[] data, int i)
    {
        Gpu.SyncThreads();
        data[i] += 100;
        return data[i];
    }

    // Suspends only through the argument of a removed call, so it does not suspend
    [GpuDeviceFunction]
    public static void CallsOnlyRemovedBarrier(int[] data, int i) => Trace(BumpAfterBarrier(data, i));

    // A warp shuffle splits the state machine; here it is only the argument of a removed call
    [GpuDeviceFunction]
    public static void ShufflesOnlyInRemovedCall(int value) => Trace(Warp.ShuffleXorSync(0xFFFFFFFFu, value, 1));

    [GpuKernel]
    public static void SplitArgumentsKernel(int[] data)
    {
        int i = ThreadIndex.X;
        void Local(int value) => Trace(BumpAfterBarrier(data, value));

        Trace(BumpAfterBarrier(data, i));
        Trace(Warp.ShuffleXorSync(0xFFFFFFFFu, data[i], 1));
        for (int k = 0; k < 2; k++, Trace(BumpAfterBarrier(data, i)))
        {
            data[i] += 1;
        }

        Local(i);
    }
}
