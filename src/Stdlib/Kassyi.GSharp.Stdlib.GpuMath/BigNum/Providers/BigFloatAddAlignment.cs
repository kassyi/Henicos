namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// How the big-float <c>Add</c> of <see cref="GSharpKernelFloatProvider"/> and <see cref="CSharpCpuBigFloatArithmetic"/>
/// aligns its operands (#775): which operand is shifted onto the other, by how many bits, and when the other one only
/// matters as a sticky bit.
/// </summary>
/// <remarks>
/// <para>
/// H is the operand with the larger exponent (on a tie, the wider significand) and L the other one; the sum is
/// computed exactly at the exponent of L, with H shifted left by <c>e_H - e_L</c> bits, and then rounded toward zero to
/// <c>p = 32n</c> bits for <c>n</c> result words. Exponents are subtracted in <see cref="long"/>, so no difference wraps
/// around.
/// </para>
/// <para>
/// When <c>e_L + 32·w_L &lt;= e_H - p</c> (<c>w_L</c> the words of L), L is below <c>2^c</c> for <c>c = e_H - p</c>,
/// and <c>c &lt;= min(e_H, top(H) - p - 1)</c> for a nonzero H. H is a multiple of <c>2^c</c>, so the sums
/// <c>H ± x</c> for every <c>0 &lt; x &lt; 2^c</c> have the same top bit and the same bits from <c>2^c</c> up, and so
/// the same <c>RZ_p</c>: a nonzero L can be replaced by the sticky bit <c>2^(c - 1)</c>, the way MPFR's
/// <c>mpfr_add1sp</c> and <c>mpfr_sub1sp</c> treat an operand more than <c>p</c> bits below the other. The work then
/// depends on the widths only, not on the exponent difference.
/// </para>
/// </remarks>
internal static class BigFloatAddAlignment
{
    /// <summary>Gets whether <paramref name="left"/> is H: the larger exponent, or on a tie the wider significand.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is shifted onto <paramref name="right"/>.</returns>
    public static bool LeftIsHigh(in GpuFloatOperand left, in GpuFloatOperand right) =>
        left.Exponent > right.Exponent
        || (left.Exponent == right.Exponent && left.Significand.NumWords >= right.Significand.NumWords);

    /// <summary>
    /// Gets whether <paramref name="low"/> lies below <c>2^(e_H - p)</c>, so that a nonzero L only matters as a sticky
    /// bit.
    /// </summary>
    /// <param name="high">H, the operand with the larger exponent.</param>
    /// <param name="low">L, the other operand.</param>
    /// <param name="resultWords">The number <c>n</c> of result words.</param>
    /// <returns><see langword="true"/> when <c>e_L + 32·w_L &lt;= e_H - 32n</c>.</returns>
    public static bool LowIsSticky(in GpuFloatOperand high, in GpuFloatOperand low, int resultWords) =>
        (long)low.Exponent + 32L * low.Significand.NumWords <= (long)high.Exponent - 32L * resultWords;

    /// <summary>
    /// Gets the exponent <c>e_H - p - 1</c> of the sticky bit that replaces a nonzero L. It fits in <see cref="int"/>
    /// whenever <see cref="LowIsSticky"/> holds: then <c>e_H - p - 1 &gt;= e_L + 32·w_L - 1 &gt;= e_L</c>.
    /// </summary>
    /// <param name="high">H, the operand with the larger exponent.</param>
    /// <param name="resultWords">The number <c>n</c> of result words.</param>
    /// <returns>The exponent of the sticky bit.</returns>
    public static int StickyExponent(in GpuFloatOperand high, int resultWords) =>
        checked((int)((long)high.Exponent - 32L * resultWords - 1));

    /// <summary>
    /// Gets the number of bits <c>e_H - e_L</c> by which H is shifted onto L. Outside <see cref="LowIsSticky"/> it is
    /// below <c>p + 32·w_L</c>, so the aligned sum stays within the widths of the operands and the result.
    /// </summary>
    /// <param name="highExponent">The exponent of H.</param>
    /// <param name="lowExponent">The exponent of L, or of the sticky bit that replaces it.</param>
    /// <returns>The shift in bits.</returns>
    public static int ShiftBits(int highExponent, int lowExponent) =>
        checked((int)((long)highExponent - lowExponent));
}
