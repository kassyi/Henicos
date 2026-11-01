using Kassyi.GSharp.Core;

namespace TestNamespace.HostWrapperNames;

public struct IntPtr
{
    public int Value;
}

public struct CudaDriverApi
{
    public int Value;
}

public struct CudaException
{
    public int Value;
}

public struct Unsafe
{
    public int Value;
}

public struct LayoutGuard
{
    public int Value;
}

public struct MemoryMarshal
{
    public int Value;
}

public struct HostWrapperCollisionItem
{
    public int[] Data;
    public bool Flag;
    public long Value;
}

public struct HostWrapperSimpleConvert
{
    public bool Flag;
    public int Number;
}

// Kernel parameters and sibling types intentionally use names that would collide with
// synthesized locals, helper members, and unqualified BCL/runtime references in the host wrapper (#406).
public static partial class HostWrapperNameCollisionKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static HostWrapperSimpleConvert SingleCollisionKernel(
        HostWrapperCollisionItem[] a,
        HostWrapperCollisionItem b,
        HostWrapperSimpleConvert[] c,
        ref int d,
        out int e,
        int[] f,
        int d_a,
        int proxy_a,
        int native_a,
        int p_a,
        int p_a_temp,
        int ptr_a,
        int a_size,
        int a_shadow,
        int a_native,
        int a_sharedShadow,
        int a_allocatedPtrs,
        int a_index,
        int a_host,
        int a_element,
        int a_source,
        int a_copy,
        int a_copyIndex,
        int p_b,
        int p_b_temp,
        int native_b,
        int c_size,
        int c_source,
        int c_copy,
        int c_copyIndex,
        int p_c,
        int d_d,
        int p_d,
        int ptr_d,
        int d_f,
        int f_size,
        int p_f,
        int kernelParams,
        int hfunc,
        int hModule,
        int archSuffix,
        int dllName,
        int dllPath,
        int handle,
        int i,
        int threadAtBlockBarrier,
        int threadAtGridSync,
        int anyGridActive,
        int firstFinishedBlock,
        int anyBlockAtGridSync,
        int blockAtGridSync,
        int anyBlockActive,
        int TotalThreads,
        int System,
        int IntPtr,
        int CudaDriverApi,
        int CudaException,
        int AotContexts,
        int s_hfunc_SingleCollisionKernel,
        int s_layoutChecked_SingleCollisionKernel,
        int SingleCollisionKernel_Delegate,
        int WarmupFunctionPointerSingleCollisionKernel,
        int LaunchSingleCollisionKernelPtr)
    {
        e = d + d_a + proxy_a + native_a + p_a + p_a_temp + ptr_a + a_size + a_shadow + a_native +
            a_sharedShadow + a_allocatedPtrs + a_index + a_host + a_element + a_source + a_copy + a_copyIndex +
            p_b + p_b_temp + native_b + c_size + c_source + c_copy + c_copyIndex + p_c +
            d_d + p_d + ptr_d + d_f + f_size + p_f +
            kernelParams + hfunc + hModule + archSuffix + dllName + dllPath + handle + i +
            threadAtBlockBarrier + threadAtGridSync + anyGridActive + firstFinishedBlock +
            anyBlockAtGridSync + blockAtGridSync + anyBlockActive + TotalThreads +
            System + IntPtr + CudaDriverApi + CudaException + AotContexts +
            s_hfunc_SingleCollisionKernel + s_layoutChecked_SingleCollisionKernel +
            SingleCollisionKernel_Delegate + WarmupFunctionPointerSingleCollisionKernel +
            LaunchSingleCollisionKernelPtr + b.Data[0] + a[0].Data[0] + c[0].Number + f[0];
        d = e;
        HostWrapperSimpleConvert result = default;
        result.Flag = b.Flag;
        result.Number = e;
        return result;
    }

    [GpuKernel(Cooperative = true)]
    [SkipSnapshotConsistency("Generator case")]
    public static void CooperativeCollisionKernel(
        int[] a,
        int threadAtBlockBarrier,
        int threadAtGridSync,
        int anyGridActive,
        int firstFinishedBlock,
        int anyBlockAtGridSync,
        int blockAtGridSync,
        int anyBlockActive,
        int WarmupFunctionPointerCooperativeCollisionKernel_o0,
        int LaunchCooperativeCollisionKernelPtr)
    {
        Gpu.SyncThreads();
        Gpu.SyncGrid();
        int idx = ThreadIndex.X;
        if (idx == 0)
        {
            a[0] = threadAtBlockBarrier + threadAtGridSync + anyGridActive + firstFinishedBlock +
                   anyBlockAtGridSync + blockAtGridSync + anyBlockActive +
                   WarmupFunctionPointerCooperativeCollisionKernel_o0 + LaunchCooperativeCollisionKernelPtr;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static void CooperativeCollisionKernel(
        float[] a,
        int LaunchCooperativeCollisionKernelPtr)
    {
        int idx = ThreadIndex.X;
        if (idx == 0)
        {
            a[0] = LaunchCooperativeCollisionKernelPtr;
        }
    }
}
