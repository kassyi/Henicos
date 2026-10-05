// ReSharper disable All

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

/// <summary>
/// Defines CUDA GPU device kernels for large-integer multiplication using NTT, Comba, and Karatsuba algorithms.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Emits device-side kernels transpiled via GSharp source generators for parallel multiplication pipelines.
/// </para>
/// <para>
/// <b>Execution Target:</b> GPU Device Kernels (<c>[GpuKernel]</c> / <c>[GpuDevice]</c>).
/// </para>
/// </remarks>
// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
// ReSharper disable once UnusedMember.Global -- Public API exposed for library consumers
[Kassyi.GSharp.Core.SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static partial class GpuBigIntMulKernels
{
    // Global Non-Cooperative NTT approach for Multi-Million digit multiplications
    public struct NttUnpackContext
    {
        [GpuReadOnly]
        public uint[] OpA;

        [GpuReadOnly]
        public uint[] OpB;

        public uint[] NttBuf1A;
        public uint[] NttBuf2A;
        public uint[] NttBuf1B;
        public uint[] NttBuf2B;
        public int TransformLength;
        public int NumWordsA;
        public int NumWordsB;
    }

    [GpuKernel]
    public static void MultiplyNttUnpackKernel(NttUnpackContext ctx)
    {
        GpuBigIntFermatKernels.DeviceUnpack16(ctx.OpA, ctx.NttBuf1A, ctx.NttBuf2A, ctx.TransformLength, ctx.NumWordsA,
            true);
        GpuBigIntFermatKernels.DeviceUnpack16(ctx.OpB, ctx.NttBuf1B, ctx.NttBuf2B, ctx.TransformLength, ctx.NumWordsB,
            true);
    }

    [GpuKernel]
    public static void MultiplyNttSharedRowKernel(NumberTheoreticTransform args)
    {
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

