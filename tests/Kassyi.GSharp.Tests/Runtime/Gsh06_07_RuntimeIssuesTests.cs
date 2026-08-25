using System.IO;
using System.Threading.Tasks;
using Kassyi.GSharp.Runtime.Jit;
using Xunit;

namespace Kassyi.GSharp.Tests.Runtime;

public class Gsh06_07_RuntimeIssuesTests
{
    [Fact(Skip = "未実装: GSH-06 (PTX / C++ コンパイルエラーの行番号マッピング)")]
    [Trait("FeatureId", "GSH-06")]
    [Trait("Category", "Runtime")]
    public void Test_GSH06_LineNumberMappingInDiagnostics()
    {
    }

    [Fact]
    [Trait("FeatureId", "GSH-07")]
    [Trait("Category", "Runtime")]
    public void Test_GSH07_JitDiskCache_RecoversFromCorruptedCache()
    {
        const string kernelName = "CacheTestKernel";
        const string kernelSource = @"extern ""C"" __global__ void CacheTestKernel(int* a) { *a = 42; }";

        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Henicos",
            "GSharp",
            "PtxCache");
        
        Directory.CreateDirectory(cacheDir);

        // Pre-create corrupted 0-byte or garbage cache files matching wildcard
        var dummyCorruptPath = Path.Combine(cacheDir, $"{kernelName}_corrupt_dummy.ptx");
        File.WriteAllBytes(dummyCorruptPath, [0x00, 0xDE, 0xAD, 0xBE, 0xEF]);

        // Attempt compilation / cache loading
        var func1 = JitEngine.GetOrCompileKernel(kernelName, kernelSource);
        Assert.NotEqual(IntPtr.Zero, func1);

        // Concurrent access test (stress testing atomic cache write)
        Parallel.For(0, 10, _ =>
        {
            var funcConcurrent = JitEngine.GetOrCompileKernel(kernelName, kernelSource);
            Assert.NotEqual(IntPtr.Zero, funcConcurrent);
        });
    }
}

