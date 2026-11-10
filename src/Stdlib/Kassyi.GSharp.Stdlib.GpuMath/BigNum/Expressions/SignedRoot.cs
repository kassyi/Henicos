namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// The result buffer of a signed root: the first NumWords words of the caller's target, through a temporary when the
// target overlaps an operand, with the target's words above the result cleared
internal static class SignedRoot
{
    // A caller's target narrower than the bound would lose words
    public static void ThrowIfNarrower(GpuBigUInt? target, int numWords)
    {
        if (target is { } t && t.NumWords < numWords)
        {
            throw new ArgumentException(
                $"Destination buffer must have at least {numWords} words; it has {t.NumWords}. The result of an " +
                "expression is as wide as its bound; narrow it explicitly with Fit.", nameof(target));
        }
    }

    // An owned temporary of numWords words when the target overlaps an operand; otherwise the default value, which owns
    // nothing
    public static GpuBigUInt StagingFor(GpuWorkspace workspace, GpuBigUInt? target, int numWords, GpuBigUInt operand0,
        GpuBigUInt operand1)
    {
        if (target is not { } t ||
            (!DeviceRanges.Overlaps(t.Handle, operand0.Handle) &&
             !DeviceRanges.Overlaps(t.Handle, operand1.Handle)))
        {
            return default;
        }

        return workspace.RentTemp(numWords);
    }

    // The value in the buffer the caller sees: copied from the temporary when one was used; a caller's target gets its
    // words above the result cleared and is returned whole
    public static GpuBigUInt Deliver(GpuBigUInt staging, GpuBigUInt result, GpuBigUInt? target)
    {
        if (!staging.Handle.IsDefault)
        {
            result.CopyFrom(staging);
        }

        if (target is not { } t)
        {
            return result;
        }

        ResultBuffers.ClearAbove(t, result.NumWords);
        return t;
    }
}
