using System.Numerics;
using Kassyi.GSharp.Transpiler;
using Kassyi.GSharp;

namespace Henicos;

public static class ModularReductionGpu
{
    [GpuKernel]
    public static void StaticFermatLoop(
        uint[] t, uint[] q, uint[] r, uint[] maskR, uint[] tTmp,
        int numWords, int eVal, long[] carryBuffer, int m,
        uint[] nttBuf1, uint[] nttBuf2, uint[] nttTmp,
        uint[] twiddle1Fwd, uint[] twiddle2Fwd, uint[] twiddle1Inv, uint[] twiddle2Inv)
    {
        // For Phase 1 & 2 structural tests, we use simple while loops
        // which can be parsed by AstBuilder once CFG is supported.
        var iter = 0;
        while (iter < eVal)
        {
            Device_MultiplySquare(t, numWords, m, nttBuf1, nttBuf2, nttTmp, twiddle1Fwd, twiddle2Fwd, twiddle1Inv, twiddle2Inv);
            // In a real transpile, these intrinsic calls will map to Device_* CUDA calls
            // For now, they serve as placeholders to generate valid method calls
            Device_ShiftRight(t, 0, q, 0); // Dummy arguments
            iter = iter + 1;
        }
    }

    // Dummy intrinsics to allow compilation of the C# kernel
#pragma warning disable IDE0060 // Remove unused parameter
    // ReSharper disable UnusedParameter.Local
    [GpuKernel]
    private static void Device_MultiplySquare(uint[] a, int b, int c, uint[] d, uint[] e, uint[] f, uint[] g, uint[] h, uint[] i, uint[] j)
    {
    }
    [GpuKernel]
    private static void Device_ShiftRight(uint[] a, int b, uint[] c, int d)
    {
    }
    // ReSharper restore UnusedParameter.Local
#pragma warning restore IDE0060 // Remove unused parameter

    public static BigInteger FermatPrpTest(int e, BigInteger n)
    {
        var numWords = (2 * e + 1 + 31) / 32;
        var outNumWords = numWords * 2 + 16;

        if (!CudaInterop.InitializeGPU(e))
        {
            throw new Exception("GPU Initialization failed.");
        }

        try
        {
            var resultData = new uint[outNumWords];
            _ = resultData;

            // Task 1-3: Use TranspilerRunner instead of FermatJitBuilder
            var includePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\src\Kassyi.Math.GpuBigInt.Cuda"));
            _ = includePath;
            var cuCode = TranspilerRunner.GenerateKernelCode(typeof(ModularReductionGpu).Assembly.Location, nameof(StaticFermatLoop));

            // Temporarily skip JIT execution until CFG generates fully valid CUDA
            // using var jitModule = Kassyi.GSharp.JitCompiler.CompileAndLoad<CudaInterop.JitLaunchCallback>(cuCode, "Launch_JitFermatLoop", includePath);
            // var jitCallback = jitModule.Kernel;

            Console.WriteLine("[G#] Transpiled CUDA C++ Code:");
            Console.WriteLine(cuCode);

            // Dummy result
            return BigInteger.One;
        }
        finally
        {
            CudaInterop.CleanupGPU();
        }
    }
}
