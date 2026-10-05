using System.Buffers;

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

        var n = baseVal.NumWords;
        var expWords = exp.NumWords;

        using var mod2N = new GpuBigUInt(baseVal.Workspace, 2 * n);
        mod2N.CopyFrom(mod, numWords: n);

        using var tempResult = new GpuBigUInt(baseVal.Workspace, 2 * n);
        tempResult.Clear();
        tempResult.Workspace.Provider.CopyHostToDevice(tempResult.DevicePointer, [1u], 1, tempResult.Workspace.Stream);

        using var tempBase = new GpuBigUInt(baseVal.Workspace, 2 * n);
        tempBase.CopyFrom(baseVal, numWords: n);

        // Initial reduction of base
        using var tempBaseQ = new GpuBigUInt(baseVal.Workspace, 2 * n);
        using var tempBaseR = new GpuBigUInt(baseVal.Workspace, 2 * n);
        DivRem(tempBase, mod2N, tempBaseQ, tempBaseR);
        tempBase.CopyFrom(tempBaseR);

        using var tempMul = new GpuBigUInt(baseVal.Workspace, 4 * n);
        using var tempQ = new GpuBigUInt(baseVal.Workspace, 4 * n);
        using var tempR = new GpuBigUInt(baseVal.Workspace, 4 * n);

        using var mod4N = new GpuBigUInt(baseVal.Workspace, 4 * n);
        mod4N.CopyFrom(mod, numWords: n);

        var hostExp = ArrayPool<uint>.Shared.Rent(expWords);
        try
        {
            exp.Workspace.Provider.CopyDeviceToHost(hostExp, exp.DevicePointer, expWords, exp.Workspace.Stream);

            for (var i = 0; i < expWords; i++)
            {
                var e = hostExp[i];

                // Optimization: if remaining bits are 0, we can break
                if (e == 0)
                {
                    var allZero = true;
                    for (var j = i; j < expWords; j++)
                    {
                        if (hostExp[j] == 0)
                        {
                            continue;
                        }

                        allZero = false;
                        break;
                    }

                    if (allZero)
                    {
                        break;
                    }
                }

                for (var bit = 0; bit < 32; bit++)
                {
                    if ((e & 1) == 1)
                    {
                        GpuBigIntMath.Mul(tempResult, tempBase, tempMul);
                        DivRem(tempMul, mod4N, tempQ, tempR);
                        tempResult.CopyFrom(tempR, numWords: 2 * n);
                    }

                    GpuBigIntMath.Mul(tempBase, tempBase, tempMul);
                    DivRem(tempMul, mod4N, tempQ, tempR);
                    tempBase.CopyFrom(tempR, numWords: 2 * n);

                    e >>= 1;

                    // If e becomes 0 and there are no more words, we can break bit loop
                    if (e == 0 && i == expWords - 1)
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(hostExp);
        }

        resultBuffer.CopyFrom(tempResult, numWords: n);
    }
}
