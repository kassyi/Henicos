using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime;

namespace Henicos.PiBenchmark.ComputeBenchmarks;

public static unsafe class NativeKernelInvoker
{
    private static IntPtr s_hfuncSgemm;
    private static IntPtr s_hfuncSha256;
    private static IntPtr s_hfuncBlackScholes;
    private static IntPtr s_hfuncReduction;

    private static readonly Lock s_lock = new();

    public static void LaunchSgemm(
        float[] a, float[] b, float[] c, int n,
        uint gridDimX, uint gridDimY, uint blockDimX, uint blockDimY)
    {
        if (a.Length == 0 || b.Length == 0 || c.Length == 0)
        {
            return;
        }

        using var scope = new GpuKernelLoader.CudaContextScope();
        using var dA = new GpuBuffer<float>(a.Length);
        using var dB = new GpuBuffer<float>(b.Length);
        using var dC = new GpuBuffer<float>(c.Length);

        dA.CopyToDevice(a);
        dB.CopyToDevice(b);

        LaunchSgemm_Device(dA.DevicePointer, dB.DevicePointer, dC.DevicePointer, n, gridDimX, gridDimY, blockDimX,
            blockDimY);

        dC.CopyToHost(c);
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
        using var dIn = new GpuBuffer<byte>(input.Length);
        using var dOut = new GpuBuffer<uint>(output.Length);

        dIn.CopyToDevice(input);

        LaunchSha256_Device(dIn.DevicePointer, dOut.DevicePointer, numMessages, gridDimX, blockDimX);

        dOut.CopyToHost(output);
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
        using var dS = new GpuBuffer<float>(s.Length);
        using var dX = new GpuBuffer<float>(x.Length);
        using var dT = new GpuBuffer<float>(t.Length);
        using var dCall = new GpuBuffer<float>(callResult.Length);
        using var dPut = new GpuBuffer<float>(putResult.Length);

        dS.CopyToDevice(s);
        dX.CopyToDevice(x);
        dT.CopyToDevice(t);

        LaunchBlackScholes_Device(dS.DevicePointer, dX.DevicePointer, dT.DevicePointer, r, v, dCall.DevicePointer,
            dPut.DevicePointer, n, gridDimX, blockDimX);

        dCall.CopyToHost(callResult);
        dPut.CopyToHost(putResult);
    }

    public static void LaunchReduction(
        float[] input, float[] output, int n,
        uint gridDimX, uint blockDimX)
    {
        if (input.Length == 0 || output.Length == 0)
        {
            return;
        }

        using var scope = new GpuKernelLoader.CudaContextScope();
        using var dIn = new GpuBuffer<float>(input.Length);
        using var dOut = new GpuBuffer<float>(output.Length);

        dIn.CopyToDevice(input);

        LaunchReduction_Device(dIn.DevicePointer, dOut.DevicePointer, n, gridDimX, blockDimX);

        dOut.CopyToHost(output);
    }

    public static void LaunchSgemm_Device(
        IntPtr dA, IntPtr dB, IntPtr dC, int n,
        uint gridDimX, uint gridDimY, uint blockDimX, uint blockDimY)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        EnsureLoaded(ref s_hfuncSgemm, "sgemm_kernel.fatbin", "SgemmKernel_Native");

        void** kernelParams = stackalloc void*[4];
        kernelParams[0] = &dA;
        kernelParams[1] = &dB;
        kernelParams[2] = &dC;
        kernelParams[3] = &n;

        CudaException.ThrowIfError(CudaDriverApi.cuLaunchKernel(
            s_hfuncSgemm,
            gridDimX, gridDimY, 1,
            blockDimX, blockDimY, 1,
            0, IntPtr.Zero, kernelParams, IntPtr.Zero));
        CudaException.ThrowIfError(CudaDriverApi.cuCtxSynchronize());
    }

    public static void LaunchSha256_Device(
        IntPtr dIn, IntPtr dOut, int numMessages,
        uint gridDimX, uint blockDimX)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        EnsureLoaded(ref s_hfuncSha256, "sha256_kernel.fatbin", "Sha256Kernel_Native");

        void** kernelParams = stackalloc void*[3];
        kernelParams[0] = &dIn;
        kernelParams[1] = &dOut;
        kernelParams[2] = &numMessages;

        CudaException.ThrowIfError(CudaDriverApi.cuLaunchKernel(
            s_hfuncSha256,
            gridDimX, 1, 1,
            blockDimX, 1, 1,
            0, IntPtr.Zero, kernelParams, IntPtr.Zero));
        CudaException.ThrowIfError(CudaDriverApi.cuCtxSynchronize());
    }

    public static void LaunchBlackScholes_Device(
        IntPtr dS, IntPtr dX, IntPtr dT, float r, float v,
        IntPtr dCall, IntPtr dPut, int n,
        uint gridDimX, uint blockDimX)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        EnsureLoaded(ref s_hfuncBlackScholes, "blackscholes_kernel.fatbin", "BlackScholesKernel_Native");

        void** kernelParams = stackalloc void*[8];
        kernelParams[0] = &dS;
        kernelParams[1] = &dX;
        kernelParams[2] = &dT;
        kernelParams[3] = &r;
        kernelParams[4] = &v;
        kernelParams[5] = &dCall;
        kernelParams[6] = &dPut;
        kernelParams[7] = &n;

        CudaException.ThrowIfError(CudaDriverApi.cuLaunchKernel(
            s_hfuncBlackScholes,
            gridDimX, 1, 1,
            blockDimX, 1, 1,
            0, IntPtr.Zero, kernelParams, IntPtr.Zero));
        CudaException.ThrowIfError(CudaDriverApi.cuCtxSynchronize());
    }

    public static void LaunchReduction_Device(
        IntPtr dIn, IntPtr dOut, int n,
        uint gridDimX, uint blockDimX)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        EnsureLoaded(ref s_hfuncReduction, "reduction_kernel.fatbin", "ReductionKernel_Native");

        void** kernelParams = stackalloc void*[3];
        kernelParams[0] = &dIn;
        kernelParams[1] = &dOut;
        kernelParams[2] = &n;

        CudaException.ThrowIfError(CudaDriverApi.cuLaunchKernel(
            s_hfuncReduction,
            gridDimX, 1, 1,
            blockDimX, 1, 1,
            0, IntPtr.Zero, kernelParams, IntPtr.Zero));
        CudaException.ThrowIfError(CudaDriverApi.cuCtxSynchronize());
    }

    private static void EnsureLoaded(ref IntPtr hfunc, string fatbinFileName, string kernelName)
    {
        if (hfunc != IntPtr.Zero)
        {
            return;
        }

        lock (s_lock)
        {
            if (hfunc != IntPtr.Zero)
            {
                return;
            }

            var path = Path.Combine(AppContext.BaseDirectory, fatbinFileName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Native fatbin '{fatbinFileName}' not found at '{path}'.");
            }

            var fatbinBytes = File.ReadAllBytes(path);
            hfunc = GpuKernelLoader.LoadAotKernel(fatbinBytes, kernelName);
        }
    }
}
