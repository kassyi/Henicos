namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// Signed expression nodes. Every node has a static upper bound of its result in bits (BigIntWidths), computed from
// the bounds of its children when the expression is built; its result buffer has the words of that bound, so no
// node drops a carry. A caller's target must be at least that wide; the words above the result are cleared.

internal class GpuBigIntLeafExpression(GpuBigInt value) : IGpuBigIntExpression, IBitBoundedExpression
{
    public int NumWords => value.Value.NumWords;

    // Every bit of the operand's words: the leaf does not read its value
    public long Bits => BigIntWidths.OfWords(Math.Max(1, NumWords));

    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
    {
        if (!targetResult.HasValue)
        {
            // The leaf borrows its operand: the caller may dispose the result without freeing the operand
            return new GpuBigInt(value.Value.AsBorrowed(), value.IsNegative);
        }

        var target = targetResult.Value;
        var words = value.Value.NumWords;
        SignedRoot.ThrowIfNarrower(target, words);

        // The target may be the operand itself (d.Value = d): then there is nothing to copy, and cuMemcpyDtoD does not
        // promise a copy onto itself
        if (target.DevicePointer != value.Value.DevicePointer)
        {
            if (DeviceRanges.Overlaps(target.DevicePointer, words, value.Value.DevicePointer, words))
            {
                // A target that partly overlaps the leaf (two views of one buffer): cuMemcpyDtoD does not promise
                // overlapping copies, so the words go through a temporary
                using var staging = new GpuBigUInt(workspace, words);
                staging.CopyFrom(value.Value, numWords: words);
                target.CopyFrom(staging, numWords: words);
            }
            else
            {
                target.CopyFrom(value.Value, numWords: words);
            }
        }

        ResultBuffers.ClearAbove(target, words);
        return new GpuBigInt(target, value.IsNegative);
    }
}
