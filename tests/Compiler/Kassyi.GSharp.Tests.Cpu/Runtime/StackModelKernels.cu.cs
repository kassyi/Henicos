#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Cpu.Runtime;

public struct StackModelRefOutBuffers
{
    [GpuReadOnly]
    public int[] Input;
    public int[] Output;
}

[SkipSnapshotConsistency("Verified by StackModelTests under both managed barrier models")]
public static partial class StackModelKernels
{
    [GpuKernel]
    public static void ReverseKernel(int[] input, int[] output, int n)
    {
        var s = new SharedMemory<int>(64);
        int tid = ThreadIndex.X;
        s[tid] = input[tid];
        Gpu.SyncThreads();
        output[tid] = s[n - 1 - tid];
    }

    [GpuKernel]
    public static void DeviceFunctionKernel(int[] input, int[] output, int n)
    {
        var s = new SharedMemory<int>(64);
        int tid = ThreadIndex.X;
        s[tid] = input[tid];
        Gpu.SyncThreads();
        output[tid] = WaitAndRead(s[n - 1 - tid], tid);
    }

    [GpuKernel]
    public static int ReturningKernel(int[] input, int n)
    {
        var s = new SharedMemory<int>(64);
        int tid = ThreadIndex.X;
        s[tid] = input[tid];
        Gpu.SyncThreads();
        return s[n - 1 - tid] + tid;
    }

    [GpuKernel]
    public static void MutatedValueParameterKernel(int[] output, int seed)
    {
        int tid = ThreadIndex.X;
        seed += tid + 1;
        Gpu.SyncThreads();
        output[tid] = seed * 3;
    }

    [GpuKernel]
    public static void LoopBarrierKernel(int[] input, int[] output, int rounds, int n)
    {
        var s = new SharedMemory<int>(64);
        int tid = ThreadIndex.X;
        int acc = input[tid];
        for (int r = 0; r < rounds; r++)
        {
            s[tid] = acc;
            Gpu.SyncThreads();
            int partner = s[(tid + 1) % n];
            Gpu.SyncThreads();
            acc += partner;
        }

        output[tid] = acc;
    }

    [GpuKernel]
    public static void RefOutKernel(StackModelRefOutBuffers buffers, ref int refTotal, out int outValue, int n)
    {
        int[] input = buffers.Input;
        int[] output = buffers.Output;
        outValue = 0;
        var s = new SharedMemory<int>(64);
        int tid = ThreadIndex.X;
        s[tid] = input[tid];
        Gpu.SyncThreads();
        int localAcc = tid;
        ExchangeViaRefOut(ref localAcc, out int mirrored, s[n - 1 - tid]);
        output[tid] = localAcc + mirrored;
        if (tid == 0)
        {
            refTotal += s[n - 1];
            outValue = s[0] + s[n - 1];
        }
    }

    [GpuKernel]
    public static void NestedDeviceFunctionKernel(int[] input, int[] output, int n)
    {
        var s = new SharedMemory<int>(64);
        int tid = ThreadIndex.X;
        s[tid] = input[tid];
        Gpu.SyncThreads();
        output[tid] = OuterWait(s[n - 1 - tid], tid);
    }

    [GpuKernel]
    public static void FaultAfterBarrierKernel(int[] input, int[] output, int n)
    {
        var s = new SharedMemory<int>(64);
        int tid = ThreadIndex.X;
        s[tid] = input[tid];
        Gpu.SyncThreads();
        int value = s[n - 1 - tid];
        if (tid == 3)
        {
            value = input[n + 10];
        }

        Gpu.SyncThreads();
        output[tid] = value + 1;
    }

    [GpuKernel]
    public static void WarpAndBarrierKernel(int[] input, int[] output)
    {
        var s = new SharedMemory<int>(64);
        int tid = ThreadIndex.X;
        s[tid] = input[tid];
        Gpu.SyncThreads();
        output[tid] = Warp.ShuffleXorSync(0xFFFFFFFFu, s[tid], 1);
    }

    // The even lanes ask for the active mask inside a branch, every lane after it, where the lanes meet again
    [GpuKernel]
    public static void ActiveMaskKernel(uint[] inBranch, uint[] afterBranch)
    {
        int tid = ThreadIndex.X;
        uint mask = 0u;
        if (tid % 2 == 0)
        {
            mask = Warp.ActiveMask();
        }

        inBranch[tid] = mask;
        afterBranch[tid] = Warp.ActiveMask();
    }

    [GpuKernel(Cooperative = true)]
    public static void CooperativeBarrierKernel(int[] input, int[] output, int n)
    {
        var s = new SharedMemory<int>(64);
        int tid = ThreadIndex.X;
        s[tid] = input[tid];
        Gpu.SyncThreads();
        Gpu.SyncGrid();
        output[tid] = s[n - 1 - tid];
    }

    [GpuKernel]
    public static void NoBarrierKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = input[tid] * 2 + 1;
    }

    [GpuKernel]
    public static void LocalFunctionBarrierKernel(int[] input, int[] scratch, int[] output, int n)
    {
        int tid = ThreadIndex.X;
        scratch[tid] = input[tid] * 3;
        output[tid] = ReadMirrored();

        int ReadMirrored()
        {
            Gpu.SyncThreads();
            return scratch[n - 1 - tid];
        }
    }

    [GpuKernel]
    public static void UnrolledBarrierKernel(int[] input, int[] scratch, int[] output, int n)
    {
        int tid = ThreadIndex.X;
        int acc = input[tid];
        Gpu.ForUnroll(0, 2, r =>
        {
            scratch[tid] = acc;
            Gpu.SyncThreads();
            acc += scratch[(tid + 1) % n];
            Gpu.SyncThreads();
        });
        output[tid] = acc;
    }

    [GpuKernel]
    public static void LargeLocalAcrossBarrierKernel(int[] input, int[] output)
    {
        var s = new SharedMemory<int>(32);
        int tid = ThreadIndex.X;
        int gid = BlockIndex.X * BlockDimension.X + tid;
        Span<byte> buf = stackalloc byte[256 * 1024];
        buf[0] = (byte)(input[gid] & 0xFF);
        buf[256 * 1024 - 1] = (byte)((input[gid] >> 8) & 0xFF);
        s[tid] = tid;
        Gpu.SyncThreads();
        int mirrored = s[BlockDimension.X - 1 - tid];
        output[gid] = (buf[0] | (buf[256 * 1024 - 1] << 8)) + mirrored;
    }

    [GpuDeviceFunction]
    private static int WaitAndRead(int value, int tid)
    {
        Gpu.SyncThreads();
        return value + tid * 0;
    }

    [GpuDeviceFunction]
    private static void ExchangeViaRefOut(ref int acc, out int mirrored, int value)
    {
        Gpu.SyncThreads();
        acc += value;
        mirrored = value * 2;
    }

    [GpuDeviceFunction]
    private static int OuterWait(int value, int tid)
    {
        Gpu.SyncThreads();
        return InnerWait(value + 1, tid);
    }

    [GpuDeviceFunction]
    private static int InnerWait(int value, int tid)
    {
        Gpu.SyncThreads();
        return value + tid;
    }
}
