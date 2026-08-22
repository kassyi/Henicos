using Kassyi.GSharp.GpuBigInt;
using Kassyi.GSharp.Runtime.Transpiler.Pipeline;
using Xunit;
using Xunit.Abstractions;

namespace Kassyi.GSharp.Tests;

public class GpuBigIntKernelsTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public void TestChunkAddKernel_GeneratesCorrectCudaSource()
    {
        string generatedSource = GpuBigIntKernelsWrapper.ChunkAddKernel_CudaSource;
        _output.WriteLine("ChunkAddKernel C++ Source:");
        _output.WriteLine(generatedSource);

        Assert.Contains("extern \"C\" __global__ void ChunkAddKernel", generatedSource);
        Assert.Contains("__gsharp_add_cc", generatedSource);
    }

    [Fact]
    public void TestChunkAddKernel_Launch()
    {
        uint[] dA = new uint[] { 0xFFFFFFFF, 0x00000001, 0xFFFFFFFF }; // 3 words
        uint[] dB = new uint[] { 0x00000001, 0xFFFFFFFF, 0x00000000 };
        uint[] dResult = new uint[3];
        long[] carryBuffer = new long[2]; // chunkIdx + 1

        GpuBigIntKernelsWrapper.Launch_ChunkAddKernel(dA, dB, dResult, carryBuffer, numWords: 3, chunkSize: 3, blockDimX: 1);

        // Word 0: 0xFFFFFFFF + 0x00000001 = 0x00000000, carry = 1
        // Word 1: 0x00000001 + 0xFFFFFFFF + 1 = 0x00000001, carry = 1
        // Word 2: 0xFFFFFFFF + 0x00000000 + 1 = 0x00000000, carry = 1
        
        Assert.Equal(0x00000000u, dResult[0]);
        Assert.Equal(0x00000001u, dResult[1]);
        Assert.Equal(0x00000000u, dResult[2]);
        Assert.Equal(1L, carryBuffer[1]); // localCarry
    }
}
