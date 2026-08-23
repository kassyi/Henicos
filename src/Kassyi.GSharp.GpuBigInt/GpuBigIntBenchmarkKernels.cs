using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.GpuBigInt;

public static class GpuBigIntBenchmarkKernels
{
    [GpuKernel(Cooperative = true)]
    public static void Kernel_BenchmarkSquareChunk(ref FermatContext ctx, int chunkSize)
    {
        var valueT = ctx.Buffers.ValueT;
        var quotient = ctx.Buffers.Quotient;
        var remainder = ctx.Buffers.Remainder;
        var remainderMask = ctx.Buffers.RemainderMask;
        var temporaryBuffer = ctx.Buffers.TemporaryBuffer;
        var carryBuffer = ctx.Buffers.CarryBuffer;
        
        var numberOfWords = ctx.Dimensions.NumberOfWords;
        var numberOfWordsExtended = ctx.Dimensions.NumberOfWordsExtended;
        var exponent = ctx.Dimensions.Exponent;
        var bitOffset2E1 = ctx.Dimensions.BitOffset2E1;
        
        var nttBuffer1 = ctx.Ntt.Buffers.Buffer1;
        var nttBuffer2 = ctx.Ntt.Buffers.Buffer2;
        var nttTemp = ctx.Ntt.Buffers.TemporaryBuffer;
        var transformLength = ctx.Ntt.TransformLength;
        var t1Fwd = ctx.Ntt.TwiddleFactors.Forward1;
        var t2Fwd = ctx.Ntt.TwiddleFactors.Forward2;
        var t1Inv = ctx.Ntt.TwiddleFactors.Inverse1;
        var t2Inv = ctx.Ntt.TwiddleFactors.Inverse2;

        for (var iter = 0; iter < chunkSize; iter++)
        {
            GpuBigIntFermatKernels.Device_MultiplySquareAndReduce(3, valueT, quotient, remainder, remainderMask, temporaryBuffer, numberOfWords, numberOfWordsExtended, exponent, bitOffset2E1, carryBuffer, nttBuffer1, nttBuffer2, nttTemp, t1Fwd, t2Fwd, t1Inv, t2Inv, transformLength);
        }
    }
}
