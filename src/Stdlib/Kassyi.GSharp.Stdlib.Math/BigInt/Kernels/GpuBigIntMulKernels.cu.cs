// ReSharper disable All
namespace Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
// ReSharper disable once UnusedMember.Global -- Public API exposed for library consumers
[Kassyi.GSharp.Core.SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static class GpuBigIntMulKernels
{
    // Global Non-Cooperative NTT approach for Multi-Million digit multiplications

    [GpuKernel]
    public static void Kernel_MultiplyNtt_Unpack(
        uint[] opA, uint[] opB, uint[] nttBuf1A, uint[] nttBuf2A, uint[] nttBuf1B, uint[] nttBuf2B,
        int transformLength, int numWordsA, int numWordsB)
    {
        GpuBigIntFermatKernels.DeviceUnpack16(opA, nttBuf1A, nttBuf2A, transformLength, numWordsA);
        GpuBigIntFermatKernels.DeviceUnpack16(opB, nttBuf1B, nttBuf2B, transformLength, numWordsB);
    }

    [GpuKernel]
    public static void Kernel_MultiplyNtt_SharedRow(
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
        GpuBigIntNttKernels.Device_NTT_Shared_Row(args);
    }

    [GpuKernel]
    public static void Kernel_MultiplyNtt_Transpose(uint[] input, uint[] output, int width, int height)
    {
        GpuBigIntFermatKernels.DeviceTranspose32X32(input, output, width, height);
    }

    [GpuKernel]
    public static void Kernel_MultiplyNtt_PointwiseMultiply(
        uint[] nttBuf1A, uint[] nttBuf2A, uint[] nttBuf1B, uint[] nttBuf2B, int transformLength)
    {
        GpuBigIntNttKernels.Device_NTT_PointwiseMultiply(
            nttBuf1A, nttBuf2A, nttBuf1B, nttBuf2B, transformLength);
    }

    [GpuKernel]
    public static void Kernel_MultiplyNtt_ComputeCRT(
        uint[] deviceResult1, uint[] deviceResult2, uint[] uncarriedLow, uint[] uncarriedHigh, int transformLength)
    {
        GpuBigIntNttKernels.Device_CRT_ComputeCoefficients(
            deviceResult1, deviceResult2, uncarriedLow, uncarriedHigh, transformLength);
    }
}

