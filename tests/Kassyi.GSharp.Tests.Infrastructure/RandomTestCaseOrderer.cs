using System;
using System.Collections.Generic;
using System.Linq;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Kassyi.GSharp.Tests.Infrastructure;

/// <summary>
/// Shuffles test cases execution order when TEST_SEED environment variable is provided.
/// If TEST_SEED is not set, preserves the original test case order.
/// </summary>
public class RandomTestCaseOrderer : ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase
    {
        var seedStr = Environment.GetEnvironmentVariable("TEST_SEED");
        if (string.IsNullOrEmpty(seedStr))
        {
            return testCases;
        }

        int seed;
        if (string.Equals(seedStr, "random", StringComparison.OrdinalIgnoreCase))
        {
            seed = unchecked((int)DateTime.UtcNow.Ticks);
        }
        else if (!int.TryParse(seedStr, out seed))
        {
            seed = seedStr.GetHashCode();
        }

        var rng = new Random(seed);
        return testCases.OrderBy(_ => rng.Next()).ToList();
    }
}
