#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// The seeded generator of the differential tests: SplitMix64 (Steele, Lea and Flood, "Fast Splittable Pseudorandom
/// Number Generators", OOPSLA 2014). A seed gives the same values on every .NET version and machine, which
/// <see cref="System.Random"/> does not promise, so a seed printed by a failure reproduces it anywhere.
/// </summary>
/// <param name="seed">The seed. Seed 0 is the boundary seed (<see cref="IsBoundarySeed"/>).</param>
public sealed class DiffRandom(int seed)
{
    private const ulong Golden = 0x9E3779B97F4A7C15ul;

    // A negative seed is sign-extended, so -1 and int.MaxValue start different sequences
    private ulong _state = unchecked((ulong)seed);

    /// <summary>Gets the seed this generator started from.</summary>
    public int Seed { get; } = seed;

    /// <summary>
    /// Gets a value indicating whether this is seed 0, for which <see cref="DiffValues"/> returns the boundary tables
    /// instead of random values.
    /// </summary>
    public bool IsBoundarySeed => Seed == 0;

    /// <summary>Returns the next 64 random bits.</summary>
    public ulong NextUInt64()
    {
        _state = unchecked(_state + Golden);
        return Mix(_state);
    }

    /// <summary>Returns the next 32 random bits (the upper half of <see cref="NextUInt64"/>).</summary>
    public uint NextUInt32() => (uint)(NextUInt64() >> 32);

    /// <summary>Returns the next 32 random bits as an <see cref="int"/>.</summary>
    public int NextInt32() => unchecked((int)NextUInt32());

    /// <summary>Returns a random value in <c>[0, bound)</c> (Lemire's multiply-shift; the bias is below 2^-32 * bound).</summary>
    /// <param name="bound">The exclusive upper bound; must be positive.</param>
    public int NextBelow(int bound)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bound);
        return (int)(((ulong)NextUInt32() * (uint)bound) >> 32);
    }

    /// <summary>Returns <see langword="true"/> or <see langword="false"/> with equal probability.</summary>
    public bool NextBool() => (NextUInt64() >> 63) != 0;

    /// <summary>The SplitMix64 output function: a bijection of 64-bit values that spreads every input bit.</summary>
    /// <param name="z">The value to mix.</param>
    public static ulong Mix(ulong z)
    {
        unchecked
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
            return z ^ (z >> 31);
        }
    }
}
