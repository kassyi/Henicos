using System.IO;
using Kassyi.GSharp.Runtime.Jit;
using Xunit;

namespace Kassyi.GSharp.Tests.Runtime;

public class GEN_44_BackendCompilerTests
{
    [SkippableFact]
    [Trait("Category", "Unit")]
    [Trait("FeatureId", "GEN-44")]
    public void Test_GEN44_NvccCompiler_CompileToFatbin_ProducesOutput()
    {
        Skip.If(!NvccCompiler.IsMsvcAvailable || !NvccCompiler.IsNvccAvailable, "MSVC or NVCC is not available");

        var tempDir = Path.GetTempPath();
        var baseName = System.Guid.NewGuid().ToString("N");
        var cuFile = Path.Combine(tempDir, baseName + ".cu");
        var fatbinFile = Path.Combine(tempDir, baseName + ".fatbin");

        try
        {
            var source = """
            extern "C" __global__ void MyKernel(int* data) {
                int idx = threadIdx.x + blockIdx.x * blockDim.x;
                data[idx] = data[idx] * 2;
            }
            """;
            File.WriteAllText(cuFile, source);

            var result = NvccCompiler.CompileToFatbin(cuFile, fatbinFile, null, tempDir, "sm_86");

            Assert.True(File.Exists(fatbinFile));
            
            // PTXAS output parser should extract register count
            Assert.True(result.RegistersUsed > 0, "RegistersUsed should be extracted from ptxas output");
        }
        finally
        {
            if (File.Exists(cuFile)) File.Delete(cuFile);
            if (File.Exists(fatbinFile)) File.Delete(fatbinFile);
        }
    }
}
