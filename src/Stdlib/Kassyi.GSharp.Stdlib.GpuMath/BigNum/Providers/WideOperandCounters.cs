namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// Counts how often the big-float <c>Divide</c> and <c>Rsqrt</c> of <see cref="GSharpKernelFloatProvider"/> truncate a
/// wide operand and how often they run the full-width check of the estimate (#661 §4.12), for one
/// <see cref="GpuWorkspace"/>.
/// </summary>
/// <remarks>
/// The values change no result; the tests read them to see that the truncated path ran and that the check ran only
/// when the discarded bits of the estimate left the rounded result open.
/// </remarks>
internal sealed class WideOperandCounters
{
    private int _truncations;
    private int _boundChecks;

    /// <summary>Gets the number of calls that truncated the divisor or the radicand.</summary>
    public int Truncations => Volatile.Read(ref _truncations);

    /// <summary>Gets the number of full-width checks (<c>x²·A &gt; 2^(2B)</c>, <c>Q·R &gt; N</c>) launched.</summary>
    public int BoundChecks => Volatile.Read(ref _boundChecks);

    /// <summary>Counts a call that truncated its operand.</summary>
    public void AddTruncation() => Interlocked.Increment(ref _truncations);

    /// <summary>Counts a full-width check.</summary>
    public void AddBoundCheck() => Interlocked.Increment(ref _boundChecks);
}
