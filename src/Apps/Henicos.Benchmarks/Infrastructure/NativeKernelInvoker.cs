using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;

namespace Henicos.Benchmarks.Infrastructure;

public static unsafe class NativeKernelInvoker
{
    private static IntPtr s_hfuncSgemm;
    private static IntPtr s_hfuncSha256;
    private static IntPtr s_hfuncBlackScholes;
    private static IntPtr s_hfuncReduction;
    private static IntPtr s_hfuncEmpty;
    private static IntPtr s_hfuncMemoryCopy;
    private static IntPtr s_hfuncMathFma;
    private static IntPtr s_hfuncBranchDivergence;
    private static IntPtr s_hfuncMandelbrot;
    private static IntPtr s_hfuncTranspose;

    private static readonly Lock s_lock = new();

    public static void LaunchSgemm_Device(
        IntPtr dA, IntPtr dB, IntPtr dC, int n,
        uint gridDimX, uint gridDimY, uint blockDimX, uint blockDimY)
    {
        void** kernelParams = stackalloc void*[4];
        kernelParams[0] = &dA;
        kernelParams[1] = &dB;
        kernelParams[2] = &dC;
        kernelParams[3] = &n;

        LaunchKernel(ref s_hfuncSgemm, "sgemm_kernel.fatbin", "SgemmKernel_Native", gridDimX, gridDimY, blockDimX, blockDimY, kernelParams);
    }

    public static void LaunchSha256_Device(
        IntPtr dIn, IntPtr dOut, int numMessages,
        uint gridDimX, uint blockDimX)
    {
        void** kernelParams = stackalloc void*[3];
        kernelParams[0] = &dIn;
        kernelParams[1] = &dOut;
        kernelParams[2] = &numMessages;

        LaunchKernel(ref s_hfuncSha256, "sha256_kernel.fatbin", "Sha256Kernel_Native", gridDimX, 1, blockDimX, 1, kernelParams);
    }

    public static void LaunchBlackScholes_Device(
        IntPtr dS, IntPtr dX, IntPtr dT, float r, float v,
        IntPtr dCall, IntPtr dPut, int n,
        uint gridDimX, uint blockDimX)
    {
        void** kernelParams = stackalloc void*[8];
        kernelParams[0] = &dS;
        kernelParams[1] = &dX;
        kernelParams[2] = &dT;
        kernelParams[3] = &r;
        kernelParams[4] = &v;
        kernelParams[5] = &dCall;
        kernelParams[6] = &dPut;
        kernelParams[7] = &n;

        LaunchKernel(ref s_hfuncBlackScholes, "blackscholes_kernel.fatbin", "BlackScholesKernel_Native", gridDimX, 1, blockDimX, 1, kernelParams);
    }

    public static void LaunchReduction_Device(
        IntPtr dIn, IntPtr dOut, int n,
        uint gridDimX, uint blockDimX)
    {
        void** kernelParams = stackalloc void*[3];
        kernelParams[0] = &dIn;
        kernelParams[1] = &dOut;
        kernelParams[2] = &n;

        LaunchKernel(ref s_hfuncReduction, "reduction_kernel.fatbin", "ReductionKernel_Native", gridDimX, 1, blockDimX, 1, kernelParams);
    }

    public static void LaunchEmpty_Device(
        uint gridDimX, uint gridDimY, uint blockDimX, uint blockDimY)
    {
        LaunchKernel(ref s_hfuncEmpty, "empty_kernel.fatbin", "EmptyKernel_Native", gridDimX, gridDimY, blockDimX, blockDimY, null);
    }

    public static void LaunchMemoryCopy_Device(
        IntPtr dSrc, IntPtr dDst, int n,
        uint gridDimX, uint blockDimX)
    {
        void** kernelParams = stackalloc void*[3];
        kernelParams[0] = &dSrc;
        kernelParams[1] = &dDst;
        kernelParams[2] = &n;

        LaunchKernel(ref s_hfuncMemoryCopy, "memory_copy_kernel.fatbin", "MemoryCopyKernel_Native", gridDimX, 1, blockDimX, 1, kernelParams);
    }

    public static void LaunchMathFma_Device(
        IntPtr dData, int n, int iterations,
        uint gridDimX, uint blockDimX)
    {
        void** kernelParams = stackalloc void*[3];
        kernelParams[0] = &dData;
        kernelParams[1] = &n;
        kernelParams[2] = &iterations;

        LaunchKernel(ref s_hfuncMathFma, "math_fma_kernel.fatbin", "MathFmaKernel_Native", gridDimX, 1, blockDimX, 1, kernelParams);
    }

    public static void LaunchBranchDivergence_Device(
        IntPtr dData, int n,
        uint gridDimX, uint blockDimX)
    {
        void** kernelParams = stackalloc void*[2];
        kernelParams[0] = &dData;
        kernelParams[1] = &n;

        LaunchKernel(ref s_hfuncBranchDivergence, "branch_divergence_kernel.fatbin", "BranchDivergenceKernel_Native", gridDimX, 1, blockDimX, 1, kernelParams);
    }

    public static void LaunchMandelbrot_Device(
        IntPtr dOutput, int width, int height, int maxIter,
        float xMin, float xMax, float yMin, float yMax,
        uint gridDimX, uint gridDimY, uint blockDimX, uint blockDimY)
    {
        void** kernelParams = stackalloc void*[8];
        kernelParams[0] = &dOutput;
        kernelParams[1] = &width;
        kernelParams[2] = &height;
        kernelParams[3] = &maxIter;
        kernelParams[4] = &xMin;
        kernelParams[5] = &xMax;
        kernelParams[6] = &yMin;
        kernelParams[7] = &yMax;

        LaunchKernel(ref s_hfuncMandelbrot, "mandelbrot_kernel.fatbin", "MandelbrotKernel_Native", gridDimX, gridDimY, blockDimX, blockDimY, kernelParams);
    }

    public static void LaunchTranspose_Device(
        IntPtr dInput, IntPtr dOutput, int width, int height,
        uint gridDimX, uint gridDimY, uint blockDimX, uint blockDimY)
    {
        void** kernelParams = stackalloc void*[4];
        kernelParams[0] = &dInput;
        kernelParams[1] = &dOutput;
        kernelParams[2] = &width;
        kernelParams[3] = &height;

        LaunchKernel(ref s_hfuncTranspose, "transpose_kernel.fatbin", "TransposeKernel_Native", gridDimX, gridDimY, blockDimX, blockDimY, kernelParams);
    }

    private static void LaunchKernel(
        ref IntPtr hfunc, string fatbinFileName, string kernelName,
        uint gridDimX, uint gridDimY, uint blockDimX, uint blockDimY,
        void** kernelParams)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        EnsureLoaded(ref hfunc, fatbinFileName, kernelName);

        CudaException.ThrowIfError(CudaDriverApi.cuLaunchKernel(
            hfunc,
            gridDimX, gridDimY, 1,
            blockDimX, blockDimY, 1,
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
