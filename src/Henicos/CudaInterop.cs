using System;
using System.Runtime.InteropServices;

namespace Henicos;

public static class CudaInterop
{
    private const string DllName = "Kassyi.Math.GpuBigInt.Cuda.dll";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool InitializeGPU(int E);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool MultiplySquare(uint[] data, int numWords);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void CleanupGPU();
}
