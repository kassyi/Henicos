namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

/// <summary>
/// Encapsulates the host-orchestrated square-and-multiply algorithm loop for modular exponentiation,
/// using an optimized NTT-based Barrett Reduction by computing the modulus reciprocal exactly once.
/// </summary>
internal static class ModPowNttHostController
{
    public static void Execute(GpuBigUInt baseVal, GpuBigUInt exp, GpuBigUInt mod, GpuBigUInt resultBuffer)
    {
        var workspace = baseVal.Workspace;
        var n = mod.NumWords;

        // 1. Precompute Reciprocal of Modulus (D)
        var bActualBits = GpuBigIntDivRem.GetActualBits(mod);
        if (bActualBits == 0)
        {
            throw new DivideByZeroException();
        }

        var lz = n * 32 - bActualBits;
        using var bNorm = new GpuBigUInt(workspace, n);
        GpuBigIntMath.ShiftLeft(mod, lz, bNorm);

        // Maximum size of operand A during ModPow is (2 * n) words (product of two n-word numbers).
        var maxABits = 2 * n * 32;
        var aActualWords = (maxABits + 31) / 32;
        var marginBits = 512;
        var marginWords = marginBits / 32;
        var targetBits = n * 32 + aActualWords * 32 + marginBits;
        var targetWords = (targetBits + 31) / 32;

        using var reciprocalNorm = new GpuBigUInt(workspace, targetWords);
        reciprocalNorm.Clear();
        GpuBigIntDivRem.Reciprocal(bNorm, reciprocalNorm, targetBits, marginWords);

        using var reciprocalOfB = new GpuBigUInt(workspace, targetWords);
        GpuBigIntMath.ShiftLeft(reciprocalNorm, lz, reciprocalOfB);

        // 2. Square and Multiply MegaKernel (Zero-Sync GPU Offloading) - Chunked for TDR avoidance
        if (workspace.Provider is GSharpKernelProvider kernelProvider)
        {
            kernelProvider.KernelAdvancedMath.ExecuteModPowNttMegaKernel(
                workspace,
                baseVal, resultBuffer, mod, reciprocalOfB, exp,
                n, targetBits,
                gridDim: 1, blockDim: 256);
        }
        else
        {
            throw new NotSupportedException("ModPowNttMegaKernel is only supported on KernelAdvancedMathHandler.");
        }
    }
}
