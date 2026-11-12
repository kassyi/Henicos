namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// The parameters of one big-float <c>Exp</c> evaluation, chosen by <see cref="TranscendentalPlanner.PlanExp"/>.
/// </summary>
/// <param name="ResultBits">The result precision <c>p</c> in bits (32 per result word).</param>
/// <param name="GuardWords">The guard words <c>gw</c> of the working precision <c>W = p + 32·gw</c>.</param>
/// <param name="Reduction">The multiple <c>k</c> of ln 2 subtracted: <c>x = k·ln 2 + r</c>, <c>r ∈ (0.69, 1.39)</c>.</param>
/// <param name="Halvings">The halvings <c>s ≥ 2</c>: the series runs at <c>r / 2^s</c> and is squared <c>s</c> times.</param>
/// <param name="Terms">The degree <c>N</c> of the Taylor polynomial <c>Σ_{j≤N} ρ^j / j!</c>.</param>
/// <param name="BlockSize">The Paterson–Stockmeyer block size <c>m ≥ 2</c> of the polynomial.</param>
/// <param name="ErrorBound">
/// The bound <c>B</c> of the approximation <c>A</c> of <c>e^x</c>: <c>|A − e^x| ≤ B</c> units of the last bit of
/// <c>A</c> normalized to <c>W</c> bits.
/// </param>
/// <param name="Ln2FractionBits">The fraction bits of ln 2 for <c>k·ln 2</c>, <c>W − s + bits(|k|) + 2</c>.</param>
internal readonly record struct ExpPlan(
    int ResultBits, int GuardWords, long Reduction, int Halvings, int Terms, int BlockSize, BigInteger ErrorBound,
    int Ln2FractionBits)
{
    /// <summary>Gets the working precision <c>W = p + 32·gw</c> in bits, a multiple of 32.</summary>
    public int WorkingBits => ResultBits + 32 * GuardWords;
}
