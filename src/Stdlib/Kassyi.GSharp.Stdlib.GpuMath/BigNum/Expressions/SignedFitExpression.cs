namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// x.Fit(words): the value in words words, checked: a magnitude that does not fit is a domain error (C# checked
// narrowing). The sign is kept.
internal sealed class SignedFitExpression(IGpuBigIntExpression target, int words)
    : SignedUnaryExpression(target, BigIntWidths.OfWords(words))
{
    public override SignedOperation Operation => SignedOperation.Fit;

    protected override GpuBigInt Execute(GpuBigInt targetEval, GpuBigUInt resultBuffer)
    {
        var magnitude = targetEval.Value;
        BigIntWideOps.CheckFits(magnitude, words);
        var copied = Math.Min(words, magnitude.NumWords);
        resultBuffer.CopyFrom(magnitude, numWords: copied);
        ResultBuffers.ClearAbove(resultBuffer, copied);
        return new GpuBigInt(resultBuffer, targetEval.IsNegative);
    }
}
