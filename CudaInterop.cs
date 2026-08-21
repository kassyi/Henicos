using System;
using System.Runtime.InteropServices;

namespace Henicos;

public static class CudaInterop
{
    private const string DllName = "HenicosCuda";

    [DllImport("HenicosCuda", CallingConvention = CallingConvention.Cdecl)]
    public static extern bool InitializeGPU(int E);

    [DllImport("HenicosCuda", CallingConvention = CallingConvention.Cdecl)]
    public static extern bool MultiplySquare(uint[] data, int numWords);

    [DllImport("HenicosCuda", CallingConvention = CallingConvention.Cdecl)]
    public static extern bool RunFermatLoopV5(uint[] data, int numWords, int E);

    [DllImport("HenicosCuda", CallingConvention = CallingConvention.Cdecl)]
    public static extern void CleanupGPU();
}
