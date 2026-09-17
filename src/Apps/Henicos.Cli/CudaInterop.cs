using System.Runtime.InteropServices;

namespace Henicos.Cli;

/// <summary>P/Invoke bindings for executing legacy CUDA native tests in CLI benchmarks.</summary>
// ReSharper disable once UnusedMember.Global -- Public interop methods
public static class CudaInterop
{
    private const string DllName = "Kassyi.GSharp.GpuBigInt.Cuda.dll";

    [DllImport(DllName, EntryPoint = "GpuBigInt_Init", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr InitializeGPU(int maxWords);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool GpuBigInt_DynamicFermatLoop(IntPtr workspace, int e, uint[] outData, JitLaunchCallback jitLaunch);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool GpuBigInt_FermatPRPTest(IntPtr workspace, int e, uint[] resultData);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool GpuBigInt_StaticFermatLoop(IntPtr workspace, int e, uint[] resultData);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool GpuBigInt_ExecuteIL(IntPtr workspace, int e, string ilScript, uint[] resultData);

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
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool GpuBigInt_RunJitFermatLoop(IntPtr workspace, int e, uint[] resultData, JitLaunchCallback callback);

    [DllImport(DllName, EntryPoint = "GpuBigInt_Cleanup", CallingConvention = CallingConvention.Cdecl)]
    public static extern void CleanupGPU(IntPtr workspace);

    static CudaInterop()
    {
        var cudaBin = CudaEnvironment.GetCudaBinPath();
        if (!string.IsNullOrEmpty(cudaBin))
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (pathEnv != null && !pathEnv.Contains(cudaBin))
            {
                Environment.SetEnvironmentVariable("PATH", pathEnv + Path.PathSeparator + cudaBin);
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
