// ReSharper disable All

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

using static NttPrimeArithmetic;

/// <summary>
/// Cooperative 2-prime NTT kernels, 2-prime CRT recombination, and block-carry scan helpers.
/// </summary>
/// <remarks>
/// Prime constants, Barrett reduction, and 3-prime Garner CRT constants live in <see cref="NttPrimeArithmetic"/>;
/// tableless 3-prime 35-bit row transforms live in <see cref="Ntt35TablelessKernels"/>; 35-bit unpack and Garner CRT3
/// carry resolution live in <see cref="Ntt35CarryKernels"/>; Goldilocks arithmetic lives in
/// <see cref="GoldilocksArithmetic"/>.
/// </remarks>
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntNttKernels
{
    /// <summary>The longest transform (in points) that runs as one shared-memory row of one block.</summary>
    /// <remarks>
    /// A longer transform is split into the rows and columns of a matrix. One row keeps the whole transform on one
    /// SM, so the limit is the length up to which that is the faster of the two.
    /// </remarks>
    internal const int SharedRowMaxTransformLength = 1024;

    /// <summary>The longest transform (in points) whose first pass reads the columns of the matrix in place.</summary>
    /// <remarks>
    /// A longer transform transposes the matrix into the scratch buffer first, several 32 x 32 tiles per block at
    /// once: its columns are far enough apart that reading them one element at a time is the slower of the two.
    /// </remarks>
    internal const int StridedColumnMaxTransformLength = 1 << 16;

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static NttGridContext CreateNttGridContext(int transformLength)
    {
        if (transformLength <= SharedRowMaxTransformLength)
        {
            return new NttGridContext { M = transformLength, N1 = transformLength, N2 = 1 };
        }

        int n2 = 1;
        while (n2 * n2 < transformLength)
        {
            n2 *= 2;
        }

        return new NttGridContext { M = transformLength, N1 = transformLength / n2, N2 = n2 };
    }

    [GpuDeviceFunction]
    public static void ExecuteNttPointwiseSquare(uint[] deviceData1, uint[] deviceData2, int transformLength)
    {
        Gpu.GridStrideLoop(transformLength, index =>
        {
            deviceData1[index] = ModMul1(deviceData1[index], deviceData1[index]);
            deviceData2[index] = ModMul2(deviceData2[index], deviceData2[index]);
        });
    }

    [GpuDeviceFunction]
    public static void ExecuteNttPointwiseMultiply(uint[] deviceData1A, uint[] deviceData2A, uint[] deviceData1B,
        uint[] deviceData2B, int transformLength)
    {
        Gpu.GridStrideLoop(transformLength, index =>
        {
            deviceData1A[index] = ModMul1(deviceData1A[index], deviceData1B[index]);
            deviceData2A[index] = ModMul2(deviceData2A[index], deviceData2B[index]);
        });
    }

    [GpuDeviceFunction]
    public static void ExecuteCrtComputeCoefficients([GpuReadOnly] uint[] deviceResult1,
        [GpuReadOnly] uint[] deviceResult2,
        uint[] uncarriedLow, uint[] uncarriedHigh,
        int transformLength)
    {
        var inverseN1 = ModPow((uint)transformLength, Prime1 - 2, Prime1, BarrettMagic1);
        var inverseN2 = ModPow((uint)transformLength, Prime2 - 2, Prime2, BarrettMagic2);

        Gpu.GridStrideLoop(transformLength, index =>
        {
            const ulong Modulus1 = Prime1;
            const ulong Modulus2 = Prime2;

            ulong value1 = ModMul1(deviceResult1[index], inverseN1);
            ulong value2 = ModMul2(deviceResult2[index], inverseN2);

            ulong v1Mod2 = value1 % Modulus2;
            ulong difference = value2 >= v1Mod2 ? value2 - v1Mod2 : value2 + Modulus2 - v1Mod2;

            const ulong Prime1InverseModPrime2 = Prime1InvModPrime2;
            var crtCoefficient = (difference * Prime1InverseModPrime2) % Modulus2;
            var recombinedValue = value1 + Modulus1 * crtCoefficient;

            uncarriedLow[index] = (uint)(recombinedValue & 0xFFFFFFFF);
            uncarriedHigh[index] = (uint)(recombinedValue >> 32);
        });
    }

    [GpuDeviceFunction]
    public static void ExecuteCrtComputeCoefficientsTransposed([GpuReadOnly] uint[] deviceResult1,
        [GpuReadOnly] uint[] deviceResult2,
        uint[] uncarriedLow, uint[] uncarriedHigh,
        int transformLength, int n1, int n2)
    {
        var inverseN1 = ModPow((uint)transformLength, Prime1 - 2, Prime1, BarrettMagic1);
        var inverseN2 = ModPow((uint)transformLength, Prime2 - 2, Prime2, BarrettMagic2);

        int log2N2 = 0;
        if (transformLength > 512)
        {
            while ((1 << log2N2) < n2)
            {
                log2N2++;
            }
        }

        int n2Mask = n2 - 1;

        Gpu.GridStrideLoop(transformLength, index =>
        {
            const ulong Modulus1 = Prime1;
            const ulong Modulus2 = Prime2;

            int srcIdx = transformLength > 512 ? ((index & n2Mask) * n1 + (index >> log2N2)) : index;
            ulong value1 = ModMul1(deviceResult1[srcIdx], inverseN1);
            ulong value2 = ModMul2(deviceResult2[srcIdx], inverseN2);

            ulong v1Mod2 = value1 % Modulus2;
            ulong difference = value2 >= v1Mod2 ? value2 - v1Mod2 : value2 + Modulus2 - v1Mod2;

            const ulong Prime1InverseModPrime2 = Prime1InvModPrime2;
            var crtCoefficient = (difference * Prime1InverseModPrime2) % Modulus2;
            var recombinedValue = value1 + Modulus1 * crtCoefficient;

            uncarriedLow[index] = (uint)(recombinedValue & 0xFFFFFFFF);
            uncarriedHigh[index] = (uint)(recombinedValue >> 32);
        });
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ComputeNttLocalSum(uint[] uncarriedLow, uint[] uncarriedHigh, int i, int maxWordCount,
        int transformLength)
    {
        ulong localSum = 0;
        if (i < maxWordCount)
        {
            // Each NTT coefficient c_j (64-bit) has weight 2^(16*j).
            // For word i (weight 2^(32*i)), relevant coefficients are j = 2*i + 1 down to 2*i - 3.
            Gpu.ForUnroll(0, 5, step =>
            {
                int idx = 2 * i + 1 - step;
                if (idx >= 0 && idx < transformLength)
                {
                    ulong c = uncarriedLow[idx] | ((ulong)uncarriedHigh[idx] << 32);
                    if (step == 0)
                    {
                        localSum += (c & 0xFFFFul) << 16;
                    }
                    else
                    {
                        localSum += (c >> ((step - 1) * 16)) & 0xFFFFFFFFul;
                    }
                }
            });
        }

        return localSum;
    }

    [GpuDeviceFunction]
    public static void ExecuteResolveNttCarriesBlocked(uint[] uncarriedLow, uint[] uncarriedHigh,
        uint[] outputBuffer, uint[] blockCarries, int transformLength, int maxWordCount, int chunkSize)
    {
        var tIdx = ThreadIndex.X;
        var bDim = BlockDimension.X;
        var numBlocks = (maxWordCount + chunkSize - 1) / chunkSize;

        var sharedCarry = new SharedMemory<uint>(32);
        var sharedH = new SharedMemory<uint>(32);

        for (int bIdx = BlockIndex.X; bIdx < numBlocks; bIdx += GridDimension.X)
        {
            int startIdx = bIdx * chunkSize;
            int i = startIdx + tIdx;

            ulong localSum = ComputeNttLocalSum(uncarriedLow, uncarriedHigh, i, maxWordCount, transformLength);

            uint L = (uint)(localSum & 0xFFFFFFFFul);
            uint H = (uint)(localSum >> 32);

            int laneId = tIdx % 32;
            int warpId = tIdx / 32;

            uint prevH = ResolveOverlapH(H, sharedH, laneId, warpId);

            ulong sum = (ulong)L + prevH;
            uint g = (uint)(sum >> 32);
            uint p = (uint)sum == 0xFFFFFFFFu ? 1u : 0u;

            uint carryIn = ComputeBlockCarryScan(g, p, sharedCarry, laneId, warpId, bDim);

            if (i < maxWordCount)
            {
                outputBuffer[i] = (uint)sum + carryIn;
            }

            if (tIdx == 0)
            {
                uint lastH = sharedH[((bDim + 31) / 32) - 1];
                uint waveCarry =
                    GpuBigIntKernels.ApplyCarry(GpuBigIntKernels.UnpackCarryState(sharedCarry[((bDim + 31) / 32) - 1]),
                        0u);
                blockCarries[bIdx] = lastH + waveCarry;
            }

            Gpu.SyncThreads();
        }
    }

    [GpuDeviceFunction]
    internal static uint ResolveOverlapH(uint h, SharedMemory<uint> sharedH, int laneId, int warpId,
        uint initialH = 0u)
    {
        uint prevH = Warp.ShuffleUpSync(0xFFFFFFFFu, h, 1);

        if (laneId == 31)
        {
            sharedH[warpId] = h;
        }

        Gpu.SyncThreads();

        if (laneId == 0)
        {
            prevH = warpId == 0 ? initialH : sharedH[warpId - 1];
        }

        return prevH;
    }

    [GpuDeviceFunction]
    internal static uint ComputeBlockCarryScan(uint g, uint p, SharedMemory<uint> sharedCarry, int laneId, int warpId,
        int bDim)
    {
        var warpScan = GpuBigIntKernels.WarpScanGp(new GpuBigIntKernels.CarryState(g, p), laneId);
        uint scanG = warpScan.Generate;
        uint scanP = warpScan.Propagate;

        if (laneId == 31)
        {
            sharedCarry[warpId] = GpuBigIntKernels.PackCarryState(warpScan);
        }

        Gpu.SyncThreads();

        if (warpId == 0)
        {
            int numWarps = (bDim + 31) / 32;
            var valState = laneId < numWarps
                ? GpuBigIntKernels.UnpackCarryState(sharedCarry[laneId])
                : new GpuBigIntKernels.CarryState(0u, 1u);
            var blockScan = GpuBigIntKernels.WarpScanGp(valState, laneId);

            if (laneId < numWarps)
            {
                sharedCarry[laneId] = GpuBigIntKernels.PackCarryState(blockScan);
            }
        }

        Gpu.SyncThreads();

        uint carryIn = 0u;

        if (warpId > 0)
        {
            carryIn = GpuBigIntKernels.ApplyCarry(GpuBigIntKernels.UnpackCarryState(sharedCarry[warpId - 1]), carryIn);
        }

        uint prevScanG = Warp.ShuffleUpSync(0xFFFFFFFFu, scanG, 1);
        uint prevScanP = Warp.ShuffleUpSync(0xFFFFFFFFu, scanP, 1);
        if (laneId > 0)
        {
            carryIn = prevScanG | (prevScanP & carryIn);
        }

        return carryIn;
    }

    [GpuKernel]
    public static void ResolveNttCarriesBlockedKernel(uint[] uncarriedLow, uint[] uncarriedHigh,
        uint[] outputBuffer, uint[] blockCarries, int transformLength, int maxWordCount, int chunkSize)
    {
        ExecuteResolveNttCarriesBlocked(uncarriedLow, uncarriedHigh, outputBuffer, blockCarries, transformLength,
            maxWordCount, chunkSize);
    }

    [GpuDeviceFunction]
    public static void ExecuteResolveNttCarriesApply(uint[] outputBuffer, uint[] blockCarries, int maxWordCount,
        int chunkSize, int numBlocks)
    {
        Gpu.GridStrideLoop(numBlocks, globalBlockIdx =>
        {
            var carry = blockCarries[globalBlockIdx];
            if (carry > 0)
            {
                int startIdx = (globalBlockIdx + 1) * chunkSize;
                AtomicAddWithCarry(outputBuffer, startIdx, carry, maxWordCount);
            }
        });
    }

    [GpuKernel]
    public static void ResolveNttCarriesApplyKernel(uint[] outputBuffer, uint[] blockCarries, int maxWordCount,
        int chunkSize, int numBlocks)
    {
        ExecuteResolveNttCarriesApply(outputBuffer, blockCarries, maxWordCount, chunkSize, numBlocks);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AtomicAddWithCarry(uint[] address, int wordIndex, uint valueToAdd, int maxWordCount)
    {
        var carry = valueToAdd;
        var index = wordIndex;
        while (carry > 0 && index < maxWordCount)
        {
            var oldValue = Gpu.AtomicAdd(ref address[index], carry);
            if (oldValue + carry < oldValue)
            {
                carry = 1;
                index++;
            }
            else
            {
                break;
            }
        }
    }

    // Not inlined: inlined into a cooperative mega-kernel, the transpose takes the kernel past the registers of a
    // block of 1024 threads.
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void TransposeTiles(uint[] source, uint[] destination, int width, int height)
    {
        GpuBigIntFermatKernels.TransposeTileBatches(source, destination, width, height);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ExecuteSingleRowNtt(NumberTheoreticTransform args)
    {
        args.TemporaryBuffer = args.Buffer;
        args.CurrentL = args.Grid.M;
        args.ApplyTwiddleBefore = false;
        args.ApplyTwiddleAfter = false;
        args.MaxBlocks = 1;
        ExecuteNttSharedRow(args);
        Gpu.SyncGrid();
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConfigureColumnPassArgs(ref NumberTheoreticTransform args, bool transposes)
    {
        if (transposes)
        {
            args.Buffer = args.TemporaryBuffer;
        }
        else
        {
            args.TemporaryBuffer = args.Buffer;
            args.ColumnStride = args.Grid.N2;
        }

        args.CurrentL = args.Grid.N1;
        args.ApplyTwiddleBefore = false;
        args.ApplyTwiddleAfter = false;
        args.MaxBlocks = args.Grid.N2;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConfigureRowPassArgs(ref NumberTheoreticTransform args)
    {
        args.TemporaryBuffer = args.Buffer;
        args.CurrentL = args.Grid.N2;
        args.ApplyTwiddleBefore = false;
        args.ApplyTwiddleAfter = false;
        args.MaxBlocks = args.Grid.N1;
    }

    [GpuDeviceFunction]
    public static void ExecuteForwardNtt(NumberTheoreticTransform args)
    {
        var buffer = args.Buffer;
        var temporaryBuffer = args.TemporaryBuffer;
        var transformLength = args.Grid.M;
        var length1 = args.Grid.N1;
        var length2 = args.Grid.N2;

        if (transformLength <= SharedRowMaxTransformLength)
        {
            ExecuteSingleRowNtt(args);
            return;
        }

        // Up to StridedColumnMaxTransformLength, row x of the first pass is column x of the buffer, transformed in
        // place: the values and their places are those of transposing, transforming row x and transposing back.
        // Above it, the matrix is transposed into the scratch buffer and back. Every thread takes the same branch,
        // so all of them reach the same grid syncs.
        bool transposes = transformLength > StridedColumnMaxTransformLength;
        var argsRow1 = args;
        ConfigureColumnPassArgs(ref argsRow1, transposes);
        argsRow1.ApplyTwiddleAfter = true;
        var argsRow2 = args;
        ConfigureRowPassArgs(ref argsRow2);

        if (transposes)
        {
            TransposeTiles(buffer, temporaryBuffer, length2, length1);
            Gpu.SyncGrid();
        }

        ExecuteNttSharedRow(argsRow1);
        Gpu.SyncGrid();
        if (transposes)
        {
            TransposeTiles(temporaryBuffer, buffer, length1, length2);
            Gpu.SyncGrid();
        }

        ExecuteNttSharedRow(argsRow2);
        Gpu.SyncGrid();
    }

    [GpuDeviceFunction]
    public static void ExecuteInverseNtt(NumberTheoreticTransform args)
    {
        var buffer = args.Buffer;
        var temporaryBuffer = args.TemporaryBuffer;
        var transformLength = args.Grid.M;
        var length1 = args.Grid.N1;
        var length2 = args.Grid.N2;

        if (transformLength <= SharedRowMaxTransformLength)
        {
            ExecuteSingleRowNtt(args);
            return;
        }

        var argsRow1 = args;
        ConfigureRowPassArgs(ref argsRow1);

        // The second pass reads the columns in place or between two transposes (see ExecuteForwardNtt).
        bool transposes = transformLength > StridedColumnMaxTransformLength;
        var argsRow2 = args;
        ConfigureColumnPassArgs(ref argsRow2, transposes);
        argsRow2.ApplyTwiddleBefore = true;

        ExecuteNttSharedRow(argsRow1);
        Gpu.SyncGrid();
        if (transposes)
        {
            TransposeTiles(buffer, temporaryBuffer, length2, length1);
            Gpu.SyncGrid();
        }

        ExecuteNttSharedRow(argsRow2);
        Gpu.SyncGrid();
        if (transposes)
        {
            TransposeTiles(temporaryBuffer, buffer, length1, length2);
            Gpu.SyncGrid();
        }
    }

    [GpuDeviceFunction]
    public static void ExecuteDualForwardNtt(DualNtt ctx)
    {
        var args1 = new NumberTheoreticTransform
        {
            Buffer = ctx.Buffer1,
            TemporaryBuffer = ctx.TemporaryBuffer,
            Twiddle = ctx.Twiddle1,
            Grid = ctx.Grid,
            P = Prime1,
            Pm = BarrettMagic1
        };
        ExecuteForwardNtt(args1);

        var args2 = new NumberTheoreticTransform
        {
            Buffer = ctx.Buffer2,
            TemporaryBuffer = ctx.TemporaryBuffer,
            Twiddle = ctx.Twiddle2,
            Grid = ctx.Grid,
            P = Prime2,
            Pm = BarrettMagic2
        };
        ExecuteForwardNtt(args2);
    }

    [GpuDeviceFunction]
    public static void ExecuteDualInverseNtt(DualNtt ctx)
    {
        var args1 = new NumberTheoreticTransform
        {
            Buffer = ctx.Buffer1,
            TemporaryBuffer = ctx.TemporaryBuffer,
            Twiddle = ctx.Twiddle1,
            Grid = ctx.Grid,
            P = Prime1,
            Pm = BarrettMagic1,
            IsInverse = true
        };
        ExecuteInverseNtt(args1);

        var args2 = new NumberTheoreticTransform
        {
            Buffer = ctx.Buffer2,
            TemporaryBuffer = ctx.TemporaryBuffer,
            Twiddle = ctx.Twiddle2,
            Grid = ctx.Grid,
            P = Prime2,
            Pm = BarrettMagic2,
            IsInverse = true
        };
        ExecuteInverseNtt(args2);
    }

    // ReSharper disable once UnusedMember.Global -- Exists solely to trigger snapshot transpilation
    [GpuKernel]
    public static void _DiagnosticsWrapper_ExecuteNttSharedRow(uint[] data)
    {
        // This is a dummy kernel wrapper that exists solely to force the compilation of
        // the [GpuDeviceFunction] methods in this file for snapshot testing.
        NumberTheoreticTransform args = default;
        args.Buffer = data;
        ExecuteForwardNtt(args);
    }
}
