using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VerifyXunit;
using Kassyi.GSharp.Compiler;
using System.Threading.Tasks;
using Xunit;
using System.Linq;

namespace Kassyi.GSharp.Tests;

public class GeneratorSnapshotTests
{
    private static async Task VerifyGeneratorAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        // Find core references (we need basic ones for the compiler to run smoothly)
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Kassyi.GSharp.Core.GpuKernelAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Kassyi.GSharp.Core.ThreadIndex).Assembly.Location)
        };

        var compilation = CSharpCompilation.Create(
            "TestCompilation",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(true));

        var generator = new GpuKernelGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        await Verifier.Verify(driver).UseDirectory("Snapshots");
    }

    [Fact]
    public async Task AddKernel_GeneratesCorrectCudaSource()
    {
        var source = """
            using Kassyi.GSharp.Core;

            namespace TestNamespace
            {
                public static class TestKernels
                {
                    [GpuKernel]
                    public static void AddKernel(int[] a, int[] b, int[] c)
                    {
                        c[0] = a[0] + b[0];
                    }
                }
            }
            """;
        await VerifyGeneratorAsync(source);
    }

    [Fact]
    public async Task ChunkAddKernel_GeneratesCorrectCudaSource()
    {
        var source = """
            using Kassyi.GSharp.Core;

            namespace Kassyi.GSharp.Stdlib.Math
            {
                public static class GpuBigIntKernels
                {
                    [GpuKernel]
                    public static void ChunkAddKernel(uint[] dA, uint[] dB, uint[] dResult, long[] carryBuffer, int numWords, int chunkSize)
                    {
                        int chunkIdx = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
                        int start = chunkIdx * chunkSize;
                        if (start >= numWords) return;
                        
                        int end = start + chunkSize;
                        if (end > numWords) end = numWords;
                        
                        ulong localCarry = 0;
                        for (int i = start; i < end; i++)
                        {
                            ulong current = (ulong)dA[i] + (ulong)dB[i] + localCarry;
                            dResult[i] = (uint)(current & 0xFFFFFFFF);
                            localCarry = current >> 32;
                        }
                        carryBuffer[chunkIdx + 1] = (long)localCarry;
                    }
                    }
                }
            }
            """;
        await VerifyGeneratorAsync(source);
    }

    [Fact]
    public async Task ComprehensiveSpecKernel_GeneratesCorrectCudaSource()
    {
        var thisFile = new StackTrace(true).GetFrame(0).GetFileName();
        var dir = Path.GetDirectoryName(thisFile);
        var sourcePath = Path.Combine(dir, "TestCases", "ComprehensiveSpecKernel.cs");
        var source = await File.ReadAllTextAsync(sourcePath);
        await VerifyGeneratorAsync(source);
    }

    [Fact]
    public async Task BitOperationsKernel_GeneratesCorrectCudaSource()
    {
        var source = """
            using Kassyi.GSharp.Core;
            using System.Numerics;

            namespace Kassyi.GSharp.Tests
            {
                public static class BitOpsTestKernels
                {
                    [GpuKernel]
                    public static void BitOpsKernel(uint[] u32Data, ulong[] u64Data)
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
                }
            }
            """;
        await VerifyGeneratorAsync(source);
    }
}
