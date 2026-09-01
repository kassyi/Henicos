
namespace Kassyi.GSharp.Tests;

public class ArchitectureAndOptimizationTests
:  TranspilerTestBase{
    private readonly ITestOutputHelper _output;

    public ArchitectureAndOptimizationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [SkippableTheory]
    [Trait("FeatureId", "GEN-10")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "Execution")]
    public void CompileToPtx_DifferentArchitectures_GeneratesDifferentSizes(ITranspilerDialect dialect)
    {
        Skip.IfNot(dialect.IsCuda, "Explicit AI Judgment: Tests CUDA runtime/hardware specifics (e.g., SharedMemory, JIT, PTX, cuCtx).");
        var source = @"
extern ""C"" __global__ void TestKernel(int* data) {
    int idx = blockIdx.x * blockDim.x + threadIdx.x;
    data[idx] = idx * 2;
}";

        var ptx89 = NvrtcCompiler.CompileToPtx(source, "TestKernel", ["-arch=sm_89"]);
        var ptx86 = NvrtcCompiler.CompileToPtx(source, "TestKernel", ["-arch=sm_86"]);

        Assert.True(ptx89.Length > 0);
        Assert.True(ptx86.Length > 0);
    }
    [MemberData(nameof(TranspilerTestBase.Dialects))]
    [SkippableTheory]
    [Trait("FeatureId", "GEN-10")]
    [Trait("Category", "SadPath")]
    [Trait("Category", "Execution")]
    public void CompileToPtx_InvalidArchitecture_ThrowsException(ITranspilerDialect dialect)
    {
        Skip.IfNot(dialect.IsCuda, "Explicit AI Judgment: Tests CUDA runtime/hardware specifics (e.g., SharedMemory, JIT, PTX, cuCtx).");
        var source = @"
extern ""C"" __global__ void TestKernel(int* data) {
    int idx = threadIdx.x;
    data[idx] = idx;
}";

        // Expect exception when passing an invalid architecture
        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            NvrtcCompiler.CompileToPtx(source, "TestKernel", ["-arch=sm_999"]);
        });
        
        Assert.Contains("nvrtc: error", ex.Message);
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [SkippableTheory]
    [Trait("FeatureId", "GEN-11")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "Execution")]
    public void JitConstant_Unroll_AltersPtxOutput(ITranspilerDialect dialect)
    {
        Skip.IfNot(dialect.IsCuda, "Explicit AI Judgment: Tests CUDA runtime/hardware specifics (e.g., SharedMemory, JIT, PTX, cuCtx).");
        var srcDynamic = @"
extern ""C"" __global__ void TestKernelDynamic(int* data, int limit) {
    int sum = 0;
    for(int i = 0; i < limit; i++) {
        sum += i * i;
    }
    data[0] = sum;
}";

        var srcStatic = @"
#define limit 5
extern ""C"" __global__ void TestKernelStatic(int* data) {
    int sum = 0;
    #pragma unroll
    for(int i = 0; i < limit; i++) {
        sum += i * i;
    }
    data[0] = sum;
}";

        var ptxDynamic =
            NvrtcCompiler.CompileToPtx(srcDynamic, "TestKernelDynamic", ["-arch=sm_86"]);
        var ptxStatic =
            NvrtcCompiler.CompileToPtx(srcStatic, "TestKernelStatic", ["-arch=sm_86"]);

        Assert.True(ptxDynamic.Length > 0);
        Assert.True(ptxStatic.Length > 0);
        Assert.NotEqual(ptxDynamic.Length, ptxStatic.Length); // The compiler will optimize them differently.
    }
}
