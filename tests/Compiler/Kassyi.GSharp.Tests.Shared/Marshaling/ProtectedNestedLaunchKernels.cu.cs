// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Marshaling;

// A protected nested kernel class and a protected kernel method taking a protected struct: generated launchers and
// AotContexts keep protected-domain accessibility so derived classes can call them (#1845).
public partial class ProtectedNestedLaunchBase
{
    protected struct ProtectedPayload
    {
        public int[] Values;
        public int Factor;
    }

    protected static partial class Kernels
    {
        [GpuKernel]
        public static void ScaleKernel(ProtectedPayload payload)
        {
            int tid = ThreadIndex.X;
            payload.Values[tid] *= payload.Factor;
        }
    }

    [GpuKernel]
    protected static void DirectScaleKernel(ProtectedPayload payload)
    {
        int tid = ThreadIndex.X;
        payload.Values[tid] += payload.Factor;
    }
}
