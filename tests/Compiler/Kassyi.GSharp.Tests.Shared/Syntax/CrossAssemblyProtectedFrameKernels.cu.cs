using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.DeviceLibrary;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

// A kernel class that derives from LibFrameBase in another assembly and calls its protected suspending device function,
// whose managed frame is private to LibFrameBase and is run through its protected entries (#1991)
public partial class CrossAssemblyProtectedFrameKernels : LibFrameBase
{
    [GpuKernel]
    public static void ProtectedFrameKernel(int[] buffer, int[] result)
    {
        int tid = ThreadIndex.X;
        result[tid] = KeepProtected(buffer, tid);
    }
}
