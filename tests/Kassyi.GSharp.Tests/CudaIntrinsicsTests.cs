using Kassyi.GSharp.Transpiler.Pipeline;

namespace Kassyi.GSharp.Tests;

public class CudaIntrinsicsTests
{
    [GpuKernel]
    public static void SimpleReduction(int[] input, int[] output, int n)
    {
        int tid = Cuda.ThreadIdxX;
        int i = Cuda.BlockIdxX * Cuda.BlockDimX + Cuda.ThreadIdxX;
        
        if (i < n)
        {
            Cuda.AtomicAdd(ref output[0], input[i]);
        }
        
        Cuda.SyncThreads();
    }

    [Fact]
    public void Transpile_IntrinsicKernel_ShouldGenerateCorrectCudaCode()
    {
        var method = typeof(CudaIntrinsicsTests).GetMethod(nameof(SimpleReduction))!;
        
        var metadata = Kassyi.GSharp.Transpiler.Pipeline.MethodExtractor.Extract(method.DeclaringType!.Assembly.Location, method.DeclaringType.FullName!, method.Name);
        var context = Kassyi.GSharp.Transpiler.Ast.AstBuilder.Build(metadata);
        var transpiled = Kassyi.GSharp.Transpiler.CudaGenerator.Generate(context);



        var code = transpiled.CudaCode;
        
        Assert.Contains("threadIdx.x", code);
        Assert.Contains("blockIdx.x", code);
        Assert.Contains("blockDim.x", code);
        Assert.Contains("atomicAdd(&(output[0]), input[loc_1])", code);
        Assert.Contains("__syncthreads()", code);
    }
}
