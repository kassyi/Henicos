using System.Buffers;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

/// <summary>
/// Encapsulates the host-orchestrated square-and-multiply algorithm loop for modular exponentiation.
/// Uses zero-sync Barrett reduction to execute entirely asynchronously without host-device synchronization.
/// </summary>
internal static class ModPowZeroSyncController
{
    public static void Execute(GpuBigUInt baseVal, GpuBigUInt exp, GpuBigUInt mod, GpuBigUInt resultBuffer)
    {
        var n = baseVal.NumWords;
        var expWords = exp.NumWords;
        using var mod2N = new GpuBigUInt(baseVal.Workspace, 2 * n);
        mod2N.Clear();
        mod2N.CopyFrom(mod, numWords: n);

        using var tempResult = new GpuBigUInt(baseVal.Workspace, 2 * n);
        tempResult.Clear();
        tempResult.Workspace.Provider.CopyHostToDevice(tempResult.DevicePointer, [1u], 1, tempResult.Workspace.Stream);

        using var tempBase = new GpuBigUInt(baseVal.Workspace, 2 * n);
        tempBase.Clear();
        tempBase.CopyFrom(baseVal, numWords: n);

        var modActualBits = GpuBigIntDivRem.GetActualBits(mod);
        if (modActualBits == 0)
        {
            throw new DivideByZeroException();
        }

        var lz = n * 32 - modActualBits;
        using var bNorm = new GpuBigUInt(baseVal.Workspace, n);
        GpuBigIntMath.ShiftLeft(mod, lz, bNorm);

        var marginBits = 512;
        var marginWords = marginBits / 32;
        var aActualWords = 4 * n; // Max size for tempMul
        var targetBits = aActualWords * 32 + marginBits;
        var targetWords = (targetBits + 31) / 32;

        using var reciprocalNorm = new GpuBigUInt(baseVal.Workspace, targetWords);
        reciprocalNorm.Clear();
        GpuBigIntDivRem.Reciprocal(bNorm, reciprocalNorm, targetBits, marginWords);

        using var reciprocalOfMod = new GpuBigUInt(baseVal.Workspace, targetWords);
        GpuBigIntMath.ShiftLeft(reciprocalNorm, lz, reciprocalOfMod);

        // Initial reduction of base using the precomputed reciprocal
        using var tempBaseQ = new GpuBigUInt(baseVal.Workspace, 4 * n);
        using var tempBaseR = new GpuBigUInt(baseVal.Workspace, 4 * n);
        using var extendedTempBase = new GpuBigUInt(baseVal.Workspace, 4 * n);
        extendedTempBase.Clear();
        extendedTempBase.CopyFrom(tempBase, numWords: 2 * n);
        GpuBigIntDivRem.ReduceZeroSync(extendedTempBase, mod, reciprocalOfMod, targetBits, aActualWords, tempBaseQ, tempBaseR);
        tempBase.CopyFrom(tempBaseR, numWords: 2 * n);

        using var tempMul = new GpuBigUInt(baseVal.Workspace, 4 * n);
        using var tempQ = new GpuBigUInt(baseVal.Workspace, 4 * n);
        using var tempR = new GpuBigUInt(baseVal.Workspace, 4 * n);

        var hostExp = ArrayPool<uint>.Shared.Rent(expWords);
        try
        {
            exp.Workspace.Provider.CopyDeviceToHost(hostExp, exp.DevicePointer, expWords, exp.Workspace.Stream);
            CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(exp.Workspace.Stream));

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
                        GpuBigIntDivRem.ReduceZeroSync(tempMul, mod, reciprocalOfMod, targetBits, aActualWords, tempQ, tempR);
                        tempResult.CopyFrom(tempR, numWords: 2 * n);
                    }

                    GpuBigIntMath.Mul(tempBase, tempBase, tempMul);
                    GpuBigIntDivRem.ReduceZeroSync(tempMul, mod, reciprocalOfMod, targetBits, aActualWords, tempQ, tempR);
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
