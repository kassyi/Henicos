#nullable enable
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Tests of <see cref="DiffPaths"/> that depend on how the test assembly was built.
/// </summary>
public class DiffPathsTests
{
    private const string CheckedBuildKey = "GSharpCheckedBuild";

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void IsCudaCheckedBuild_AssemblyMetadataPresent_MatchesTheBuildProperty()
    {
        // Without the metadata every build reads as release, and the checked CUDA cells never run
        // mutant: remove the AssemblyMetadata item of Kassyi.GSharp.Tests.Shared.projitems
        var values = typeof(DiffPaths).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(static a => string.Equals(a.Key, CheckedBuildKey, StringComparison.OrdinalIgnoreCase))
            .Select(static a => a.Value)
            .ToArray();

        var value = Assert.Single(values);
        Assert.True(
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "false", StringComparison.OrdinalIgnoreCase),
            $"{CheckedBuildKey} metadata is '{value}', expected true or false");
        Assert.Equal(string.Equals(value, "true", StringComparison.OrdinalIgnoreCase), DiffPaths.IsCudaCheckedBuild);
    }
}
