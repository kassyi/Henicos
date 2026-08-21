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
}
