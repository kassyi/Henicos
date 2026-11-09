using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

internal static class SpanSortTestHelpers
{
    public const uint GridDimX = 2;
    public const uint BlockDimX = 32;

    // Lengths around the insertion sort threshold (16), the 2- and 3-element paths and larger partitions
    private static readonly int[] s_lengths = [1, 2, 3, 4, 15, 16, 17, 31, 32, 33, 64, 100, 1000];

    public static TheoryData<int, SortPattern, bool> IntCases()
    {
        var data = new TheoryData<int, SortPattern, bool>();
        foreach (var managed in new[] { false, true })
        {
            foreach (var length in s_lengths)
            {
                foreach (var pattern in Enum.GetValues<SortPattern>())
                {
                    data.Add(length, pattern, managed);
                }
            }
        }

        return data;
    }

    public static TheoryData<int, bool> TypedCases()
    {
        var data = new TheoryData<int, bool>();
        foreach (var managed in new[] { false, true })
        {
            foreach (var length in new[] { 1, 2, 3, 16, 17, 33, 100, 1000 })
            {
                data.Add(length, managed);
            }
        }

        return data;
    }

    public static TheoryData<bool> Paths() => new() { false, true };

    public static void Run(bool isCudaDialect, bool managed, Action launch)
    {
        Skip.If(managed && isCudaDialect, "The managed path runs on the CPU test assembly.");
#if GSHARP_CPU
        if (managed)
        {
            ManagedKernelRun.Run(launch);
            return;
        }
#endif
        launch();
    }

    public static void AssertSameAsDotNet<T>(T[] input, T[] actual, string what)
    {
        var expected = (T[])input.Clone();
        MemoryExtensions.Sort(expected.AsSpan());

        // (1) the order of .NET, element by element
        Assert.True(expected.AsSpan().SequenceEqual(actual),
            $"{what}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");

        // (2) ascending by the default comparer, which does not depend on the order .NET picks among equal keys
        var comparer = Comparer<T>.Default;
        for (var i = 1; i < actual.Length; i++)
        {
            Assert.True(comparer.Compare(actual[i - 1], actual[i]) <= 0,
                $"{what}: elements {i - 1} and {i} are out of order");
        }
    }

    public static void AssertSameOrder<T>(T[] expected, T[] actual, Comparison<T> comparison, string what)
    {
        // (1) the order of .NET with the same comparison, element by element (equal keys included)
        Assert.True(expected.AsSpan().SequenceEqual(actual),
            $"{what}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");

        // (2) ascending by the comparison
        for (var i = 1; i < actual.Length; i++)
        {
            Assert.True(comparison(actual[i - 1], actual[i]) <= 0,
                $"{what}: elements {i - 1} and {i} are out of order");
        }
    }

    public static void AssertSamePairs<TKey>(TKey[] keys, int[] items, TKey[] expectedKeys, int[] expectedItems,
        TKey[] actualKeys, int[] actualItems, Comparison<TKey> comparison, string what)
    {
        // (1) keys and items in the order of .NET
        AssertSameOrder(expectedKeys, actualKeys, comparison, what);
        Assert.True(expectedItems.AsSpan().SequenceEqual(actualItems),
            $"{what}: items expected [{string.Join(", ", expectedItems)}], got [{string.Join(", ", actualItems)}]");

        // (3) each item still sits with its own key (items are the input positions): a permutation of the input pairs
        for (var i = 0; i < actualItems.Length; i++)
        {
            Assert.Equal(keys[actualItems[i]], actualKeys[i]);
        }

        Assert.Equal(items.Length, actualItems.Distinct().Count());
    }

    // Equal keys of floating point (-0.0 and 0.0, NaNs with different payloads) differ in their bits, so the bits show
    // whether the port moves them exactly as .NET does
    public static void AssertSameBits<T, TBits>(T[] input, T[] actual, Func<T, TBits> bits, string what)
        where TBits : IEquatable<TBits>
    {
        var expected = (T[])input.Clone();
        MemoryExtensions.Sort(expected.AsSpan());
        var expectedBits = Array.ConvertAll(expected, x => bits(x));
        var actualBits = Array.ConvertAll(actual, x => bits(x));
        Assert.True(expectedBits.AsSpan().SequenceEqual(actualBits),
            $"{what}: expected [{string.Join(", ", expectedBits)}], got [{string.Join(", ", actualBits)}]");
    }
}
