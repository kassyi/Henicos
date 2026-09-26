using Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;
using Kassyi.GSharp.Stdlib.Math.BigFloat.Runtime;
using Kassyi.GSharp.Stdlib.Math.BigFloat.Kernels;

namespace Kassyi.GSharp.Stdlib.Math.BigFloat.Providers;

public sealed class GSharpAotFloatProvider : IGpuBigFloatArithmetic
{
    public static readonly GSharpAotFloatProvider Instance = new();

    private GSharpAotFloatProvider() { }

    public void Normalize(ref BigIntExecutionContext context, 
                   BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
                   BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        var stream = context.Stream;
        int maxWords = resultSig.NumWords;

        // Step 1: Copy to host to determine shift amount (Phase 1 Hack for TDD)
        // Ideally this should be computed on GPU with CountLeadingZerosKernel.
        var valueWords = new uint[valueSig.NumWords];
        context.GpuContext.Provider.CopyDeviceToHost(valueWords, IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords, stream);

        int currentBitLength = 0;
        for (int i = valueWords.Length - 1; i >= 0; i--)
        {
            if (valueWords[i] != 0)
            {
                uint w = valueWords[i];
                int msb = 0;
                while (w > 0)
                {
                    msb++;
                    w >>= 1;
                }
                currentBitLength = i * 32 + msb;
                break;
            }
        }

        if (currentBitLength == 0)
        {
            resultExp = 0;
            resultSign = false;
            var zeros = new uint[maxWords];
            context.GpuContext.Provider.CopyHostToDevice(IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4), zeros, maxWords, stream);
            return;
        }

        int targetBitLength = maxWords * 32;
        int shift = targetBitLength - currentBitLength;
        int wordShift = shift / 32;
        int bitShift = shift % 32;

        if (bitShift < 0)
        {
            wordShift--;
            bitShift += 32;
        }

        resultExp = valueExp - shift;
        resultSign = valueSign;

        int blockDimX = 256;
        int gridDimX = (maxWords + blockDimX - 1) / blockDimX;

        GpuBigFloatKernelsWrapper.LaunchFloatNormalizeKernelPtr(
            IntPtr.Add(valueSig.Buffer.Pointer, valueSig.WordOffset * 4), valueSig.NumWords,
            IntPtr.Add(resultSig.Buffer.Pointer, resultSig.WordOffset * 4), maxWords,
            wordShift, bitShift,
            (uint)gridDimX, 1, 1, (uint)blockDimX, 1, 1,
            0, stream);
    }

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

    public void Divide(ref BigIntExecutionContext context, 
                BigIntMemoryHandle leftSig, int leftExp, bool leftSign,
                BigIntMemoryHandle rightSig, int rightExp, bool rightSign,
                BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        throw new NotImplementedException();
    }

    public void Reciprocal(ref BigIntExecutionContext context, 
                    BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
                    BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        throw new NotImplementedException();
    }

    public void Sqrt(ref BigIntExecutionContext context, 
              BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
              BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        throw new NotImplementedException();
    }

    public void Rsqrt(ref BigIntExecutionContext context, 
               BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
               BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        throw new NotImplementedException();
    }

    public void Exp(ref BigIntExecutionContext context, 
             BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
             BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        throw new NotImplementedException();
    }

    public void Log(ref BigIntExecutionContext context, 
             BigIntMemoryHandle valueSig, int valueExp, bool valueSign,
             BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        throw new NotImplementedException();
    }

    public void Pow(ref BigIntExecutionContext context, 
             BigIntMemoryHandle baseSig, int baseExp, bool baseSign,
             BigIntMemoryHandle expSig, int exponentExp, bool exponentSign,
             BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign)
    {
        throw new NotImplementedException();
    }
}
