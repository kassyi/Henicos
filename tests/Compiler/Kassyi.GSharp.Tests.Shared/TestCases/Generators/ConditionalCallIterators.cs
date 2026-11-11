using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace ConditionalCallCase;

// CONDITIONAL_FILE_ON is not defined in this file, so C# removes the call of Marker here
public static partial class CallIterators
{
    [GpuDeviceFunction]
    public static IEnumerable<int> Values(int[] output)
    {
        yield return 1;
        CallOps.Marker(output);
        yield return 2;
    }
}

// A class of this file only: its generated file defines nothing, and the call of Kept is removed
public static partial class PlainKernels
{
    [GpuKernel]
    public static void PlainKernel(int[] output)
    {
        output[0] += 1;
        CallOps.Kept(output);
    }
}
