// ReSharper disable All
namespace Kassyi.GSharp.Stdlib.Math.Kernels;

[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for library consumers")]
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for library consumers")]
public static class GpuBigIntMulKernels
{
    [GpuKernel(Cooperative = true)]
    public static void Kernel_MultiplyNtt(
        uint[] opA, uint[] opB, uint[] opResult,
        uint[] nttBuf1A, uint[] nttBuf2A, uint[] nttBuf1B, uint[] nttBuf2B, uint[] nttTmpBuf,
        uint[] uncarriedLow, uint[] uncarriedHigh,
        uint[] twiddleForward1, uint[] twiddleForward2, uint[] twiddleInverse1, uint[] twiddleInverse2,
        int numWordsA, int numWordsB, int outNumWords, int transformLength)
    {
        // 1. Unpack
        GpuBigIntFermatKernels.Device_Unpack16(opA, nttBuf1A, nttBuf2A, transformLength, numWordsA);
        GpuBigIntFermatKernels.Device_Unpack16(opB, nttBuf1B, nttBuf2B, transformLength, numWordsB);
        Gpu.SyncGrid();

        // 2. Forward NTT
        int n2 = 1;
        while (n2 * n2 < transformLength)
        {
            n2 *= 2;
        }

        var n1 = transformLength / n2;

        GpuBigIntNttKernels.Device_ForwardNTT(new()
        {
            Buffer = nttBuf1A,
            TemporaryBuffer = nttTmpBuf,
            Twiddle = twiddleForward1,
            M = transformLength,
            N1 = n1,
            N2 = n2,
            P = GpuBigIntNttKernels.Prime1,
            Pm = GpuBigIntNttKernels.BarrettMagic1
        });
        Gpu.SyncGrid();

        GpuBigIntNttKernels.Device_ForwardNTT(new()
        {
            Buffer = nttBuf2A,
            TemporaryBuffer = nttTmpBuf,
            Twiddle = twiddleForward2,
            M = transformLength,
            N1 = n1,
            N2 = n2,
            P = GpuBigIntNttKernels.Prime2,
            Pm = GpuBigIntNttKernels.BarrettMagic2
        });
        Gpu.SyncGrid();

        GpuBigIntNttKernels.Device_ForwardNTT(new()
        {
            Buffer = nttBuf1B,
            TemporaryBuffer = nttTmpBuf,
            Twiddle = twiddleForward1,
            M = transformLength,
            N1 = n1,
            N2 = n2,
            P = GpuBigIntNttKernels.Prime1,
            Pm = GpuBigIntNttKernels.BarrettMagic1
        });
        Gpu.SyncGrid();

        GpuBigIntNttKernels.Device_ForwardNTT(new()
        {
            Buffer = nttBuf2B,
            TemporaryBuffer = nttTmpBuf,
            Twiddle = twiddleForward2,
            M = transformLength,
            N1 = n1,
            N2 = n2,
            P = GpuBigIntNttKernels.Prime2,
            Pm = GpuBigIntNttKernels.BarrettMagic2
        });
        Gpu.SyncGrid();

        // 3. Pointwise Multiply
        GpuBigIntNttKernels.Device_NTT_PointwiseMultiply(
            nttBuf1A, nttBuf2A, nttBuf1B, nttBuf2B, transformLength);
        Gpu.SyncGrid();

        // 4. Inverse NTT
        GpuBigIntNttKernels.Device_InverseNTT(new()
        {
            Buffer = nttBuf1A,
            TemporaryBuffer = nttTmpBuf,
            Twiddle = twiddleInverse1,
            M = transformLength,
            N1 = n1,
            N2 = n2,
            P = GpuBigIntNttKernels.Prime1,
            Pm = GpuBigIntNttKernels.BarrettMagic1
        });
        Gpu.SyncGrid();

        GpuBigIntNttKernels.Device_InverseNTT(new()
        {
            Buffer = nttBuf2A,
            TemporaryBuffer = nttTmpBuf,
            Twiddle = twiddleInverse2,
            M = transformLength,
            N1 = n1,
            N2 = n2,
            P = GpuBigIntNttKernels.Prime2,
            Pm = GpuBigIntNttKernels.BarrettMagic2
        });
        Gpu.SyncGrid();

        // 5. CRT & Compute Coefficients
        GpuBigIntNttKernels.Device_CRT_ComputeCoefficients(
            nttBuf1A, nttBuf2A, uncarriedLow, uncarriedHigh, transformLength);
    }
}


