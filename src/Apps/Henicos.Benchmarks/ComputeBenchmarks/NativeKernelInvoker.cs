using System.Runtime.InteropServices;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Interop;

namespace Henicos.Benchmarks.ComputeBenchmarks;

public static unsafe class NativeKernelInvoker
{
    private static readonly Lazy<byte[]> s_fatbinSgemm = new(() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "sgemm_kernel.fatbin")));
    private static readonly Lazy<byte[]> s_fatbinSha256 = new(() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "sha256_kernel.fatbin")));
    private static readonly Lazy<byte[]> s_fatbinBlackScholes = new(() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "blackscholes_kernel.fatbin")));
    private static readonly Lazy<byte[]> s_fatbinReduction = new(() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "reduction_kernel.fatbin")));

    public static IntPtr FunctionSgemm => GpuKernelLoader.LoadAotKernel(s_fatbinSgemm.Value, "SgemmKernel_Native");
    public static IntPtr FunctionSha256 => GpuKernelLoader.LoadAotKernel(s_fatbinSha256.Value, "Sha256Kernel_Native");
    public static IntPtr FunctionBlackScholes => GpuKernelLoader.LoadAotKernel(s_fatbinBlackScholes.Value, "BlackScholesKernel_Native");
    public static IntPtr FunctionReduction => GpuKernelLoader.LoadAotKernel(s_fatbinReduction.Value, "ReductionKernel_Native");

    public static void LaunchSgemm(
        float[] a, float[] b, float[] c, int n,
        uint gridDimX, uint gridDimY, uint blockDimX, uint blockDimY)
    {
        if (a.Length == 0 || b.Length == 0 || c.Length == 0)
        {
            return;
        }

        using var scope = new GpuKernelLoader.CudaContextScope();
        IntPtr kernel = FunctionSgemm;

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
        IntPtr kernel = FunctionSha256;

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


    [StructLayout(LayoutKind.Sequential)]
    public struct BlackScholesNativeContext
    {
        public IntPtr S;
        public IntPtr X;
        public IntPtr T;
        public float R;
        public float V;
        public IntPtr CallResult;
        public IntPtr PutResult;
        public int N;
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
        IntPtr kernel = FunctionBlackScholes;

        using var d_s = new GpuBuffer<float>(s.Length);
        d_s.CopyToDevice(s);
        using var d_x = new GpuBuffer<float>(x.Length);
        d_x.CopyToDevice(x);
        using var d_t = new GpuBuffer<float>(t.Length);
        d_t.CopyToDevice(t);
        using var d_call = new GpuBuffer<float>(callResult.Length);
        using var d_put = new GpuBuffer<float>(putResult.Length);

        var ctx = new BlackScholesNativeContext
        {
            S = d_s.DevicePointer,
            X = d_x.DevicePointer,
            T = d_t.DevicePointer,
            R = r,
            V = v,
            CallResult = d_call.DevicePointer,
            PutResult = d_put.DevicePointer,
            N = n
        };

        void** kernelParams = stackalloc void*[1];
        kernelParams[0] = &ctx;

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
        IntPtr kernel = FunctionReduction;

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

}
