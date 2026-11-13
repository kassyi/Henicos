namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// a.Truncate(words): a mod 2^(32 words), as a C# unchecked narrowing; a.Fit(words): the same value, but a value that
// does not fit is a domain error, as a C# checked narrowing
internal class NarrowExpression(IGpuBigUIntExpression target, int words, bool isChecked)
    : UnsignedOperatorExpression(target ?? throw new ArgumentNullException(nameof(target)))
{
    internal int Words { get; } = words;
    internal bool IsChecked { get; } = isChecked;
    public override long Bits => BigIntWidths.OfWords(Words);
    public override int NumWords => Words;

    // The low words of the operand, zero-extended; Fit also checks that no word above them is set
    public override void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result)
    {
        var workspace = context.GpuWorkspace;
        // Borrowed views: disposing them frees nothing
        using var operand = new GpuBigUInt(workspace, first.Borrow());
        if (IsChecked)
        {
            BigIntWideOps.CheckFits(operand, Words);
        }

        var copied = Math.Min(Words, operand.NumWords);
        using var resultBuffer = new GpuBigUInt(workspace, result.Borrow());
        resultBuffer.CopyFrom(operand, numWords: copied);
        ResultBuffers.ClearAbove(resultBuffer, copied);
    }
}
