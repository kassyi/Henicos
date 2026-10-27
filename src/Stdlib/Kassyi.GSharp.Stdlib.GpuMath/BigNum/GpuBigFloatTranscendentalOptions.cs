namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Tuning parameters of the big-float transcendental functions (<c>Exp</c>, <c>Log</c>, <c>Pow</c>) of one
/// <see cref="GpuWorkspace"/>.
/// </summary>
/// <remarks>
/// <para>
/// A parameter left <see langword="null"/> is chosen per call by the host-side planner from the precision and the
/// argument (the number of full-width multiplications, weighted by the working width). A value overrides that choice.
/// The parameters change only the speed and the working precision; the accuracy of the results does not depend on
/// them, because the guard bits are derived from the error bound of the chosen parameters.
/// </para>
/// <para><b>Thread Safety:</b> set the parameters before the workspace is used from several threads.</para>
/// </remarks>
public sealed class GpuBigFloatTranscendentalOptions
{
    /// <summary>The smallest accepted <see cref="ExpHalvingCount"/>.</summary>
    public const int MinExpHalvingCount = 2;

    /// <summary>The largest accepted <see cref="ExpHalvingCount"/>.</summary>
    public const int MaxExpHalvingCount = 4096;

    /// <summary>
    /// Gets or sets the number <c>s</c> of halvings of <c>Exp</c>: after the reduction <c>x = k·ln 2 + r</c>, the series
    /// is evaluated at <c>r / 2^s</c> and squared <c>s</c> times. <see langword="null"/> (the default) lets the planner
    /// choose.
    /// </summary>
    /// <remarks>
    /// A larger <c>s</c> needs fewer series terms but amplifies the rounding error by <c>2^s</c>, which the planner
    /// compensates with more guard bits. The error bound used for that assumes <c>r / 2^s ≤ 1/2</c>, hence the lower
    /// limit of 2.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not <see langword="null"/> and lies outside
    /// [<see cref="MinExpHalvingCount"/>, <see cref="MaxExpHalvingCount"/>].</exception>
    public int? ExpHalvingCount
    {
        get;
        set
        {
            if (value is { } halvings)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(halvings, MinExpHalvingCount, nameof(value));
                ArgumentOutOfRangeException.ThrowIfGreaterThan(halvings, MaxExpHalvingCount, nameof(value));
            }

            field = value;
        }
    }

    /// <summary>The smallest accepted <see cref="SeriesBlockSize"/>.</summary>
    public const int MinSeriesBlockSize = 2;

    /// <summary>The largest accepted <see cref="SeriesBlockSize"/>.</summary>
    public const int MaxSeriesBlockSize = 4096;

    /// <summary>
    /// Gets or sets the block size <c>m</c> of the Paterson–Stockmeyer evaluation of the <c>Exp</c> and <c>Log</c>
    /// series: the powers <c>x^1 … x^m</c> are computed once and the series is evaluated in Horner form over blocks of
    /// <c>m</c> terms, with about <c>m − 1 + N/m</c> full-width multiplications for <c>N</c> terms.
    /// <see langword="null"/> (the default) lets the planner choose the <c>m</c> with the fewest multiplications.
    /// </summary>
    /// <remarks>
    /// The rounding error of the <c>Log</c> series grows with <c>m</c> (by about one unit per term of a block); the
    /// planner adds guard bits for it. Each power takes a buffer of twice the working precision on the device.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not <see langword="null"/> and lies outside
    /// [<see cref="MinSeriesBlockSize"/>, <see cref="MaxSeriesBlockSize"/>].</exception>
    public int? SeriesBlockSize
    {
        get;
        set
        {
            if (value is { } blockSize)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(blockSize, MinSeriesBlockSize, nameof(value));
                ArgumentOutOfRangeException.ThrowIfGreaterThan(blockSize, MaxSeriesBlockSize, nameof(value));
            }

            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the largest number of terms of the <c>Log</c> series (<c>atanh</c>) that is evaluated in Horner form
    /// (one multiplication per term, error below 1.8 units); a longer series uses Paterson–Stockmeyer. The default is
    /// 8, so only arguments close to 1 take Horner form. 0 always uses Paterson–Stockmeyer, <see cref="int.MaxValue"/>
    /// always Horner form.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int AtanhHornerMaxTerms
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 8;
}
