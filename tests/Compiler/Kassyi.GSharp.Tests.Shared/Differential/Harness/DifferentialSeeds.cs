#nullable enable
using System.Globalization;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// The seeds of a differential test (design D3): 0 to 8, fixed in code, plus one seed derived from <c>TEST_SEED</c> when
/// it is set. <c>just test-stability</c> sets <c>TEST_SEED</c> per iteration, so a long run tries new inputs, and
/// <c>just test-stability 1 seed=&lt;N&gt;</c> repeats an iteration. Without <c>TEST_SEED</c> the fixed seeds alone decide
/// the result (property-test-design §6).
/// </summary>
public static class DifferentialSeeds
{
    /// <summary>The environment variable that <c>just test-stability</c> sets.</summary>
    public const string TestSeedVariable = "TEST_SEED";

    /// <summary>The first seed that <see cref="Derive"/> can return; the fixed seeds are below it.</summary>
    public const int FirstDerivedSeed = 9;

    /// <summary>Gets the fixed seeds: 0 (the boundary tables) and 1 to 8.</summary>
    public static IReadOnlyList<int> Fixed { get; } = [0, 1, 2, 3, 4, 5, 6, 7, 8];

    /// <summary>Gets the value of <c>TEST_SEED</c>, or <see langword="null"/>.</summary>
    public static string? TestSeed => Environment.GetEnvironmentVariable(TestSeedVariable);

    /// <summary>Returns the fixed seeds, plus the seed derived from <paramref name="testSeed"/> when it is set.</summary>
    /// <param name="testSeed">The value of <c>TEST_SEED</c>, or <see langword="null"/>.</param>
    public static IReadOnlyList<int> For(string? testSeed) =>
        Derive(testSeed) is { } derived ? [.. Fixed, derived] : Fixed;

    /// <summary>
    /// Returns the seed derived from <paramref name="testSeed"/>, in <c>[9, int.MaxValue]</c>, or <see langword="null"/>
    /// when it is not set. An integer is mixed by <see cref="DiffRandom.Mix"/>; any other text (<c>random</c>) is first
    /// hashed with FNV-1a, so the same text always gives the same seed.
    /// </summary>
    /// <param name="testSeed">The value of <c>TEST_SEED</c>.</param>
    public static int? Derive(string? testSeed)
    {
        if (string.IsNullOrWhiteSpace(testSeed))
        {
            return null;
        }

        ulong value = int.TryParse(testSeed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? unchecked((ulong)parsed)
            : Fnv1a(testSeed);
        const ulong Range = (ulong)int.MaxValue - FirstDerivedSeed + 1;
        return FirstDerivedSeed + (int)(DiffRandom.Mix(value) % Range);
    }

    private static ulong Fnv1a(string text)
    {
        ulong hash = 0xCBF29CE484222325ul;
        foreach (char c in text)
        {
            hash = unchecked((hash ^ c) * 0x100000001B3ul);
        }

        return hash;
    }
}
