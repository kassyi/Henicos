namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// Max and Min: the comparison that chooses an operand, and the copy of the chosen one into the node's result
internal static class SignedSelect
{
    // The comparison that chooses the operand of Max and Min is read from the device
    public static int Compare(GpuBigInt a, GpuBigInt b)
    {
        CaptureChecks.ThrowIfCapturing(a.Workspace, "The operand that Max and Min choose");
        return a.CompareTo(b);
    }

    // The chosen operand in resultBuffer, which is at least as wide as either operand's words; the words above it are
    // cleared
    public static GpuBigInt Copy(GpuBigInt chosen, GpuBigUInt resultBuffer)
    {
        var words = chosen.Value.NumWords;
        resultBuffer.CopyFrom(chosen.Value, numWords: words);
        ResultBuffers.ClearAbove(resultBuffer, words);
        return new GpuBigInt(resultBuffer, chosen.IsNegative);
    }
}
