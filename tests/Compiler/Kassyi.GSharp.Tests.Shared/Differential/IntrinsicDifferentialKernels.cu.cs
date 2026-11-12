using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for the <c>Gpu</c> intrinsics of <c>ptx_intrinsics.yml</c> that the CPU dialect implements in
/// its prelude and the managed path in <c>ManagedIntrinsics</c> (#717), and for <c>Gpu.SmId</c> and the clocks (#469).
/// </summary>
public static partial class IntrinsicDifferentialKernels
{
    /// <summary>The columns of <see cref="Integer32IntrinsicsKernel"/>.</summary>
    public static readonly string[] Integer32Columns =
    [
        "CountLeadingZeros(a)",
        "BytePermute(a,b,s)",
        "AbsDiff(a,b)",
        "AbsDiff((int)a,(int)b)",
        "MultiplyHigh((int)a,(int)b)",
    ];

    /// <summary>The columns of <see cref="Integer64IntrinsicsKernel"/>.</summary>
    public static readonly string[] Integer64Columns =
    [
        "MultiplyWide(a,b)",
        "MultiplyHigh(c,d)",
        "MultiplyHigh((long)c,(long)d)",
        "CountLeadingZeros(c)",
        "MultiplyAddWide(a,b,c)",
        "MultiplyAddWide((int)a,(int)b,(long)c)",
        "MultiplyAddWideWithCarry(a,b,c,ref carry)",
        "carry-out",
    ];

    /// <summary>The columns of <see cref="CacheHintAndFenceKernel"/>.</summary>
    public static readonly string[] CacheHintColumns = ["LoadCachedGlobal", "StoreCachedGlobal"];

    /// <summary>The columns of <see cref="ClockKernel"/>.</summary>
    public static readonly string[] ClockColumns = ["Clock64 first", "Clock64 second", "Clock first", "Clock second"];

    /// <summary>The columns of <see cref="Bits32Kernel"/>.</summary>
    public static readonly string[] Bits32Columns =
    [
        "PopCount(a)",
        "BitReverse(a)",
        "CountTrailingZeros(a)",
        "FunnelShiftLeft(a,b,c)",
        "FunnelShiftRight(a,b,c)",
        "BitFieldExtract(a,pos,len)",
        "BitFieldInsert(a,b,pos,len)",
        "Lop3(a,b,c,0x96)",
        "Lop3(a,b,c,0xE8)",
        "Lop3(a,b,c,0xCA)",
        "Lop3(a,b,c,0x00)",
        "Lop3(a,b,c,0xFF)",
        "Sad4(a,b,c)",
        "DotProduct4((int)a,(int)b,(int)c)",
        "DotProduct4(a,b,c)",
    ];

    /// <summary>The columns of <see cref="Bits64Kernel"/>.</summary>
    public static readonly string[] Bits64Columns =
    [
        "PopCount(a)",
        "BitReverse(a)",
        "BitReverse64(a)",
        "CountTrailingZeros(a)",
        "BitFieldExtract(a,pos,len)",
        "BitFieldInsert(a,b,pos,len)",
    ];

    /// <summary>The columns of <see cref="CarryChainKernel"/>.</summary>
    public static readonly string[] CarryColumns =
    [
        "AddWithCarry(a,b,ref carry)",
        "carry-out",
        "SubWithBorrow(a,b,ref borrow)",
        "borrow-out",
    ];

    [GpuKernel]
    public static void Integer32IntrinsicsKernel(uint[] a, uint[] b, uint[] s, uint[] output, int n)
    {
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i >= n)
        {
            return;
        }

        int o = i * 5;
        output[o] = Gpu.CountLeadingZeros(a[i]);
        output[o + 1] = Gpu.BytePermute(a[i], b[i], s[i]);
        output[o + 2] = Gpu.AbsDiff(a[i], b[i]);
        output[o + 3] = (uint)Gpu.AbsDiff((int)a[i], (int)b[i]);
        output[o + 4] = (uint)Gpu.MultiplyHigh((int)a[i], (int)b[i]);
    }

    [GpuKernel]
    public static void Integer64IntrinsicsKernel(uint[] a, uint[] b, ulong[] c, ulong[] d, ulong[] output, int n)
    {
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i >= n)
        {
            return;
        }

        int o = i * 8;
        output[o] = Gpu.MultiplyWide(a[i], b[i]);
        output[o + 1] = Gpu.MultiplyHigh(c[i], d[i]);
        output[o + 2] = (ulong)Gpu.MultiplyHigh((long)c[i], (long)d[i]);
        output[o + 3] = Gpu.CountLeadingZeros(c[i]);
        output[o + 4] = Gpu.MultiplyAddWide(a[i], b[i], c[i]);
        output[o + 5] = (ulong)Gpu.MultiplyAddWide((int)a[i], (int)b[i], (long)c[i]);
        ulong carry = d[i] & 1ul;
        output[o + 6] = Gpu.MultiplyAddWideWithCarry(a[i], b[i], c[i], ref carry);
        output[o + 7] = carry;
    }

    // Each thread loads its own element, fences, and stores a value derived from it: the hints and fences change no
    // value
    [GpuKernel]
    public static void CacheHintAndFenceKernel(int[] source, int[] output, int n)
    {
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i >= n)
        {
            return;
        }

        int loaded = Gpu.LoadCachedGlobal(ref source[i]);
        Gpu.ThreadFenceBlock();
        Gpu.ThreadFence();
        Gpu.ThreadFenceSystem();
        output[i * 2] = loaded;
        Gpu.StoreCachedGlobal(ref output[i * 2 + 1], loaded * 3 + 1);
    }

    [GpuKernel]
    public static void SmIdKernel(int[] output, int n)
    {
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i >= n)
        {
            return;
        }

        output[i] = Gpu.SmId();
    }

    // Two readings of each clock around work that depends on the input, so that the second reading cannot be moved
    // before it
    [GpuKernel]
    public static void ClockKernel(uint[] input, ulong[] output, int n)
    {
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i >= n)
        {
            return;
        }

        ulong first64 = Gpu.Clock64();
        uint first = Gpu.Clock();
        uint acc = input[i];
        for (int k = 0; k < 64; k++)
        {
            acc = acc * 1664525u + 1013904223u;
        }

        uint second = Gpu.Clock();
        ulong second64 = Gpu.Clock64();
        int o = i * 4;
        output[o] = first64;
        output[o + 1] = second64 + (acc & 0ul);
        output[o + 2] = first;
        output[o + 3] = second;
    }

    [GpuKernel]
    public static void Bits32Kernel(uint[] a, uint[] b, uint[] c, int[] pos, int[] len, uint[] output, int n)
    {
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i >= n)
        {
            return;
        }

        int o = i * 15;
        output[o] = Gpu.PopCount(a[i]);
        output[o + 1] = Gpu.BitReverse(a[i]);
        output[o + 2] = Gpu.CountTrailingZeros(a[i]);
        output[o + 3] = Gpu.FunnelShiftLeft(a[i], b[i], c[i]);
        output[o + 4] = Gpu.FunnelShiftRight(a[i], b[i], c[i]);
        output[o + 5] = Gpu.BitFieldExtract(a[i], pos[i], len[i]);
        output[o + 6] = Gpu.BitFieldInsert(a[i], b[i], pos[i], len[i]);
        output[o + 7] = Gpu.Lop3(a[i], b[i], c[i], 0x96);
        output[o + 8] = Gpu.Lop3(a[i], b[i], c[i], 0xE8);
        output[o + 9] = Gpu.Lop3(a[i], b[i], c[i], 0xCA);
        output[o + 10] = Gpu.Lop3(a[i], b[i], c[i], 0x00);
        output[o + 11] = Gpu.Lop3(a[i], b[i], c[i], 0xFF);
        output[o + 12] = Gpu.Sad4(a[i], b[i], c[i]);
        output[o + 13] = (uint)Gpu.DotProduct4((int)a[i], (int)b[i], (int)c[i]);
        output[o + 14] = Gpu.DotProduct4(a[i], b[i], c[i]);
    }

    [GpuKernel]
    public static void Bits64Kernel(ulong[] a, ulong[] b, int[] pos, int[] len, ulong[] output, int n)
    {
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i >= n)
        {
            return;
        }

        int o = i * 6;
        output[o] = Gpu.PopCount(a[i]);
        output[o + 1] = Gpu.BitReverse(a[i]);
        output[o + 2] = Gpu.BitReverse64(a[i]);
        output[o + 3] = Gpu.CountTrailingZeros(a[i]);
        output[o + 4] = Gpu.BitFieldExtract(a[i], pos[i], len[i]);
        output[o + 5] = Gpu.BitFieldInsert(a[i], b[i], pos[i], len[i]);
    }

    [GpuKernel]
    public static void CarryChainKernel(uint[] a, uint[] b, ulong[] carryIn, long[] borrowIn, ulong[] output, int n)
    {
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i >= n)
        {
            return;
        }

        int o = i * 4;
        ulong carry = carryIn[i];
        output[o] = Gpu.AddWithCarry(a[i], b[i], ref carry);
        output[o + 1] = carry;
        long borrow = borrowIn[i];
        output[o + 2] = Gpu.SubWithBorrow(a[i], b[i], ref borrow);
        output[o + 3] = (ulong)borrow;
    }

    // Each block copies its 32 elements into shared memory in two groups and reads them back reversed: the first half
    // after waiting for all but the last group, the second half after waiting for all of them. A sleep between the
    // copy and the wait changes nothing
    [GpuKernel]
    public static void CopyAsyncAndSleepKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        int blockStart = BlockIndex.X * 32;
        var smem = new SharedMemory<int>(32);
        if (tid < 16)
        {
            Gpu.CopyAsync(ref smem[tid], in input[blockStart + tid]);
        }

        Gpu.CopyAsyncCommitGroup();
        if (tid < 16)
        {
            Gpu.CopyAsync(ref smem[tid + 16], in input[blockStart + tid + 16]);
        }

        Gpu.CopyAsyncCommitGroup();
        Gpu.NanoSleep(100u);
        Gpu.CopyAsyncWaitGroup(1);
        Gpu.SyncThreads();
        if (tid < 16)
        {
            output[blockStart + tid] = smem[15 - tid];
        }

        Gpu.CopyAsyncWaitAll();
        Gpu.SyncThreads();
        if (tid >= 16)
        {
            output[blockStart + tid] = smem[47 - tid];
        }
    }

    // Each thread allocates a block of its size, fills it, sums it up and frees it; a size the heap cannot give
    // returns null
    [GpuKernel]
    public static unsafe void MallocKernel(int[] sizes, int[] output, int n)
    {
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i >= n)
        {
            return;
        }

        int size = sizes[i];
#pragma warning disable GSH0029 // The block is freed below
        byte* block = (byte*)Gpu.Unsafe.Malloc(size);
#pragma warning restore GSH0029
        if (block == null)
        {
            output[i] = -1;
            return;
        }

        for (int k = 0; k < size; k++)
        {
            block[k] = (byte)(i + k);
        }

        int sum = 0;
        for (int k = 0; k < size; k++)
        {
            sum += block[k];
        }

        Gpu.Unsafe.Free(block);
        output[i] = sum;
    }
}
