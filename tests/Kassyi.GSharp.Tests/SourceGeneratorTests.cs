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
    public void TestGeneratedWrapper()
    {
        // 1. Check if the constant exists
        string expectedSource = SourceGeneratorTestsWrapper.MyTestKernel_CudaSource;
        _output.WriteLine("Generated C++ Source: ");
        _output.WriteLine(expectedSource);

        Assert.Contains("extern \"C\" __global__ void MyTestKernel", expectedSource);
        Assert.Contains("c[0] = (a[0] + b[0]);", expectedSource);

        // 2. Launch it and verify it calculates correctly!
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
    public void TestComplexLogicKernel()
    {
        string expectedSource = SourceGeneratorTestsWrapper.ComplexLogicKernel_CudaSource;
        _output.WriteLine("Complex Logic C++ Source:");
        _output.WriteLine(expectedSource);

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
            data[1] = Math.Max(data[0], 100);
        }
    }

    [Fact]
    public void TestIntrinsicsKernel()
    {
        string expectedSource = SourceGeneratorTestsWrapper.IntrinsicsKernel_CudaSource;
        _output.WriteLine("Intrinsics C++ Source:");
        _output.WriteLine(expectedSource);

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
    public void TestStructKernel()
    {
        string expectedSource = SourceGeneratorTestsWrapper.StructKernel_CudaSource;
        _output.WriteLine("Struct C++ Source:");
        _output.WriteLine(expectedSource);

        Vector2[] dataArray = new Vector2[1];
        dataArray[0] = new Vector2 { X = 1.5f, Y = 2.0f };

        SourceGeneratorTestsWrapper.Launch_StructKernel(dataArray, 1);

        Assert.Equal(3.0f, dataArray[0].X);
        Assert.Equal(3.0f, dataArray[0].Y);
    }

    [Fact]
    public void TestInvalidKernelCompilation_ThrowsWrappedException()
    {
        var invalidCode = "extern \"C\" __global__ void bad_kernel(int* x) { this is invalid c++ code }";

        var ex = Assert.Throws<Exception>(() =>
            Kassyi.GSharp.Runtime.Transpiler.Pipeline.JitEngine.GetOrCompileKernel(invalidCode, "bad_kernel")
        );

        Assert.Contains("NVCC Compilation Failed", ex.Message);
        Assert.NotNull(ex.InnerException); // Assert inner exception is preserved
    }
}
