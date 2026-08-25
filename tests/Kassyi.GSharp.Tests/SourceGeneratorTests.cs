using System.Numerics;
using Kassyi.GSharp.Core;
using Xunit;
using Xunit.Abstractions;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Tests;

public class SourceGeneratorTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [GpuKernel]
    public static void MyTestKernel(int[] a, int[] b, int[] c)
    {
        c[0] = a[0] + b[0];
    }

    [Fact]
    [Trait("RoadmapId", "Core")]
    public void TestGeneratedWrapper()
    {
        string expectedSource = SourceGeneratorTestsWrapper.MyTestKernel_CudaSource;

        Assert.Contains("extern \"C\" __global__ void MyTestKernel", expectedSource);
        Assert.Contains("c[0]=(a[0]+b[0]);", expectedSource);

        int[] aArray = [10];
        int[] bArray = [20];
        int[] cArray = [0];

        SourceGeneratorTestsWrapper.Launch_MyTestKernel(aArray, bArray, cArray);

        Assert.Equal(30, cArray[0]);
    }

    [GpuKernel]
    public static void ComplexLogicKernel(int[] a, int[] b, int[] c)
    {
        for (int i = 0; i < 5; i++)
        {
            if (a[0] > 10)
            {
                c[0] = c[0] + a[0];
            }
            else
            {
                c[0] = c[0] + b[0];
            }
        }
    }

    [Fact]
    [Trait("RoadmapId", "Core")]
    public void TestComplexLogicKernel()
    {
        string expectedSource = SourceGeneratorTestsWrapper.ComplexLogicKernel_CudaSource;

        int[] aArray = [5]; // Less than 10, should take else branch (b[0]) 5 times
        int[] bArray = [2];
        int[] cArray = [0];

        SourceGeneratorTestsWrapper.Launch_ComplexLogicKernel(aArray, bArray, cArray);
        Assert.Equal(10, cArray[0]); // 5 * 2 = 10

        aArray[0] = 15; // Greater than 10, should take if branch (a[0]) 5 times
        cArray[0] = 0;
        SourceGeneratorTestsWrapper.Launch_ComplexLogicKernel(aArray, bArray, cArray);
        Assert.Equal(75, cArray[0]); // 5 * 15 = 75
    }

    [GpuKernel]
    public static void IntrinsicsKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        if (tid < 10)
        {
            // Thread 0-9 adds its id to data[0] atomically
            Gpu.AtomicAdd(ref data[0], tid);
        }

        Gpu.SyncThreads();

        if (tid == 0)
        {
            // Just testing math function
            data[1] = System.Math.Max(data[0], 100);
        }
    }

    [Fact]
    [Trait("RoadmapId", "SYN-49")]
    [Trait("RoadmapId", "SYN-68")]
    public void TestIntrinsicsKernel()
    {
        string expectedSource = SourceGeneratorTestsWrapper.IntrinsicsKernel_CudaSource;

        int[] dataArray = new int[2];
        dataArray[0] = 0; // atomic sum
        dataArray[1] = 0; // max value

        SourceGeneratorTestsWrapper.Launch_IntrinsicsKernel(dataArray, blockDimX: 10);

        Assert.Equal(45, dataArray[0]); // Sum of 0..9 is 45
        Assert.Equal(100, dataArray[1]);
    }

    public struct Vector2
    {
        public float X;
        public float Y;
    }

    [GpuKernel]
    public static void StructKernel(Vector2[] vectors, int length)
    {
        int tid = Kassyi.GSharp.Core.ThreadIndex.X;
        if (tid < length)
        {
            Vector2 v = vectors[tid];
            v.X = v.X * 2.0f;
            v.Y = v.Y + 1.0f;
            vectors[tid] = v;
        }
    }

    [Fact]
    [Trait("RoadmapId", "Core")]
    public void TestStructKernel()
    {
        string expectedSource = SourceGeneratorTestsWrapper.StructKernel_CudaSource;

        Vector2[] dataArray = new Vector2[1];
        dataArray[0] = new Vector2 { X = 1.5f, Y = 2.0f };

        SourceGeneratorTestsWrapper.Launch_StructKernel(dataArray, 1);

        Assert.Equal(3.0f, dataArray[0].X);
        Assert.Equal(3.0f, dataArray[0].Y);
    }

    [GpuKernel]
    public static void ControlFlowKernel(int[] data)
    {
        int tid = ThreadIndex.X;

        // 1. switch
        switch (tid)
        {
            case 0:
                data[tid] = 10;
                break;
            case 1:
            case 2:
                data[tid] = 20;
                break;
            default:
                data[tid] = 30;
                break;
        }

        // 2. do-while
        int count = 0;
        do
        {
            count = count + 1;
        } while (count < 3);

        data[tid] = data[tid] + count;

        // 3. && and ||
        if (tid == 0 && count == 3)
        {
            data[tid] = data[tid] + 100;
        }

        if (tid == 1 || tid == 2)
        {
            data[tid] = data[tid] + 200;
        }
    }

    [Fact]
    [Trait("RoadmapId", "Core")]
    public void TestControlFlowKernel()
    {
        string expectedSource = SourceGeneratorTestsWrapper.ControlFlowKernel_CudaSource;

        int[] dataArray = new int[4];

        SourceGeneratorTestsWrapper.Launch_ControlFlowKernel(dataArray, blockDimX: 4);

        // tid 0: 10 + 3 + 100 = 113
        Assert.Equal(113, dataArray[0]);
        // tid 1: 20 + 3 + 200 = 223
        Assert.Equal(223, dataArray[1]);
        // tid 2: 20 + 3 + 200 = 223
        Assert.Equal(223, dataArray[2]);
        // tid 3: 30 + 3 = 33
        Assert.Equal(33, dataArray[3]);
    }

    [GpuDevice]
    public static int MyCustomAdd(int a, int b)
    {
        return a + b;
    }

    [GpuKernel]
    public static void DeviceMethodKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        data[tid] = MyCustomAdd(data[tid], 100);
    }

    [Fact]
    [Trait("RoadmapId", "Core")]
    public void TestDeviceMethodKernel()
    {
        string expectedSource = SourceGeneratorTestsWrapper.DeviceMethodKernel_CudaSource;

        Assert.Contains("__device__ int MyCustomAdd", expectedSource);

        int[] dataArray = [5, 10];
        SourceGeneratorTestsWrapper.Launch_DeviceMethodKernel(dataArray, blockDimX: 2);

        Assert.Equal(105, dataArray[0]);
        Assert.Equal(110, dataArray[1]);
    }

    [GpuKernel]
    public static void SharedMemoryKernel(int[] data, int dynSize)
    {
        int tid = ThreadIndex.X;

        // Static Shared Memory
        SharedMemory<int> sStatic = new SharedMemory<int>(256);

        // Dynamic Shared Memory
        SharedMemory<int> sDyn1 = new SharedMemory<int>(dynSize);
        SharedMemory<float> sDyn2 = new SharedMemory<float>(dynSize);

        sStatic[tid] = data[tid];
        sDyn1[tid] = data[tid] * 2;
        sDyn2[tid] = 3.14f;

        Gpu.SyncThreads();

        data[tid] = sStatic[tid] + sDyn1[tid];
    }

    [Fact]
    [Trait("RoadmapId", "SYN-47")]
    public void TestSharedMemoryKernel()
    {
        string expectedSource = SourceGeneratorTestsWrapper.SharedMemoryKernel_CudaSource;

        Assert.Contains("__shared__ int sStatic[256];", expectedSource);
        Assert.Contains("extern __shared__ char __gsharp_dynamic_smem[];", expectedSource);

        int[] dataArray = new int[4] { 1, 2, 3, 4 };

        // sDyn1 (int) + sDyn2 (float) -> 4 * 4 + 4 * 4 = 32 bytes
        SourceGeneratorTestsWrapper.Launch_SharedMemoryKernel(dataArray, 4, blockDimX: 4, sharedMemBytes: 32);

        // data[tid] + data[tid] * 2 = 3 * data[tid]
        Assert.Equal(3, dataArray[0]);
        Assert.Equal(6, dataArray[1]);
        Assert.Equal(9, dataArray[2]);
        Assert.Equal(12, dataArray[3]);
    }

    [Fact]
    [Trait("RoadmapId", "Core")]
    public void TestInvalidKernelCompilation_ThrowsWrappedException()
    {
        var invalidCode = "extern \"C\" __global__ void bad_kernel(int* x) { this is invalid c++ code }";

        var ex = Assert.Throws<Exception>(() =>
            Kassyi.GSharp.Runtime.Jit.JitEngine.GetOrCompileKernel(invalidCode, "bad_kernel")
        );

        Assert.Contains("NVCC Compilation Failed", ex.Message);
        Assert.NotNull(ex.InnerException); // Assert inner exception is preserved
    }

    [GpuKernel]
    public static void BitOperationsTestKernel(uint[] u32Data, ulong[] u64Data)
    {
        uint u32Val = u32Data[0];
        u32Data[1] = (uint)BitOperations.LeadingZeroCount(u32Val);
        u32Data[2] = (uint)BitOperations.PopCount(u32Val);
        u32Data[3] = (uint)BitOperations.TrailingZeroCount(u32Val);
        u32Data[4] = (uint)BitOperations.Log2(u32Val);
        u32Data[5] = BitOperations.RotateLeft(u32Val, 5);
        u32Data[6] = BitOperations.RotateRight(u32Val, 3);

        ulong u64Val = u64Data[0];
        u64Data[1] = (ulong)BitOperations.LeadingZeroCount(u64Val);
        u64Data[2] = (ulong)BitOperations.PopCount(u64Val);
        u64Data[3] = (ulong)BitOperations.TrailingZeroCount(u64Val);
        u64Data[4] = (ulong)BitOperations.Log2(u64Val);
        u64Data[5] = BitOperations.RotateLeft(u64Val, 10);
        u64Data[6] = BitOperations.RotateRight(u64Val, 7);
    }

    [Fact]
    [Trait("RoadmapId", "SYN-35")]
    public void TestBitOperationsKernel()
    {
        uint[] u32Data = new uint[7];
        u32Data[0] = 0b00000000_10101010_11110000_00001000;

        ulong[] u64Data = new ulong[7];
        u64Data[0] = 0b00000000_00000000_10101010_11110000_00001000_00000000_00000000_00001000;

        SourceGeneratorTestsWrapper.Launch_BitOperationsTestKernel(u32Data, u64Data);

        uint u32Val = u32Data[0];
        Assert.Equal((uint)BitOperations.LeadingZeroCount(u32Val), u32Data[1]);
        Assert.Equal((uint)BitOperations.PopCount(u32Val), u32Data[2]);
        Assert.Equal((uint)BitOperations.TrailingZeroCount(u32Val), u32Data[3]);
        Assert.Equal((uint)BitOperations.Log2(u32Val), u32Data[4]);
        Assert.Equal(BitOperations.RotateLeft(u32Val, 5), u32Data[5]);
        Assert.Equal(BitOperations.RotateRight(u32Val, 3), u32Data[6]);

        ulong u64Val = u64Data[0];
        Assert.Equal((ulong)BitOperations.LeadingZeroCount(u64Val), u64Data[1]);
        Assert.Equal((ulong)BitOperations.PopCount(u64Val), u64Data[2]);
        Assert.Equal((ulong)BitOperations.TrailingZeroCount(u64Val), u64Data[3]);
        Assert.Equal((ulong)BitOperations.Log2(u64Val), u64Data[4]);
        Assert.Equal(BitOperations.RotateLeft(u64Val, 10), u64Data[5]);
        Assert.Equal(BitOperations.RotateRight(u64Val, 7), u64Data[6]);

        // Test with 0
        u32Data[0] = 0;
        u64Data[0] = 0;
        SourceGeneratorTestsWrapper.Launch_BitOperationsTestKernel(u32Data, u64Data);
        Assert.Equal((uint)BitOperations.LeadingZeroCount(0u), u32Data[1]);
        Assert.Equal((uint)BitOperations.PopCount(0u), u32Data[2]);
        Assert.Equal((uint)BitOperations.TrailingZeroCount(0u), u32Data[3]);
        Assert.Equal((uint)BitOperations.Log2(0u), u32Data[4]);
        Assert.Equal((ulong)BitOperations.LeadingZeroCount(0ul), u64Data[1]);
        Assert.Equal((ulong)BitOperations.PopCount(0ul), u64Data[2]);
    }


    [GpuKernel]
    public static unsafe void FixedBufferKernel(uint[] data)
    {
        fixed (uint* p = &data[0])
        {
            p[1] = p[0] + 10;
        }
    }

    [Fact]
    [Trait("RoadmapId", "SYN-14")]
    public void TestFixedBufferKernel()
    {
        uint[] data = [5, 0];
        SourceGeneratorTestsWrapper.Launch_FixedBufferKernel(data);
        Assert.Equal(15u, data[1]);
    }


    [GpuKernel]
    public static unsafe void PointerIncrementKernel(uint[] src, uint[] dest)
    {
        fixed (uint* pSrc = &src[0], pDest = &dest[0])
        {
            uint* ptrS = pSrc;
            uint* ptrD = pDest;
            for (int i = 0; i < 3; i++)
            {
                *ptrD++ = *ptrS++;
            }
        }
    }

    [Fact]
    [Trait("RoadmapId", "SYN-36")]
    public void TestPointerIncrementKernel()
    {
        uint[] src = [10, 20, 30];
        uint[] dest = [0, 0, 0];
        SourceGeneratorTestsWrapper.Launch_PointerIncrementKernel(src, dest);
        Assert.Equal(10u, dest[0]);
        Assert.Equal(20u, dest[1]);
        Assert.Equal(30u, dest[2]);
    }


    [GpuKernel]
    public static void InlinePtxKernel(uint[] data)
    {
        Gpu.InlinePtx("data[0] = 999;");
    }

    [Fact]
    [Trait("RoadmapId", "SYN-77")]
    public void TestInlinePtxKernel()
    {
        uint[] data = [0];
        SourceGeneratorTestsWrapper.Launch_InlinePtxKernel(data);
        Assert.Equal(999u, data[0]);
    }
}

