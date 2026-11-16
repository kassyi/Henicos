using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

// 64 bytes
public struct Block64
{
    public long A;
    public long B;
    public long C;
    public long D;
    public long E;
    public long F;
    public long G;
    public long H;
}

// 512 bytes
public struct Block512
{
    public Block64 A;
    public Block64 B;
    public Block64 C;
    public Block64 D;
    public Block64 E;
    public Block64 F;
    public Block64 G;
    public Block64 H;
}

// 4096 bytes
public struct Block4096
{
    public Block512 A;
    public Block512 B;
    public Block512 C;
    public Block512 D;
    public Block512 E;
    public Block512 F;
    public Block512 G;
    public Block512 H;
}

// 48 bytes
public struct Size48Struct
{
    public long A;
    public long B;
    public long C;
    public long D;
    public long E;
    public long F;
}

// Sizes around the CUDA limit of 32,764 bytes; every CUDA kernel also takes the 4-byte launch epoch last
[System.Runtime.CompilerServices.InlineArray(32760)]
public struct Bytes32760
{
    private byte _element;
}

[System.Runtime.CompilerServices.InlineArray(32761)]
public struct Bytes32761
{
    private byte _element;
}

// 32,752 bytes, 8-byte aligned
[System.Runtime.CompilerServices.InlineArray(4094)]
public struct Longs32752
{
    private long _element;
}

// 8,192 bytes: above the old 4,096-byte check, which nvcc accepts
[System.Runtime.CompilerServices.InlineArray(2048)]
public struct Ints8192
{
    private int _element;
}

// 40,960 bytes, which nvcc rejects
[System.Runtime.CompilerServices.InlineArray(10240)]
public struct Ints40960
{
    private int _element;
}

public static partial class StructSizeDiagnosticKernels
{
    [GpuKernel]
    public static void AtLimitKernel(Bytes32760 s)
    {
        int x = s[0];
        _ = x;
    }

    [GpuKernel]
    public static void OverLimitKernel(Bytes32761 s)
    {
        int x = s[0];
        _ = x;
    }

    [GpuKernel]
    public static void ArrayAndBlockKernel(int[] data, Bytes32760 s)
    {
        data[0] = s[0];
    }

    [GpuKernel]
    public static void PaddingKernel(byte flag, Longs32752 s, byte tail)
    {
        int x = flag + tail + (int)s[0];
        _ = x;
    }

    [GpuKernel]
    public static int ResultKernel(Longs32752 s)
    {
        return (int)s[0];
    }

    [GpuKernel]
    public static void MediumKernel(int[] output, Ints8192 block)
    {
        output[0] = block[2047];
    }

    [GpuKernel]
    public static void LargeKernel(int[] output, Ints40960 block)
    {
        output[0] = block[10239];
    }

    [GpuKernel]
    public static void SmallStructKernel(int[] data, Size48Struct s)
    {
        data[0] = (int)s.A;
    }

    [GpuDeviceFunction]
    public static int ReadBlock(Block4096 s)
    {
        return (int)s.A.A.A;
    }

    [GpuKernel]
    public static void DeviceCallKernel(int[] data, Block4096[] items)
    {
        data[0] = ReadBlock(items[0]);
    }

    [GpuKernel]
    public static void DoubleSharedKernel(int[] data)
    {
        SharedMemory<double> cache = new SharedMemory<double>(5000);
        cache[0] = 1.0;
        data[0] = (int)cache[0];
    }

    [GpuKernel]
    public static void DoubleAtLimitKernel(int[] data)
    {
        SharedMemory<double> cache = new SharedMemory<double>(4096);
        cache[0] = 1.0;
        data[0] = (int)cache[0];
    }

    [GpuKernel]
    public static void ByteSharedKernel(int[] data)
    {
        SharedMemory<byte> cache = new SharedMemory<byte>(10000);
        cache[0] = 1;
        data[0] = cache[0];
    }

    // 300,000,000 doubles are 2,400,000,000 bytes, above int.MaxValue (#1280)
    [GpuKernel]
    public static void HugeSharedKernel(int[] data)
    {
        SharedMemory<double> cache = new SharedMemory<double>(300_000_000);
        cache[0] = 1.0;
        data[0] = (int)cache[0];
    }

    // 2^29 doubles are 2^32 bytes, which an int product wraps to exactly 0
    [GpuKernel]
    public static void WrapToZeroSharedKernel(int[] data)
    {
        SharedMemory<double> cache = new SharedMemory<double>(536_870_912);
        cache[0] = 1.0;
        data[0] = (int)cache[0];
    }

    // A kernel receives an `in` struct by value (the whole struct, not a pointer): 16 + 40960 + 4 = 40980 bytes
    [GpuKernel]
    public static void InLargeKernel(int[] output, in Ints40960 block)
    {
        output[0] = block[10239];
    }

    // An `in` struct that may share memory with the `ref` one is passed by value in one entry point and by address in
    // the other; the by-value one is 32760 + 8 + 4 = 32772 bytes (the by-address one 8 + 8 + 4 = 20)
    [GpuKernel]
    public static void InBesideRefLargeKernel(in Bytes32760 source, ref Bytes32760 target)
    {
        target[0] = source[0];
    }

    // `ref` is a pointer: 16 + 8 + 4 = 28 bytes
    [GpuKernel]
    public static void RefLargeKernel(int[] output, ref Ints40960 block)
    {
        output[0] = block[10239];
    }

    // 16 + 32744 = 32760; the grid aggregate scratch pointer (8) makes 32768 and the epoch 32772
    [GpuKernel(Cooperative = true)]
    public static void GridScratchKernel(long[] data, Longs32744 pad)
    {
        data[0] = data.AsStackEnumerable().AsParallel().Sum() + pad[0];
    }

    // The same parameters without the aggregate: 32760 + 4 (epoch) = 32764, exactly the limit
    [GpuKernel(Cooperative = true)]
    public static void NoGridScratchKernel(long[] data, Longs32744 pad)
    {
        data[0] = pad[0];
    }
}

// 32,744 bytes, 8-byte aligned: with the 16-byte array header 32,760, which leaves room for the epoch alone
[System.Runtime.CompilerServices.InlineArray(4093)]
public struct Longs32744
{
    private long _element;
}
