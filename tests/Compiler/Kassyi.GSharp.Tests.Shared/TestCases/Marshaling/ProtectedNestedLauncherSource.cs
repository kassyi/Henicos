using Kassyi.GSharp.Core;

namespace Access;

public partial class ProtectedLauncherBase
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

    [GpuDeviceFunction]
    public static int SumProtectedPayload(int[] values, int tid)
    {
        ProtectedPayload payload = default;
        payload.Values = values;
        payload.Factor = tid + 1;
        Gpu.SyncThreads();
        return payload.Values[tid] * payload.Factor;
    }
}

public partial class ProtectedLauncherDerived : ProtectedLauncherBase
{
    [GpuKernel]
    public static void CallProtectedDeviceKernel(int[] values, int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = SumProtectedPayload(values, tid);
    }

    public static void CallBothLaunchers(int[] values)
    {
        var payload = new ProtectedPayload { Values = values, Factor = 2 };
        Kernels.LaunchScaleKernel(payload, blockDimX: (uint)values.Length);
        LaunchDirectScaleKernel(payload, blockDimX: (uint)values.Length);
    }
}

public static partial class UnrelatedSuspendingCaller
{
    [GpuKernel]
    public static void CallPublicProtectedFrameKernel(int[] values, int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = ProtectedLauncherBase.SumProtectedPayload(values, tid);
    }
}
