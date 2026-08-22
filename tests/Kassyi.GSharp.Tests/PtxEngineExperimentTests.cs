using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Transpiler.Pipeline;
using Xunit;
using Xunit.Abstractions;
using System.Text;

namespace Kassyi.GSharp.Tests;

public class PtxEngineExperimentTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public void ExecutePtxInMemory_ShouldAddNumbersOnGpu()
    {
        // 1. Prepare simple CUDA C++ kernel
        var kernelCode = """
            extern "C" __global__ void add_kernel(int* a, int* b, int* c) {
                *c = *a + *b;
            }
            """;
        var tempDir = Path.Combine(Path.GetTempPath(), "KassyiPtxExp");
        Directory.CreateDirectory(tempDir);
        var cuFile = Path.Combine(tempDir, "exp.cu");
        var fatbinFile = Path.Combine(tempDir, "exp.fatbin");

        File.WriteAllText(cuFile, kernelCode, new UTF8Encoding(false));

        // 2. Compile to FATBIN using existing NvccCompiler
        _output.WriteLine("Compiling CU to FATBIN...");
        NvccCompiler.CompileToFatbin(cuFile, fatbinFile, null, tempDir);

        Assert.True(File.Exists(fatbinFile));
        var ptxBytes = File.ReadAllBytes(fatbinFile);

        // 3. Initialize CUDA Driver API
        _output.WriteLine("Initializing CUDA Driver API...");
        CudaException.ThrowIfError(CudaDriverApi.cuInit(0));

        CudaException.ThrowIfError(CudaDriverApi.cuDeviceGet(out int device, 0));
        CudaException.ThrowIfError(CudaDriverApi.cuCtxCreate_v2(out IntPtr context, 0, device));

        try
        {
            // 4. Load FATBIN Module
            _output.WriteLine("Loading FATBIN module...");
            CudaException.ThrowIfError(CudaDriverApi.cuModuleLoadDataEx(out IntPtr module, ptxBytes, 0, null, null));

            // 5. Get Function
            _output.WriteLine("Getting function handle...");
            CudaException.ThrowIfError(CudaDriverApi.cuModuleGetFunction(out IntPtr hfunc, module, "add_kernel"));

            // 6. Allocate Memory
            _output.WriteLine("Allocating memory on GPU...");
            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out IntPtr dA, 4));
            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out IntPtr dB, 4));
            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out IntPtr dC, 4));

            // 7. Copy Host to Device
            int a = 12;
            int b = 34;

            IntPtr pA = Marshal.AllocHGlobal(4);
            IntPtr pB = Marshal.AllocHGlobal(4);
            IntPtr pC = Marshal.AllocHGlobal(4);

            Marshal.WriteInt32(pA, a);
            Marshal.WriteInt32(pB, b);

            CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(dA, pA, 4));
            CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(dB, pB, 4));

            // 8. Launch Kernel
            _output.WriteLine("Launching kernel...");

            // Set up kernel parameters: an array of pointers to the arguments
            // We need pointers to dA, dB, dC itself, so we allocate unmanaged memory for those pointers.
            IntPtr pArgA = Marshal.AllocHGlobal(IntPtr.Size);
            IntPtr pArgB = Marshal.AllocHGlobal(IntPtr.Size);
            IntPtr pArgC = Marshal.AllocHGlobal(IntPtr.Size);

            Marshal.WriteIntPtr(pArgA, dA);
            Marshal.WriteIntPtr(pArgB, dB);
            Marshal.WriteIntPtr(pArgC, dC);

            IntPtr[] kernelParams = [pArgA, pArgB, pArgC];

            CudaException.ThrowIfError(CudaDriverApi.cuLaunchKernel(
                hfunc,
                1, 1, 1, // Grid dim
                1, 1, 1, // Block dim
                0, // Shared mem
                IntPtr.Zero, // Stream
                kernelParams,
                IntPtr.Zero // Extra
            ));

            // 9. Copy Device to Host
            _output.WriteLine("Copying result to host...");
            CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2(pC, dC, 4));

            int c = Marshal.ReadInt32(pC);

            _output.WriteLine($"Result: {a} + {b} = {c}");
            Assert.Equal(46, c);

            // 10. Cleanup
            Marshal.FreeHGlobal(pA);
            Marshal.FreeHGlobal(pB);
            Marshal.FreeHGlobal(pC);
            Marshal.FreeHGlobal(pArgA);
            Marshal.FreeHGlobal(pArgB);
            Marshal.FreeHGlobal(pArgC);

            CudaDriverApi.cuMemFree_v2(dA);
            CudaDriverApi.cuMemFree_v2(dB);
            CudaDriverApi.cuMemFree_v2(dC);
        }
        finally
        {
            CudaDriverApi.cuCtxDestroy_v2(context);
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
