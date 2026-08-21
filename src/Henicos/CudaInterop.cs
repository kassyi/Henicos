using System;
using System.Runtime.InteropServices;

namespace Henicos;

public static class CudaInterop
{
    private const string DllName = "Kassyi.Math.GpuBigInt.Cuda.dll";

    [DllImport(DllName, EntryPoint = "GpuBigInt_Init", CallingConvention = CallingConvention.Cdecl)]
    public static extern bool InitializeGPU(int maxWords);

    [DllImport(DllName, EntryPoint = "GpuBigInt_Square", CallingConvention = CallingConvention.Cdecl)]
    public static extern bool MultiplySquare(uint[] data, int numWords);

    [DllImport(DllName, EntryPoint = "GpuBigInt_Cleanup", CallingConvention = CallingConvention.Cdecl)]
    public static extern void CleanupGPU();
}
