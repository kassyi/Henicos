namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// The parameters of one big-float <c>Log</c> evaluation, chosen by <see cref="TranscendentalPlanner.PlanLog"/>.
/// </summary>
/// <param name="ResultBits">The result precision <c>p</c> in bits (32 per result word).</param>
/// <param name="GuardWords">The guard words <c>gw</c> of the working precision <c>W = p + 32·gw</c>.</param>
/// <param name="Terms">
/// The number of terms <c>N</c> of <c>T = Σ_{k&lt;N} z^k / (2k+1)</c>, <c>z = y²</c>; 0 when the reduced argument
/// <c>m</c> is exactly 1 and no series is needed.
/// </param>
/// <param name="BlockSize">
/// The Paterson–Stockmeyer block size <c>m ≥ 2</c> of the series, or 0 for Horner form from the top.
/// </param>
/// <param name="ErrorBound">
/// The bound <c>B</c> of the <c>W</c>-bit approximation <c>A</c>: <c>|A − ln x| ≤ B</c> units of its last bit.
/// </param>
/// <param name="Ln2FractionBits">
/// The fraction bits of ln 2 for <c>e·ln 2</c>, <c>W + ceil(log2 |e|) + 4</c>, or 0 when <c>e = 0</c>.
/// </param>
internal readonly record struct LogPlan(
    int ResultBits, int GuardWords, int Terms, int BlockSize, int ErrorBound, int Ln2FractionBits)
{
    /// <summary>Gets the working precision <c>W = p + 32·gw</c> in bits, a multiple of 32.</summary>
    public int WorkingBits => ResultBits + 32 * GuardWords;
}
