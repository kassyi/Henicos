namespace Kassyi.GSharp.Stdlib.Math.Kernels;

[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for library consumers")]
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for library consumers")]
public static class GpuBigIntMulKernels
{
    [GpuKernel(Cooperative = true)]
    public static void Kernel_MultiplyNtt(
        uint[] opA, uint[] opB, uint[] opResult,
        uint[] nttBuf1A, uint[] nttBuf2A, uint[] nttBuf1B, uint[] nttBuf2B, uint[] nttTmpBuf,
        uint[] twiddleForward1, uint[] twiddleForward2, uint[] twiddleInverse1, uint[] twiddleInverse2,
        int numWordsA, int numWordsB, int outNumWords, int transformLength)
    {
        // 1. Init
        var tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        for (var i = tid; i < outNumWords; i += GridDimension.X * BlockDimension.X)
        {
            opResult[i] = 0;
        }

        // 2. Unpack
        GpuBigIntFermatKernels.Device_Unpack16(opA, nttBuf1A, nttBuf2A, transformLength, numWordsA);
        GpuBigIntFermatKernels.Device_Unpack16(opB, nttBuf1B, nttBuf2B, transformLength, numWordsB);
        Gpu.SyncGrid();

        // 3. Forward NTT
        int n2 = 1;
        while (n2 * n2 < transformLength)
        {
            n2 *= 2;
        }

        var n1 = transformLength / n2;

        var nttArgs1A = new NumberTheoreticTransformKernelArgs
        {
            Buffer = nttBuf1A,
            TemporaryBuffer = nttTmpBuf,
            Twiddle = twiddleForward1,
            M = transformLength,
            N1 = n1,
            N2 = n2,
            P = GpuBigIntNttKernels.Prime1,
            Pm = GpuBigIntNttKernels.BarrettMagic1
        };
        GpuBigIntNttKernels.Device_ForwardNTT(ref nttArgs1A);
        Gpu.SyncGrid();

        var nttArgs2A = new NumberTheoreticTransformKernelArgs
        {
            Buffer = nttBuf2A,
            TemporaryBuffer = nttTmpBuf,
            Twiddle = twiddleForward2,
            M = transformLength,
            N1 = n1,
            N2 = n2,
            P = GpuBigIntNttKernels.Prime2,
            Pm = GpuBigIntNttKernels.BarrettMagic2
        };
        GpuBigIntNttKernels.Device_ForwardNTT(ref nttArgs2A);
        Gpu.SyncGrid();

        var nttArgs1B = new NumberTheoreticTransformKernelArgs
        {
            Buffer = nttBuf1B,
            TemporaryBuffer = nttTmpBuf,
            Twiddle = twiddleForward1,
            M = transformLength,
            N1 = n1,
            N2 = n2,
            P = GpuBigIntNttKernels.Prime1,
            Pm = GpuBigIntNttKernels.BarrettMagic1
        };
        GpuBigIntNttKernels.Device_ForwardNTT(ref nttArgs1B);
        Gpu.SyncGrid();

        var nttArgs2B = new NumberTheoreticTransformKernelArgs
        {
            Buffer = nttBuf2B,
            TemporaryBuffer = nttTmpBuf,
            Twiddle = twiddleForward2,
            M = transformLength,
            N1 = n1,
            N2 = n2,
            P = GpuBigIntNttKernels.Prime2,
            Pm = GpuBigIntNttKernels.BarrettMagic2
        };
        GpuBigIntNttKernels.Device_ForwardNTT(ref nttArgs2B);
        Gpu.SyncGrid();

        // 4. Pointwise Multiply
        GpuBigIntNttKernels.Device_NTT_PointwiseMultiply(
            nttBuf1A, nttBuf2A, nttBuf1B, nttBuf2B, transformLength);
        Gpu.SyncGrid();

        // 5. Inverse NTT
        var invArgs1 = new NumberTheoreticTransformKernelArgs
        {
            Buffer = nttBuf1A,
            TemporaryBuffer = nttTmpBuf,
            Twiddle = twiddleInverse1,
            M = transformLength,
            N1 = n1,
            N2 = n2,
            P = GpuBigIntNttKernels.Prime1,
            Pm = GpuBigIntNttKernels.BarrettMagic1
        };
        GpuBigIntNttKernels.Device_InverseNTT(ref invArgs1);
        Gpu.SyncGrid();

        var invArgs2 = new NumberTheoreticTransformKernelArgs
        {
            Buffer = nttBuf2A,
            TemporaryBuffer = nttTmpBuf,
            Twiddle = twiddleInverse2,
            M = transformLength,
            N1 = n1,
            N2 = n2,
            P = GpuBigIntNttKernels.Prime2,
            Pm = GpuBigIntNttKernels.BarrettMagic2
        };
        GpuBigIntNttKernels.Device_InverseNTT(ref invArgs2);
        Gpu.SyncGrid();

        // 6. CRT & Carry Resolution
        GpuBigIntNttKernels.Device_CRT_ParallelAtomic(
            nttBuf1A, nttBuf2A, opResult, transformLength, outNumWords);
    }
}


