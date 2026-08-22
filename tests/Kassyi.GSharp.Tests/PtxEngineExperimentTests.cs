using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime.Transpiler.Pipeline;
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
        var tempDir = Path.Combine(Path.GetTempPath(), $"KassyiPtxExp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var fatbinFile = Path.Combine(tempDir, "exp.fatbin");

        try
        {
            var (context, smVersion) = InitializeCuda();
            try
            {
                var ptxBytes = CompileTestKernel(tempDir, fatbinFile, smVersion);
                RunKernelAndVerify(ptxBytes, 12, 34, 46);
            }
            finally
            {
                CudaDriverApi.cuCtxDestroy_v2(context);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch (IOException)
                {
                    // Ignore cleanup errors on best-effort basis (NVCC subprocesses might hold a lock temporarily)
                }
                catch (UnauthorizedAccessException)
                {
                    // Ignore cleanup errors on best-effort basis
                }
            }
        }
    }

    [GpuKernel]
    public static void AddKernel(int[] a, int[] b, int[] c)
    {
        c[0] = a[0] + b[0];
    }

    private byte[] CompileTestKernel(string tempDir, string fatbinFile, string smVersion)
    {
        var kernelCode = PtxEngineExperimentTestsWrapper.AddKernel_CudaSource;
        var cuFile = Path.Combine(tempDir, "exp.cu");
        File.WriteAllText(cuFile, kernelCode, new UTF8Encoding(false));

        _output.WriteLine($"Compiling CU to FATBIN (Architecture: {smVersion})...");
        NvccCompiler.CompileToFatbin(cuFile, fatbinFile, null, tempDir, smVersion);

        Assert.True(File.Exists(fatbinFile));
        return File.ReadAllBytes(fatbinFile);
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

    private void RunKernelAndVerify(byte[] ptxBytes, int a, int b, int expected)
    {
        IntPtr dA = IntPtr.Zero, dB = IntPtr.Zero, dC = IntPtr.Zero;
        IntPtr pA = IntPtr.Zero, pB = IntPtr.Zero, pC = IntPtr.Zero;
        IntPtr pArgA = IntPtr.Zero, pArgB = IntPtr.Zero, pArgC = IntPtr.Zero;

        try
        {
            _output.WriteLine("Loading FATBIN module...");
            CudaException.ThrowIfError(CudaDriverApi.cuModuleLoadDataEx(out IntPtr module, ptxBytes, 0, null, null));

            _output.WriteLine("Getting function handle...");
            CudaException.ThrowIfError(CudaDriverApi.cuModuleGetFunction(out IntPtr hfunc, module, "AddKernel"));

            _output.WriteLine("Allocating memory on GPU...");
            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out dA, 4));
            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out dB, 4));
            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out dC, 4));

            pA = Marshal.AllocHGlobal(4);
            pB = Marshal.AllocHGlobal(4);
            pC = Marshal.AllocHGlobal(4);

            Marshal.WriteInt32(pA, a);
            Marshal.WriteInt32(pB, b);

            CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(dA, pA, 4));
            CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(dB, pB, 4));

            _output.WriteLine("Launching kernel...");
            pArgA = Marshal.AllocHGlobal(IntPtr.Size);
            pArgB = Marshal.AllocHGlobal(IntPtr.Size);
            pArgC = Marshal.AllocHGlobal(IntPtr.Size);

            Marshal.WriteIntPtr(pArgA, dA);
            Marshal.WriteIntPtr(pArgB, dB);
            Marshal.WriteIntPtr(pArgC, dC);

            IntPtr[] kernelParams = [pArgA, pArgB, pArgC];

            CudaException.ThrowIfError(CudaDriverApi.cuLaunchKernel(
                hfunc, 1, 1, 1, 1, 1, 1, 0, IntPtr.Zero, kernelParams, IntPtr.Zero));

            _output.WriteLine("Copying result to host...");
            CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2(pC, dC, 4));

            int c = Marshal.ReadInt32(pC);
            _output.WriteLine($"Result: {a} + {b} = {c}");
            Assert.Equal(expected, c);
        }
        finally
        {
            if (pA != IntPtr.Zero) Marshal.FreeHGlobal(pA);
            if (pB != IntPtr.Zero) Marshal.FreeHGlobal(pB);
            if (pC != IntPtr.Zero) Marshal.FreeHGlobal(pC);
            if (pArgA != IntPtr.Zero) Marshal.FreeHGlobal(pArgA);
            if (pArgB != IntPtr.Zero) Marshal.FreeHGlobal(pArgB);
            if (pArgC != IntPtr.Zero) Marshal.FreeHGlobal(pArgC);

            if (dA != IntPtr.Zero) CudaDriverApi.cuMemFree_v2(dA);
            if (dB != IntPtr.Zero) CudaDriverApi.cuMemFree_v2(dB);
            if (dC != IntPtr.Zero) CudaDriverApi.cuMemFree_v2(dC);
        }
    }
}
