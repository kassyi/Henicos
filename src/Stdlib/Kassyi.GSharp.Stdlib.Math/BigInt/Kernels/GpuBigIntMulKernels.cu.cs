// ReSharper disable All
namespace Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
// ReSharper disable once UnusedMember.Global -- Public API exposed for library consumers
[Kassyi.GSharp.Core.SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static class GpuBigIntMulKernels
{
    // Global Non-Cooperative NTT approach for Multi-Million digit multiplications

    [GpuKernel]
    public static void MultiplyNttUnpackKernel(
        uint[] opA, uint[] opB, uint[] nttBuf1A, uint[] nttBuf2A, uint[] nttBuf1B, uint[] nttBuf2B,
        int transformLength, int numWordsA, int numWordsB)
    {
        GpuBigIntFermatKernels.DeviceUnpack16(opA, nttBuf1A, nttBuf2A, transformLength, numWordsA, true);
        GpuBigIntFermatKernels.DeviceUnpack16(opB, nttBuf1B, nttBuf2B, transformLength, numWordsB, true);
    }

    [GpuKernel]
    public static void MultiplyNttSharedRowKernel(
        uint[] buffer, uint[] temporaryBuffer, uint[] twiddle,
        int m, int n1, int n2, uint p, ulong pm,
        bool applyTwiddleBefore, bool applyTwiddleAfter,
        int maxBlocks, int currentL)
    {
        var args = new NumberTheoreticTransformKernelArgs
        {
            Buffer = buffer,
            TemporaryBuffer = temporaryBuffer,
            Twiddle = twiddle,
            M = m,
            N1 = n1,
            N2 = n2,
            P = p,
            Pm = pm,
            ApplyTwiddleBefore = applyTwiddleBefore,
            ApplyTwiddleAfter = applyTwiddleAfter,
            MaxBlocks = maxBlocks,
            CurrentL = currentL
        };
        GpuBigIntNttKernels.ExecuteNttSharedRow(args);
    }

    [GpuKernel]
    public static void MultiplyNttTransposeKernel(uint[] input, uint[] output, int width, int height)
    {
        GpuBigIntFermatKernels.DeviceTranspose32X32(input, output, width, height, true);
    }

    [GpuKernel]
    public static void MultiplyNttPointwiseMultiplyKernel(
        uint[] nttBuf1A, uint[] nttBuf2A, uint[] nttBuf1B, uint[] nttBuf2B, int transformLength)
    {
        GpuBigIntNttKernels.ExecuteNttPointwiseMultiply(
            nttBuf1A, nttBuf2A, nttBuf1B, nttBuf2B, transformLength);
    }

    [GpuKernel]
    public static void MultiplyNttComputeCrtKernel(
        uint[] deviceResult1, uint[] deviceResult2, uint[] uncarriedLow, uint[] uncarriedHigh, int transformLength)
    {
        GpuBigIntNttKernels.ExecuteCrtComputeCoefficients(
            deviceResult1, deviceResult2, uncarriedLow, uncarriedHigh, transformLength);
    }
}

