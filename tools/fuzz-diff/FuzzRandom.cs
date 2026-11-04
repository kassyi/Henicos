using System.Globalization;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Deterministic SplitMix64 pseudorandom number generator used by the differential kernel fuzzer.
/// Identical seeds produce identical sequences across all operating systems and .NET runtimes.
/// </summary>
public sealed class FuzzRandom
{
    private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;

    private ulong _state;

    /// <summary>Initializes a new instance of the <see cref="FuzzRandom"/> class with the specified 64-bit seed.</summary>
    public FuzzRandom(ulong seed)
    {
        Seed = seed;
        _state = seed;
    }

    /// <summary>Gets the initial 64-bit seed.</summary>
    public ulong Seed { get; }

    /// <summary>Parses a 64-bit seed from decimal or <c>0x</c>-prefixed hexadecimal text.</summary>
    public static bool TryParseSeed(string? text, out ulong seed)
    {
        seed = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        ReadOnlySpan<char> trimmed = text.AsSpan().Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out seed);
        }

        if (ulong.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
        {
            return true;
        }

        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long signed))
        {
            seed = unchecked((ulong)signed);
            return true;
        }

        return false;
    }

    /// <summary>Formats a 64-bit seed as a canonical <c>0x</c>-prefixed 16-digit hexadecimal string.</summary>
    public static string FormatSeed(ulong seed) =>
        string.Create(CultureInfo.InvariantCulture, $"0x{seed:X16}");

    /// <summary>Returns the next 64 pseudorandom bits.</summary>
    public ulong NextUInt64()
    {
        _state = unchecked(_state + GoldenGamma);
        return Mix(_state);
    }

    /// <summary>Returns the next 32 pseudorandom bits.</summary>
    public uint NextUInt32() => (uint)(NextUInt64() >> 32);

    /// <summary>Returns the next 32 pseudorandom bits as a signed integer.</summary>
    public int NextInt32() => unchecked((int)NextUInt32());

    /// <summary>Returns the next 64 pseudorandom bits as a signed 64-bit integer.</summary>
    public long NextInt64() => unchecked((long)NextUInt64());

    /// <summary>Returns a pseudorandom integer in <c>[0, bound)</c>.</summary>
    public int NextBelow(int bound)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bound);
        return (int)(((ulong)NextUInt32() * (uint)bound) >> 32);
    }

    /// <summary>Returns a pseudorandom integer in <c>[minInclusive, maxInclusive]</c>.</summary>
    public int NextRange(int minInclusive, int maxInclusive)
    {
        if (minInclusive > maxInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(minInclusive));
        }

        ulong span = (ulong)((long)maxInclusive - minInclusive + 1L);
        if (span <= 1UL)
        {
            return minInclusive;
        }

        return minInclusive + NextBelow((int)span);
    }

    /// <summary>Returns <see langword="true"/> or <see langword="false"/> with equal probability.</summary>
    public bool NextBool() => (NextUInt64() >> 63) != 0;

    /// <summary>Returns <see langword="true"/> with the specified <paramref name="probability"/> in <c>[0.0, 1.0]</c>.</summary>
    public bool Chance(double probability)
    {
        if (probability <= 0.0)
        {
            return false;
        }

        if (probability >= 1.0)
        {
            return true;
        }

        double sample = (NextUInt64() >> 11) * (1.0 / (1UL << 53));
        return sample < probability;
    }

    /// <summary>Selects a random element from a non-empty list.</summary>
    public T Choose<T>(IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new ArgumentException("Cannot choose from an empty list.", nameof(items));
        }

        return items[NextBelow(items.Count)];
    }

    /// <summary>Creates a deterministic child generator derived from the current state and a stream tag.</summary>
    public FuzzRandom Fork(ulong streamTag = 0)
    {
        ulong derived = Mix(unchecked(NextUInt64() ^ streamTag));
        return new FuzzRandom(derived);
    }

    /// <summary>The SplitMix64 mixing bijection.</summary>
    public static ulong Mix(ulong z)
    {
        unchecked
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
