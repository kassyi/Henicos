namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

public static partial class GpuBigIntDivRem
{
    /// <summary>
    /// Computes modular exponentiation ($(\text{baseVal}^{\text{exp}} \bmod \text{mod})$) on the GPU using binary exponentiation (square-and-multiply).
    /// </summary>
    /// <param name="baseVal">The base value.</param>
    /// <param name="exp">The exponent value.</param>
    /// <param name="mod">The modulus.</param>
    /// <param name="resultBuffer">The destination buffer in GPU device memory receiving the modular power.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="baseVal"/>, <paramref name="mod"/>, and <paramref name="resultBuffer"/> have mismatched word lengths.</exception>
    /// <exception cref="DivideByZeroException">Thrown when <paramref name="mod"/> is zero.</exception>
    public static void ModPow(GpuBigUInt baseVal, GpuBigUInt exp, GpuBigUInt mod, GpuBigUInt resultBuffer)
    {
        ThrowIfSizeMismatch(baseVal, mod, resultBuffer);

        var bitLength = baseVal.NumWords * 32;

        if (bitLength <= baseVal.Workspace.PolyAlgorithmThreshold)
        {
            // MegaKernel (NTT-Barrett) is highly optimized and avoids kernel launch overheads.
            // It runs entirely within registers up to ~1024 bits.
            ModPowNttHostController.Execute(baseVal, exp, mod, resultBuffer);
        }
        else
        {
            // For giant-scale numbers (>1024 bits), MegaKernel suffers catastrophic performance degradation
            // due to register spilling into local memory. We fall back to the Zero-Sync HostLoop which queues kernels asynchronously.
            ModPowZeroSyncController.Execute(baseVal, exp, mod, resultBuffer);
        }
    }
}
