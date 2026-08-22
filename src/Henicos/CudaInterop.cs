using System.Runtime.InteropServices;

namespace Henicos;


// ReSharper disable UnusedMember.Global

public static class CudaInterop
{
    private const string DllName = "Kassyi.Math.GpuBigInt.Cuda.dll";

    [DllImport(DllName, EntryPoint = "GpuBigInt_Init", CallingConvention = CallingConvention.Cdecl)]
    public static extern bool InitializeGPU(int maxWords);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool GpuBigInt_DynamicFermatLoop(int e, uint[] outData, JitLaunchCallback jitLaunch);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool GpuBigInt_FermatPRPTest(int e, uint[] resultData);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool GpuBigInt_StaticFermatLoop(int e, uint[] resultData);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool GpuBigInt_ExecuteIL(int e, string ilScript, uint[] resultData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void JitLaunchCallback(
        IntPtr t, IntPtr q, IntPtr r, IntPtr maskR, IntPtr tmp,
        int numWords, int eVal, IntPtr carryBuffer,
        int m, IntPtr nttBuf1, IntPtr nttBuf2, IntPtr nttTmp,
        IntPtr twiddle1Fwd, IntPtr twiddle2Fwd,
        IntPtr twiddle1Inv, IntPtr twiddle2Inv,
        int gridDim, int blockDim
    );

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool GpuBigInt_RunJitFermatLoop(int e, uint[] resultData, JitLaunchCallback callback);

    [DllImport(DllName, EntryPoint = "GpuBigInt_Cleanup", CallingConvention = CallingConvention.Cdecl)]
    public static extern void CleanupGPU();

    static CudaInterop()
    {
        var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
        if (!string.IsNullOrEmpty(cudaPath))
        {
            var cudaBinX64 = Path.Combine(cudaPath, "bin", "x64");
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (pathEnv != null && !pathEnv.Contains(cudaBinX64))
            {
                Environment.SetEnvironmentVariable("PATH", pathEnv + ";" + cudaBinX64);
            }
        }

        NativeLibrary.SetDllImportResolver(typeof(CudaInterop).Assembly, (libraryName, assembly, searchPath) =>
        {
            if (libraryName == DllName)
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, libraryName);
                if (File.Exists(path))
                {
                    return NativeLibrary.Load(path, assembly, searchPath);
                }
            }
            return IntPtr.Zero;
        });
    }
}
