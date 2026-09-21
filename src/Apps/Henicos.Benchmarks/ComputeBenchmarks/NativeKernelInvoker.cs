using Kassyi.GSharp.Runtime.Interop;

namespace Henicos.Benchmarks.ComputeBenchmarks;

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
        EnsureLoaded(ref s_hfuncSgemm, "sgemm_kernel.fatbin", "SgemmKernel_Native");

        IntPtr d_a = IntPtr.Zero, d_b = IntPtr.Zero, d_c = IntPtr.Zero;
        try
        {
            int aSize = a.Length * sizeof(float);
            int bSize = b.Length * sizeof(float);
            int cSize = c.Length * sizeof(float);

            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_a, aSize));
            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_b, bSize));
            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_c, cSize));

            fixed (float* pa = a, pb = b, pc = c)
            {
                CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(d_a, (IntPtr)pa, aSize));
                CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(d_b, (IntPtr)pb, bSize));

                void** kernelParams = stackalloc void*[4];
                kernelParams[0] = &d_a;
                kernelParams[1] = &d_b;
                kernelParams[2] = &d_c;
                kernelParams[3] = &n;

                CudaException.ThrowIfError(CudaDriverApi.cuLaunchKernel(
                    s_hfuncSgemm,
                    gridDimX, gridDimY, 1,
                    blockDimX, blockDimY, 1,
                    0, IntPtr.Zero, kernelParams, IntPtr.Zero));

                CudaException.ThrowIfError(CudaDriverApi.cuCtxSynchronize());
                CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2((IntPtr)pc, d_c, cSize));
            }
        }
        finally
        {
            if (d_a != IntPtr.Zero)
            {
                CudaDriverApi.cuMemFree_v2(d_a);
            }

            if (d_b != IntPtr.Zero)
            {
                CudaDriverApi.cuMemFree_v2(d_b);
            }

            if (d_c != IntPtr.Zero)
            {
                CudaDriverApi.cuMemFree_v2(d_c);
            }
        }
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
        EnsureLoaded(ref s_hfuncSha256, "sha256_kernel.fatbin", "Sha256Kernel_Native");

        IntPtr d_in = IntPtr.Zero, d_out = IntPtr.Zero;
        try
        {
            int inSize = input.Length * sizeof(byte);
            int outSize = output.Length * sizeof(uint);

            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_in, inSize));
            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_out, outSize));

            fixed (byte* pin = input)
            fixed (uint* pout = output)
            {
                CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(d_in, (IntPtr)pin, inSize));

                void** kernelParams = stackalloc void*[3];
                kernelParams[0] = &d_in;
                kernelParams[1] = &d_out;
                kernelParams[2] = &numMessages;

                CudaException.ThrowIfError(CudaDriverApi.cuLaunchKernel(
                    s_hfuncSha256,
                    gridDimX, 1, 1,
                    blockDimX, 1, 1,
                    0, IntPtr.Zero, kernelParams, IntPtr.Zero));

                CudaException.ThrowIfError(CudaDriverApi.cuCtxSynchronize());
                CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2((IntPtr)pout, d_out, outSize));
            }
        }
        finally
        {
            if (d_in != IntPtr.Zero)
            {
                CudaDriverApi.cuMemFree_v2(d_in);
            }

            if (d_out != IntPtr.Zero)
            {
                CudaDriverApi.cuMemFree_v2(d_out);
            }
        }
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
        EnsureLoaded(ref s_hfuncBlackScholes, "blackscholes_kernel.fatbin", "BlackScholesKernel_Native");

        IntPtr d_s = IntPtr.Zero, d_x = IntPtr.Zero, d_t = IntPtr.Zero, d_call = IntPtr.Zero, d_put = IntPtr.Zero;
        try
        {
            int floatSize = n * sizeof(float);

            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_s, floatSize));
            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_x, floatSize));
            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_t, floatSize));
            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_call, floatSize));
            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_put, floatSize));

            fixed (float* ps = s, px = x, pt = t, pcall = callResult, pput = putResult)
            {
                CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(d_s, (IntPtr)ps, floatSize));
                CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(d_x, (IntPtr)px, floatSize));
                CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(d_t, (IntPtr)pt, floatSize));

                void** kernelParams = stackalloc void*[8];
                kernelParams[0] = &d_s;
                kernelParams[1] = &d_x;
                kernelParams[2] = &d_t;
                kernelParams[3] = &r;
                kernelParams[4] = &v;
                kernelParams[5] = &d_call;
                kernelParams[6] = &d_put;
                kernelParams[7] = &n;

                CudaException.ThrowIfError(CudaDriverApi.cuLaunchKernel(
                    s_hfuncBlackScholes,
                    gridDimX, 1, 1,
                    blockDimX, 1, 1,
                    0, IntPtr.Zero, kernelParams, IntPtr.Zero));

                CudaException.ThrowIfError(CudaDriverApi.cuCtxSynchronize());
                CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2((IntPtr)pcall, d_call, floatSize));
                CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2((IntPtr)pput, d_put, floatSize));
            }
        }
        finally
        {
            if (d_s != IntPtr.Zero)
            {
                CudaDriverApi.cuMemFree_v2(d_s);
            }

            if (d_x != IntPtr.Zero)
            {
                CudaDriverApi.cuMemFree_v2(d_x);
            }

            if (d_t != IntPtr.Zero)
            {
                CudaDriverApi.cuMemFree_v2(d_t);
            }

            if (d_call != IntPtr.Zero)
            {
                CudaDriverApi.cuMemFree_v2(d_call);
            }

            if (d_put != IntPtr.Zero)
            {
                CudaDriverApi.cuMemFree_v2(d_put);
            }
        }
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
        EnsureLoaded(ref s_hfuncReduction, "reduction_kernel.fatbin", "ReductionKernel_Native");

        IntPtr d_in = IntPtr.Zero, d_out = IntPtr.Zero;
        try
        {
            int inSize = input.Length * sizeof(float);
            int outSize = output.Length * sizeof(float);

            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_in, inSize));
            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_out, outSize));

            fixed (float* pin = input, pout = output)
            {
                CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(d_in, (IntPtr)pin, inSize));

                void** kernelParams = stackalloc void*[3];
                kernelParams[0] = &d_in;
                kernelParams[1] = &d_out;
                kernelParams[2] = &n;

                CudaException.ThrowIfError(CudaDriverApi.cuLaunchKernel(
                    s_hfuncReduction,
                    gridDimX, 1, 1,
                    blockDimX, 1, 1,
                    0, IntPtr.Zero, kernelParams, IntPtr.Zero));

                CudaException.ThrowIfError(CudaDriverApi.cuCtxSynchronize());
                CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2((IntPtr)pout, d_out, outSize));
            }
        }
        finally
        {
            if (d_in != IntPtr.Zero)
            {
                CudaDriverApi.cuMemFree_v2(d_in);
            }

            if (d_out != IntPtr.Zero)
            {
                CudaDriverApi.cuMemFree_v2(d_out);
            }
        }
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
