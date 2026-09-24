using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Interop;

namespace Henicos.Benchmarks.ComputeBenchmarks;

public static unsafe class NativeKernelInvoker
{
    private static readonly Lazy<IntPtr> s_hfuncSgemm = new(() => LoadKernel("sgemm_kernel.fatbin", "SgemmKernel_Native"));
    private static readonly Lazy<IntPtr> s_hfuncSha256 = new(() => LoadKernel("sha256_kernel.fatbin", "Sha256Kernel_Native"));
    private static readonly Lazy<IntPtr> s_hfuncBlackScholes = new(() => LoadKernel("blackscholes_kernel.fatbin", "BlackScholesKernel_Native"));
    private static readonly Lazy<IntPtr> s_hfuncReduction = new(() => LoadKernel("reduction_kernel.fatbin", "ReductionKernel_Native"));

    public static void LaunchSgemm(
        float[] a, float[] b, float[] c, int n,
        uint gridDimX, uint gridDimY, uint blockDimX, uint blockDimY)
    {
        if (a.Length == 0 || b.Length == 0 || c.Length == 0)
        {
            return;
        }

        using var scope = new GpuKernelLoader.CudaContextScope();
        IntPtr kernel = s_hfuncSgemm.Value;

        using var d_a = new GpuBuffer<float>(a.Length);
        d_a.CopyToDevice(a);
        using var d_b = new GpuBuffer<float>(b.Length);
        d_b.CopyToDevice(b);
        using var d_c = new GpuBuffer<float>(c.Length);

        IntPtr pa = d_a.DevicePointer;
        IntPtr pb = d_b.DevicePointer;
        IntPtr pc = d_c.DevicePointer;

        void** kernelParams = stackalloc void*[4];
        kernelParams[0] = &pa;
        kernelParams[1] = &pb;
        kernelParams[2] = &pc;
        kernelParams[3] = &n;

        CudaException.ThrowIfError(CudaDriverApi.cuLaunchKernel(
            kernel,
            gridDimX, gridDimY, 1,
            blockDimX, blockDimY, 1,
            0, IntPtr.Zero, kernelParams, IntPtr.Zero));

        CudaException.ThrowIfError(CudaDriverApi.cuCtxSynchronize());
        d_c.CopyToHost(c);
    }

    public static void LaunchSha256(
        byte[] input, uint[] output, int numMessages,
        uint gridDimX, uint blockDimX)
    {
        if (input.Length == 0 || output.Length == 0)
        {
            return;
        }

        using var scope = new GpuKernelLoader.CudaContextScope();
        IntPtr kernel = s_hfuncSha256.Value;

        using var d_in = new GpuBuffer<byte>(input.Length);
        d_in.CopyToDevice(input);
        using var d_out = new GpuBuffer<uint>(output.Length);

        IntPtr pin = d_in.DevicePointer;
        IntPtr pout = d_out.DevicePointer;

        void** kernelParams = stackalloc void*[3];
        kernelParams[0] = &pin;
        kernelParams[1] = &pout;
        kernelParams[2] = &numMessages;

        CudaException.ThrowIfError(CudaDriverApi.cuLaunchKernel(
            kernel,
            gridDimX, 1, 1,
            blockDimX, 1, 1,
            0, IntPtr.Zero, kernelParams, IntPtr.Zero));

        CudaException.ThrowIfError(CudaDriverApi.cuCtxSynchronize());
        d_out.CopyToHost(output);
    }

    public static void LaunchBlackScholes(
        float[] s, float[] x, float[] t, float r, float v,
        float[] callResult, float[] putResult, int n,
        uint gridDimX, uint blockDimX)
    {
        if (s.Length == 0)
        {
            return;
        }

        using var scope = new GpuKernelLoader.CudaContextScope();
        IntPtr kernel = s_hfuncBlackScholes.Value;

        using var d_s = new GpuBuffer<float>(s.Length);
        d_s.CopyToDevice(s);
        using var d_x = new GpuBuffer<float>(x.Length);
        d_x.CopyToDevice(x);
        using var d_t = new GpuBuffer<float>(t.Length);
        d_t.CopyToDevice(t);
        using var d_call = new GpuBuffer<float>(callResult.Length);
        using var d_put = new GpuBuffer<float>(putResult.Length);

        IntPtr ps = d_s.DevicePointer;
        IntPtr px = d_x.DevicePointer;
        IntPtr pt = d_t.DevicePointer;
        IntPtr pcall = d_call.DevicePointer;
        IntPtr pput = d_put.DevicePointer;

        void** kernelParams = stackalloc void*[8];
        kernelParams[0] = &ps;
        kernelParams[1] = &px;
        kernelParams[2] = &pt;
        kernelParams[3] = &r;
        kernelParams[4] = &v;
        kernelParams[5] = &pcall;
        kernelParams[6] = &pput;
        kernelParams[7] = &n;

        CudaException.ThrowIfError(CudaDriverApi.cuLaunchKernel(
            kernel,
            gridDimX, 1, 1,
            blockDimX, 1, 1,
            0, IntPtr.Zero, kernelParams, IntPtr.Zero));

        CudaException.ThrowIfError(CudaDriverApi.cuCtxSynchronize());
        d_call.CopyToHost(callResult);
        d_put.CopyToHost(putResult);
    }

    public static void LaunchReduction(
        Float4[] input, float[] output, int n,
        uint gridDimX, uint blockDimX)
    {
        if (input.Length == 0 || output.Length == 0)
        {
            return;
        }

        using var scope = new GpuKernelLoader.CudaContextScope();
        IntPtr kernel = s_hfuncReduction.Value;

        using var d_in = new GpuBuffer<Float4>(input.Length);
        d_in.CopyToDevice(input);
        using var d_out = new GpuBuffer<float>(output.Length);

        IntPtr pin = d_in.DevicePointer;
        IntPtr pout = d_out.DevicePointer;

        void** kernelParams = stackalloc void*[3];
        kernelParams[0] = &pin;
        kernelParams[1] = &pout;
        kernelParams[2] = &n;

        CudaException.ThrowIfError(CudaDriverApi.cuLaunchKernel(
            kernel,
            gridDimX, 1, 1,
            blockDimX, 1, 1,
            0, IntPtr.Zero, kernelParams, IntPtr.Zero));

        CudaException.ThrowIfError(CudaDriverApi.cuCtxSynchronize());
        d_out.CopyToHost(output);
    }

    private static IntPtr LoadKernel(string fatbinFileName, string kernelName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fatbinFileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Native fatbin '{fatbinFileName}' not found at '{path}'.");
        }
        var fatbinBytes = File.ReadAllBytes(path);
        return GpuKernelLoader.LoadAotKernel(fatbinBytes, kernelName);
    }
}
