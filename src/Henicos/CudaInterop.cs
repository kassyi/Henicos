using System.Runtime.InteropServices;

namespace Henicos;

public static class CudaInterop
{
    private const string DllName = "Kassyi.Math.GpuBigInt.Cuda.dll";

    [DllImport(DllName, EntryPoint = "GpuBigInt_Init", CallingConvention = CallingConvention.Cdecl)]
    public static extern bool InitializeGPU(int maxWords);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool GpuBigInt_DynamicFermatLoop(int E, uint[] out_data, JitLaunchCallback jitLaunch);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool GpuBigInt_FermatPRPTest(int E, uint[] resultData);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool GpuBigInt_StaticFermatLoop(int E, uint[] resultData);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool GpuBigInt_ExecuteIL(int E, string ilScript, uint[] resultData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void JitLaunchCallback(
        IntPtr T, IntPtr Q, IntPtr R, IntPtr MaskR, IntPtr T_tmp,
        int numWords, int E_val, IntPtr carryBuffer,
        int M, IntPtr ntt_buf1, IntPtr ntt_buf2, IntPtr ntt_tmp,
        IntPtr twiddle1_fwd, IntPtr twiddle2_fwd,
        IntPtr twiddle1_inv, IntPtr twiddle2_inv,
        int gridDim, int blockDim
    );

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool GpuBigInt_RunJitFermatLoop(int E, uint[] resultData, JitLaunchCallback callback);

    [DllImport(DllName, EntryPoint = "GpuBigInt_Cleanup", CallingConvention = CallingConvention.Cdecl)]
    public static extern void CleanupGPU();

    static CudaInterop()
    {
        var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
        if (!string.IsNullOrEmpty(cudaPath))
        {
            var cudaBinX64 = Path.Combine(cudaPath, "bin", "x64");
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!pathEnv.Contains(cudaBinX64))
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
