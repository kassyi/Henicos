using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Runtime.Jit;
using Xunit;
using Xunit.Abstractions;
using System.Text;

namespace Kassyi.GSharp.Tests;

public class PtxEngineExperimentTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Fact]
    [Trait("FeatureId", "GEN-12")]
    public void ExecutePtxInMemory_ShouldAddNumbersOnGpu()
    {
        var (context, smVersion) = InitializeCuda();
        try
        {
            var ptxBytes = CompileTestKernel(smVersion);
            RunKernelAndVerify(ptxBytes, 12, 34, 46);
        }
        finally
        {
            CudaDriverApi.cuCtxDestroy_v2(context);
        }
    }

    private byte[] CompileTestKernel(string smVersion)
    {
        var kernelCode = PtxEngineExperimentKernelsWrapper.AddKernel_CudaSource;
        _output.WriteLine($"Compiling CU to PTX (Architecture: {smVersion})...");
        
        var options = new List<string> { $"-arch={smVersion}" };
        var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH") ?? @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.3";
        
        var includePath = Path.Combine(cudaPath, "include");
        if (Directory.Exists(includePath))
        {
            options.Add($"-I{includePath}");
            var ccclPath = Path.Combine(includePath, "cccl");
            if (Directory.Exists(ccclPath))
            {
                options.Add($"-I{ccclPath}");
                var libcxxPath = Path.Combine(ccclPath, "libcxx", "include");
                if (Directory.Exists(libcxxPath))
                {
                    options.Add($"-I{libcxxPath}");
                }
            }
        }
        
        return NvrtcCompiler.CompileToPtx(kernelCode, "AddKernel", [.. options]);
    }

    private (IntPtr context, string smVersion) InitializeCuda()
    {
        _output.WriteLine("Initializing CUDA Driver API...");
        CudaException.ThrowIfError(CudaDriverApi.cuInit(0));
        CudaException.ThrowIfError(CudaDriverApi.cuDeviceGet(out int device, 0));
        CudaException.ThrowIfError(CudaDriverApi.cuDeviceGetAttribute(out var major, CudaDriverApi.CuDeviceAttribute.ComputeCapabilityMajor, device));
        CudaException.ThrowIfError(CudaDriverApi.cuDeviceGetAttribute(out var minor, CudaDriverApi.CuDeviceAttribute.ComputeCapabilityMinor, device));
        CudaException.ThrowIfError(CudaDriverApi.cuCtxCreate_v2(out IntPtr context, 0, device));
        return (context, $"sm_{major}{minor}");
    }

    private unsafe void RunKernelAndVerify(byte[] ptxBytes, int a, int b, int expected)
    {
        IntPtr dA = IntPtr.Zero, dB = IntPtr.Zero, dC = IntPtr.Zero;

        try
        {
            CudaException.ThrowIfError(CudaDriverApi.cuModuleLoadDataEx(out IntPtr module, ptxBytes, 0, null, null));
            CudaException.ThrowIfError(CudaDriverApi.cuModuleGetFunction(out IntPtr hfunc, module, "AddKernel"));
            
            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out dA, 4));
            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out dB, 4));
            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out dC, 4));

            CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(dA, (IntPtr)(&a), 4));
            CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(dB, (IntPtr)(&b), 4));

            void** kernelParams = stackalloc void*[3];
            kernelParams[0] = &dA;
            kernelParams[1] = &dB;
            kernelParams[2] = &dC;

            CudaException.ThrowIfError(CudaDriverApi.cuLaunchKernel(
                hfunc, 1, 1, 1, 1, 1, 1, 0, IntPtr.Zero, kernelParams, IntPtr.Zero));
                
            int c = 0;
            CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2((IntPtr)(&c), dC, 4));

            Assert.Equal(expected, c);
        }
        finally
        {
            if (dA != IntPtr.Zero) CudaDriverApi.cuMemFree_v2(dA);
            if (dB != IntPtr.Zero) CudaDriverApi.cuMemFree_v2(dB);
            if (dC != IntPtr.Zero) CudaDriverApi.cuMemFree_v2(dC);
        }
    }
}
