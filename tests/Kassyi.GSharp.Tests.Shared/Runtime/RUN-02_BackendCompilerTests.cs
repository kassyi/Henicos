using System.IO;
using Kassyi.GSharp.Runtime.Jit;
using Xunit;

namespace Kassyi.GSharp.Tests.Runtime;

public class RUN_02_BackendCompilerTests
{
    [Fact]
    [Trait("Category", "Unit")]
    [Trait("FeatureId", "RUN-02")]
    public void Test_NvccCompiler_CompileToFatbin_ProducesOutput()
    {
        if (!NvccCompiler.IsMsvcAvailable)
        {
            return; // Skip if MSVC is not available
        }

        var cuFile = Path.GetTempFileName() + ".cu";
        var fatbinFile = Path.GetTempFileName() + ".fatbin";
        var tempDir = Path.GetTempPath();

        try
        {
            var source = @"
extern ""C"" __global__ void MyKernel(int* data) {
    int idx = threadIdx.x + blockIdx.x * blockDim.x;
    data[idx] = data[idx] * 2;
}
";
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
