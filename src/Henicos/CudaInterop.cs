using System;
using System.Runtime.InteropServices;

namespace Henicos;

public static class CudaInterop
{
    private const string DllName = "Kassyi.Math.GpuBigInt.Cuda.dll";

    [DllImport(DllName, EntryPoint = "GpuBigInt_Init", CallingConvention = CallingConvention.Cdecl)]
    public static extern bool InitializeGPU(int maxWords);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool GpuBigInt_FermatPRPTest(int E, uint[] resultData);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool GpuBigInt_ExecuteIL(int E, string ilScript, uint[] resultData);

    [DllImport(DllName, EntryPoint = "GpuBigInt_Cleanup", CallingConvention = CallingConvention.Cdecl)]
    public static extern void CleanupGPU();

    static CudaInterop()
    {
        string cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
        if (!string.IsNullOrEmpty(cudaPath))
        {
            string cudaBinX64 = System.IO.Path.Combine(cudaPath, "bin", "x64");
            string pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!pathEnv.Contains(cudaBinX64))
            {
                Environment.SetEnvironmentVariable("PATH", pathEnv + ";" + cudaBinX64);
            }
        }

        NativeLibrary.SetDllImportResolver(typeof(CudaInterop).Assembly, (libraryName, assembly, searchPath) =>
        {
            if (libraryName == DllName)
            {
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, libraryName);
                if (System.IO.File.Exists(path))
                {
                    return NativeLibrary.Load(path, assembly, searchPath);
                }
            }
            return IntPtr.Zero;
        });
    }
}
