using System.Buffers;

namespace Kassyi.GSharp.Stdlib.Math.BigInt;

public static partial class GpuBigIntDivRem
{
    public static void ModPow(GpuUIntBigInteger baseVal, GpuUIntBigInteger exp, GpuUIntBigInteger mod, GpuUIntBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(baseVal, mod, resultBuffer);

        var n = baseVal.NumWords;
        var expWords = exp.NumWords;

        using var mod2N = new GpuUIntBigInteger(baseVal.Workspace, 2 * n);
        mod2N.CopyFrom(mod, numWords: n);

        using var tempResult = new GpuUIntBigInteger(baseVal.Workspace, 2 * n);
        tempResult.Clear();
        tempResult.Workspace.Provider.CopyHostToDevice(tempResult.DevicePointer, [1u], 1, tempResult.Workspace.Stream);

        using var tempBase = new GpuUIntBigInteger(baseVal.Workspace, 2 * n);
        tempBase.CopyFrom(baseVal, numWords: n);

        // Initial reduction of base
        using var tempBaseQ = new GpuUIntBigInteger(baseVal.Workspace, 2 * n);
        using var tempBaseR = new GpuUIntBigInteger(baseVal.Workspace, 2 * n);
        DivRem(tempBase, mod2N, tempBaseQ, tempBaseR);
        tempBase.CopyFrom(tempBaseR);

        using var tempMul = new GpuUIntBigInteger(baseVal.Workspace, 4 * n);
        using var tempQ = new GpuUIntBigInteger(baseVal.Workspace, 4 * n);
        using var tempR = new GpuUIntBigInteger(baseVal.Workspace, 4 * n);

        using var mod4N = new GpuUIntBigInteger(baseVal.Workspace, 4 * n);
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
