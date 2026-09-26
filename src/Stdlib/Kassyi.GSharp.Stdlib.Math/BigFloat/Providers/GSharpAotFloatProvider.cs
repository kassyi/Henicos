using Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;
using Kassyi.GSharp.Stdlib.Math.BigFloat.Runtime;
using Kassyi.GSharp.Stdlib.Math.BigFloat.Kernels;

namespace Kassyi.GSharp.Stdlib.Math.BigFloat.Providers;

public sealed class GSharpAotFloatProvider : IGpuBigFloatArithmetic
{
    public static readonly GSharpAotFloatProvider Instance = new();

    private GSharpAotFloatProvider() { }

    public void Add(ref BigIntExecutionContext context,
             BigIntMemoryHandle leftSig, int leftExp, bool leftSign,
             BigIntMemoryHandle rightSig, int rightExp, bool rightSign,
             BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        var stream = context.Stream;
        int maxWords = resultSig.NumWords;

        bool leftLarger = leftExp >= rightExp;
        int expDiff = leftLarger ? (leftExp - rightExp) : (rightExp - leftExp);

        var largerSig = leftLarger ? leftSig : rightSig;
        var smallerSig = leftLarger ? rightSig : leftSig;
        bool largerSign = leftLarger ? leftSign : rightSign;
        bool smallerSign = leftLarger ? rightSign : leftSign;

        resultExp = leftLarger ? leftExp : rightExp;
        
        int signScale = (largerSign == smallerSign) ? 1 : -1;
        resultSign = largerSign; // TODO: properly compute sign if subtraction

        int wordShift = expDiff / 32;
        int bitShift = expDiff % 32;

        int blockDimX = 256;
        int gridDimX = (maxWords + blockDimX - 1) / blockDimX;

        GpuBigFloatKernelsWrapper.LaunchFloatAddAlignedKernelPtr(
            IntPtr.Add(largerSig.Buffer.Pointer, largerSig.WordOffset * 4), largerSig.NumWords,
            IntPtr.Add(smallerSig.Buffer.Pointer, smallerSig.WordOffset * 4), smallerSig.NumWords,
            IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4), maxWords,
            wordShift, bitShift, signScale,
            (uint)gridDimX, 1, 1, (uint)blockDimX, 1, 1,
            0, stream);
    }

    public void Multiply(ref BigIntExecutionContext context,
                  BigIntMemoryHandle leftSig, int leftExp, bool leftSign,
                  BigIntMemoryHandle rightSig, int rightExp, bool rightSign,
                  BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        throw new NotImplementedException();
    }
}
