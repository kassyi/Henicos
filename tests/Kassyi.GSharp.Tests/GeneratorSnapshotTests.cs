using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VerifyXunit;
using Kassyi.Generators.GSharp;
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
            MetadataReference.CreateFromFile(typeof(Kassyi.GSharp.Runtime.GpuKernelAttribute).Assembly.Location),
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

            namespace Kassyi.Math.GpuBigInt
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
}
