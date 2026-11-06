#nullable enable
using System.Collections.Immutable;

namespace Kassyi.GSharp.Tools.SmellScan.Tests;

/// <summary>Small conversions that keep assertions readable.</summary>
internal static class TestValues
{
    /// <summary>Copies an immutable array to an array, which xunit compares element by element.</summary>
    internal static string[] Strings(ImmutableArray<string> values) => [.. values];

    /// <summary>Copies an immutable array of integers to an array.</summary>
    internal static int[] Ints(ImmutableArray<int> values) => [.. values];
}
