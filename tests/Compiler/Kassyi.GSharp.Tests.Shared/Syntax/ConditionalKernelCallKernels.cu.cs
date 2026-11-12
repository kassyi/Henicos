using System.Diagnostics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

// A [GpuKernel] method can carry [Conditional] too: C# removes a call of it from a kernel when the symbol is not
// defined, as it does for a device function. CONDITIONAL_CALL_NEVER_DEFINED is defined nowhere
public static partial class ConditionalKernelCallKernels
{
    [GpuKernel]
    [Conditional("CONDITIONAL_CALL_NEVER_DEFINED")]
    public static void RemovedKernel(int[] data)
    {
        data[1] = ThreadIndex.X + 100;
    }

    [GpuKernel]
    public static void CallsRemovedKernel(int[] data)
    {
        data[0] = 42;
        RemovedKernel(data);
    }
}
