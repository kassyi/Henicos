namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// When the big-float <c>Divide</c> and <c>Rsqrt</c> of <see cref="GSharpKernelFloatProvider"/> cut a divisor or
/// radicand much wider than the result to its top <c>n + 2</c> words (#661 §4.12), for <c>n</c> result words.
/// </summary>
/// <remarks>
/// <para>
/// The results are the same bits with and without the cut; only the speed changes. The GPU division takes about 2 ms
/// for a divisor of up to <see cref="DivRemFastDivisorWords"/> words and 11 to 17 ms for a wider one, and the inverse
/// square root of the cut radicand at most 1.7 ms for results of up to <see cref="DefaultRsqrtMaxResultWords"/> words
/// and 24 ms from one word more. The cut pays off when it moves the work from the slow range into the fast one: there
/// it is 6 to 20 times faster. Once the top words are in the slow range themselves, the cut form takes 0.96 to 1.7
/// times as long as the full width (which gets faster with the width there), and a divisor already in the fast range
/// gains nothing from it.
/// </para>
/// <para>
/// The defaults come from <c>Henicos.Benchmarks arithmetic wide</c> on an RTX 3070 Ti (#767). Internal: the tests and
/// the benchmarks set the properties to force the cut on or off.
/// </para>
/// </remarks>
internal sealed class WideOperandOptions
{
    /// <summary>
    /// The widest divisor, in words, that the GPU division handles in its fast range (measured: 2 ms at 32 words, 15 ms
    /// at 33 and 34 words, 11 to 17 ms up to 1024 words).
    /// </summary>
    internal const int DivRemFastDivisorWords = 32;

    /// <summary>
    /// The default <see cref="ThresholdWords"/> (measured: at <c>n + 6</c> words the cut takes 0.95 to 1.12 of the
    /// full-width time; <c>Rsqrt</c> gains from about <c>n + 16</c> words on).
    /// </summary>
    internal const int DefaultThresholdWords = 4;

    /// <summary>
    /// The default <see cref="DivideMaxResultWords"/>: the cut divisor of <c>n + 2</c> words stays in the fast range of
    /// the division up to <c>n = 30</c> (measured with divisors of <c>4n</c> and <c>16n</c> words: 0.12 to 0.17 of the
    /// full-width time up to <c>n = 30</c>, 0.96 to 1.51 from <c>n = 31</c>).
    /// </summary>
    internal const int DefaultDivideMaxResultWords = DivRemFastDivisorWords - 2;

    /// <summary>
    /// The default <see cref="DivideMinDivisorWords"/>: a divisor of up to <see cref="DivRemFastDivisorWords"/> words
    /// is in the fast range already, and the cut gains nothing there (measured: 0.87 to 1.19 of the full-width time
    /// over four runs).
    /// </summary>
    internal const int DefaultDivideMinDivisorWords = DivRemFastDivisorWords + 1;

    /// <summary>
    /// The default <see cref="RsqrtMaxResultWords"/>: the cut inverse square root stays in its fast range up to
    /// <c>n = 44</c> (measured with radicands of <c>4n</c> and <c>16n</c> words: 0.05 to 0.10 of the full-width time up
    /// to <c>n = 44</c>, 1.05 to 1.68 from <c>n = 45</c>).
    /// </summary>
    internal const int DefaultRsqrtMaxResultWords = 44;

    /// <summary>
    /// Gets or sets the threshold <c>g</c>: an operand is cut only when it has at least <c>n + 2 + g</c> words.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int ThresholdWords
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = DefaultThresholdWords;

    /// <summary>Gets or sets the largest result width, in words, for which <c>Divide</c> cuts its divisor.</summary>
    public int DivideMaxResultWords { get; set; } = DefaultDivideMaxResultWords;

    /// <summary>Gets or sets the smallest divisor width, in words, that <c>Divide</c> cuts.</summary>
    public int DivideMinDivisorWords { get; set; } = DefaultDivideMinDivisorWords;

    /// <summary>Gets or sets the largest result width, in words, for which <c>Rsqrt</c> cuts its radicand.</summary>
    public int RsqrtMaxResultWords { get; set; } = DefaultRsqrtMaxResultWords;

    /// <summary>Gets whether a <c>Divide</c> of <paramref name="resultWords"/> result words cuts a divisor of
    /// <paramref name="divisorWords"/> words.</summary>
    /// <param name="resultWords">The number <c>n</c> of result words.</param>
    /// <param name="divisorWords">The number of words of the divisor.</param>
    /// <returns><see langword="true"/> when the divisor is cut to its top <c>n + 2</c> words.</returns>
    public bool CutsDivisor(int resultWords, int divisorWords) =>
        resultWords <= DivideMaxResultWords
        && divisorWords >= DivideMinDivisorWords
        && divisorWords - (resultWords + 2) >= ThresholdWords;

    /// <summary>Gets whether an <c>Rsqrt</c> of <paramref name="resultWords"/> result words cuts a normalized radicand
    /// of <paramref name="radicandWords"/> words.</summary>
    /// <param name="resultWords">The number <c>n</c> of result words.</param>
    /// <param name="radicandWords">The number of words of the normalized radicand.</param>
    /// <returns><see langword="true"/> when the radicand is cut to its top <c>n + 2</c> words.</returns>
    public bool CutsRadicand(int resultWords, int radicandWords) =>
        resultWords <= RsqrtMaxResultWords && radicandWords - (resultWords + 2) >= ThresholdWords;
}
