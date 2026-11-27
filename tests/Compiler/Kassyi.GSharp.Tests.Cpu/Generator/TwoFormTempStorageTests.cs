using Xunit.Abstractions;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// Two-form CUDA kernels emit both a by-value and a by-address form. Per-form temporary tables
/// (<c>DiscardStorages</c>, <c>SharedMemoryLifts</c>) must be cleared between forms so the second
/// form does not reuse the first form's count and redeclare the same temporary name.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class TwoFormTempStorageTests(ITestOutputHelper output)
{
    private string GenerateCuda(string source)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempPath);
        try
        {
            var options = new Dictionary<string, string>
            {
                ["build_property.IntermediateOutputPath"] = ".",
                ["build_property.MSBuildProjectDirectory"] = tempPath,
            };
            GeneratorTestRunner.RunGenerator(source, "Cuda", options);
            var cuPath = Assert.Single(Directory.GetFiles(tempPath, "*.g.cu"));
            var text = File.ReadAllText(cuPath);
            output.WriteLine(text);
            return text;
        }
        finally
        {
            Directory.Delete(tempPath, recursive: true);
        }
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void TwoFormCudaKernel_WithMultipleDiscardOutArguments_NumbersEachFormUniquely()
    {
        const string source = """
            using Kassyi.GSharp.Core;

            public static partial class DiscardKernels
            {
                [GpuDeviceFunction]
                public static void Get(out int value)
                {
                    value = ThreadIndex.X;
                }

                [GpuKernel]
                public static void DiscardTwiceKernel(in int n, int[] data)
                {
                    Get(out _);
                    Get(out _);
                    data[0] = n;
                }
            }
            """;

        var cu = GenerateCuda(source);

        Assert.Contains("DiscardTwiceKernel__gsharp_in_by_address", cu, StringComparison.Ordinal);
        Assert.DoesNotContain("__gsharp_discard_2", cu, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(cu, "__gsharp_discard_0{}"));
        Assert.Equal(2, CountOccurrences(cu, "__gsharp_discard_1{}"));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void TwoFormCudaKernel_WithMultipleLiftedSharedMemoryCreations_NumbersEachFormUniquely()
    {
        const string source = """
            using Kassyi.GSharp.Core;

            public static partial class SharedMemoryLiftKernels
            {
                [GpuDeviceFunction]
                public static void Touch(SharedMemory<int> smem)
                {
                    smem[0] = ThreadIndex.X;
                }

                [GpuKernel]
                public static void SharedLiftTwiceKernel(in int n, int[] data)
                {
                    Touch(new SharedMemory<int>(4));
                    Touch(new SharedMemory<int>(4));
                    data[0] = n;
                }
            }
            """;

        var cu = GenerateCuda(source);

        Assert.Contains("SharedLiftTwiceKernel__gsharp_in_by_address", cu, StringComparison.Ordinal);
        Assert.DoesNotContain("__gsharp_smem_2", cu, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(cu, "__shared__ int __gsharp_smem_0[4]"));
        Assert.Equal(2, CountOccurrences(cu, "__shared__ int __gsharp_smem_1[4]"));
    }
}
