namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// The result buffer rules of the scalar operations <c>GpuBigIntMath.Add/Sub/Mul(a, uint, r)</c>, shared by the GPU
/// kernels and the CPU fallback.
/// </summary>
/// <remarks>
/// <para>
/// <b>Narrower than the operand:</b> rejected before anything is written.
/// </para>
/// <para>
/// <b>Wider than the operand:</b> the exact result; the words above it are cleared. A difference below zero is still a
/// domain error.
/// </para>
/// <para>
/// <b>As wide as the operand (the checked form):</b> a carry out of the top word, a non-zero top word of the product
/// and a difference below zero are domain errors. On the GPU they are reported as <see cref="OverflowException"/> at
/// the next read of the workspace stream; the CPU fallback throws at the call. The contents of the result buffer after
/// a domain error are unspecified.
/// </para>
/// </remarks>
internal static class ScalarResultWidth
{
    /// <summary>Throws when <paramref name="result"/> has fewer words than <paramref name="operand"/>.</summary>
    /// <exception cref="ArgumentException">The result buffer is narrower than the operand.</exception>
    public static void ThrowIfNarrower(GpuBigUInt operand, GpuBigUInt result)
    {
        if (result.NumWords < operand.NumWords)
        {
            throw new ArgumentException(
                "The result buffer must have at least as many words as the operand. " +
                $"a: {operand.NumWords}, result: {result.NumWords} words.",
                nameof(result));
        }
    }

    /// <summary>
    /// The block size of the scalar kernels: one block, a multiple of 32, so that every lane named by the warp scans
    /// exists.
    /// </summary>
    public static uint BlockSize(GpuBigUInt operand) =>
        (uint)((Math.Min(1024, Math.Max(operand.NumWords, 1)) + 31) / 32 * 32);

    /// <summary>
    /// Throws when the exact result of a scalar operation on the CPU fallback is below zero, or does not fit in a
    /// result buffer as wide as the operand. The caller has already written the result modulo the buffer width.
    /// </summary>
    /// <param name="value">The exact result.</param>
    /// <param name="operandWords">The word count of the operand.</param>
    /// <param name="resultWords">The word count of the result buffer.</param>
    /// <param name="operation">The name of the operation for the message.</param>
    /// <exception cref="OverflowException">The result is below zero or does not fit.</exception>
    public static void ThrowIfOutOfDomain(BigInteger value, int operandWords, int resultWords, string operation)
    {
        if (value.Sign < 0)
        {
            throw new OverflowException($"{operation}: the difference is below zero.");
        }

        if (resultWords == operandWords && value.GetBitLength() > 32L * resultWords)
        {
            throw new OverflowException(
                $"{operation}: the result does not fit in the {resultWords} words of the result buffer; pass a " +
                "wider buffer to get the exact value.");
        }
    }
}
