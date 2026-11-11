#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public class DifferentialRandomTests
{
    // ---- DiffRandom ----

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void DiffRandom_SplitMix64_ProducesThePublishedSequence()
    {
        // The reference outputs of SplitMix64 (Vigna's splitmix64.c) for seeds 0 and 1234567, so a seed printed by a
        // failure reproduces on any machine and in any port of the generator
        // mutant: DiffRandom.cs Mix `z ^ (z >> 30)` -> `z ^ (z >> 31)`
        var zero = new DiffRandom(0);
        Assert.Equal(0xE220A8397B1DCDAFul, zero.NextUInt64());
        Assert.Equal(0x6E789E6AA1B965F4ul, zero.NextUInt64());
        Assert.Equal(0x06C45D188009454Ful, zero.NextUInt64());

        var other = new DiffRandom(1234567);
        Assert.Equal(6457827717110365317ul, other.NextUInt64());
        Assert.Equal(3203168211198807973ul, other.NextUInt64());
        Assert.Equal(9817491932198370423ul, other.NextUInt64());
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void DiffRandom_NextBelow_StaysInRange()
    {
        // NextBelow(n) is in [0, n) and reaches both ends for a small n
        // mutant: DiffRandom.cs NextBelow `>> 32` -> `>> 31`
        var rng = new DiffRandom(7);
        var seen = new bool[3];
        for (int i = 0; i < 1000; i++)
        {
            int v = rng.NextBelow(3);
            Assert.InRange(v, 0, 2);
            seen[v] = true;
        }

        Assert.All(seen, Assert.True);
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextBelow(0));
    }

    // ---- DiffValues ----

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void DiffValues_SeedZero_IsEdgeCrossProduct()
    {
        int n = DiffValues.Int32Boundaries.Length;
        var (left, right) = DiffValues.Int32Pairs(new DiffRandom(0), n * n);

        // Fixed rows: row r is (B[r / n], B[r % n]), so a failure's row number names the same inputs in every run
        // mutant: DiffValues.cs Pairs `boundaries[r / n % n]` <-> `boundaries[r % n]` (left and right swapped)
        Assert.Equal((0, 0), (left[0], right[0]));
        Assert.Equal((0, 1), (left[1], right[1]));
        Assert.Equal((1, 0), (left[n], right[n]));
        Assert.Equal((int.MaxValue, int.MinValue), (left[5 * n + 6], right[5 * n + 6]));
        Assert.Equal((65, 65), (left[n * n - 1], right[n * n - 1]));

        // Every pair of boundaries appears exactly once
        // mutant: DiffValues.cs Pairs `boundaries[r / n % n]` -> `boundaries[r % n]`
        Assert.Equal(n * n, left.Zip(right).Distinct().Count());

        // Single values repeat the table in order
        Assert.Equal(DiffValues.SingleBoundaries.Select(BitConverter.SingleToUInt32Bits),
            DiffValues.Single(new DiffRandom(0), DiffValues.SingleBoundaries.Length).Select(BitConverter.SingleToUInt32Bits));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void DiffValues_RandomSeeds_AreDeterministicAndMixEdgesWithRandomBits()
    {
        // The same seed gives the same values; another seed gives other values
        // mutant: DiffValues.cs Values `rng.IsBoundarySeed ?` -> `true ?` (every seed returns the boundary table)
        var first = DiffValues.Int32(new DiffRandom(3), 400);
        Assert.Equal(first, DiffValues.Int32(new DiffRandom(3), 400));
        Assert.NotEqual(first, DiffValues.Int32(new DiffRandom(4), 400));

        // Both boundaries and values outside the table occur (each with probability about 1/2)
        // mutant: DiffValues.cs NextInt32 `rng.NextBool() ?` -> `false ?`
        var boundaries = DiffValues.Int32Boundaries.ToHashSet();
        Assert.InRange(first.Count(boundaries.Contains), 150, 250);

        var singles = DiffValues.Single(new DiffRandom(3), 400);
        Assert.Equal(singles.Select(BitConverter.SingleToUInt32Bits), DiffValues.Single(new DiffRandom(3), 400).Select(BitConverter.SingleToUInt32Bits));
        Assert.Contains(singles, static v => !DiffValues.SingleBoundaries.Contains(v));
    }

    // ---- Seeds ----

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void DifferentialSeeds_TestSeed_AddsOneDerivedSeed()
    {
        // Without TEST_SEED the seeds are 0 to 8 (no environment dependence); with it, one more seed of at least 9
        // that the same TEST_SEED always gives
        // mutant: DifferentialSeeds.cs For `[.. Fixed, derived]` -> `Fixed`
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8], DifferentialSeeds.For(null));
        Assert.Equal(DifferentialSeeds.Fixed, DifferentialSeeds.For(" "));

        var withSeed = DifferentialSeeds.For("42");
        Assert.Equal(10, withSeed.Count);
        Assert.Equal(DifferentialSeeds.Fixed, withSeed.Take(9));
        Assert.True(withSeed[9] >= DifferentialSeeds.FirstDerivedSeed);
        Assert.Equal(withSeed, DifferentialSeeds.For("42"));

        // Consecutive TEST_SEED values (test-stability iterations) give different seeds; a non-integer is hashed stably
        // mutant: DifferentialSeeds.cs Derive `DiffRandom.Mix(value) % Range` -> `0`
        Assert.NotEqual(DifferentialSeeds.Derive("42"), DifferentialSeeds.Derive("43"));
        Assert.Equal(DifferentialSeeds.Derive("random"), DifferentialSeeds.Derive("random"));
        Assert.True(DifferentialSeeds.Derive("-1") >= DifferentialSeeds.FirstDerivedSeed);
    }
}
