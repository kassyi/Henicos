using Kassyi.GSharp.Core;

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

public static partial class StructSizeDiagnosticKernels
{
    [GpuKernel]
    public static void AtLimitKernel(Block4096 s)
    {
        int x = (int)s.A.A.A;
        _ = x;
    }

    [GpuKernel]
    public static void OverLimitKernel(int[] data, Block4096 s)
    {
        data[0] = (int)s.A.A.A;
    }

    [GpuKernel]
    public static void PaddingKernel(byte flag, Block4096 s)
    {
        int x = flag + (int)s.A.A.A;
        _ = x;
    }

    [GpuKernel]
    public static int ResultKernel(Block4096 s)
    {
        return (int)s.A.A.A;
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
}
