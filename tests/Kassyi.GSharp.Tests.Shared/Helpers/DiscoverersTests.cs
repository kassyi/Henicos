#nullable enable
using Xunit.Abstractions;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class DiscoverersTests
{
    private sealed class FakeAttributeInfo(params object?[] args) : IAttributeInfo
    {
        public IEnumerable<object?> GetConstructorArguments() => args;
        public IEnumerable<IAttributeInfo> GetCustomAttributes(string assemblyQualifiedAttributeTypeName) => [];
        public TValue GetNamedArgument<TValue>(string argumentName) => default!;
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetTraits_ValidEnumAndIntValues_EmitsExpectedTraits()
    {
        var targetDiscoverer = new TestTargetDiscoverer();
        var targetTraits = targetDiscoverer.GetTraits(new FakeAttributeInfo(ExecutionTarget.GpuExecution)).ToList();
        Assert.Equal(2, targetTraits.Count);
        Assert.Contains(new KeyValuePair<string, string>("TestTarget", "GpuExecution"), targetTraits);
        Assert.Contains(new KeyValuePair<string, string>("Category", "GpuExecution"), targetTraits);

        var categoryDiscoverer = new CategoryDiscoverer();
        var categoryTraits = categoryDiscoverer.GetTraits(new FakeAttributeInfo((int)TestCategory.Runtime)).ToList();
        Assert.Single(categoryTraits);
        Assert.Equal(new KeyValuePair<string, string>("Category", "Runtime"), categoryTraits[0]);

        var scenarioDiscoverer = new ScenarioDiscoverer();
        var scenarioTraits = scenarioDiscoverer.GetTraits(new FakeAttributeInfo("Boundary")).ToList();
        Assert.Equal(2, scenarioTraits.Count);
        Assert.Contains(new KeyValuePair<string, string>("Scenario", "Boundary"), scenarioTraits);
        Assert.Contains(new KeyValuePair<string, string>("Category", "Boundary"), scenarioTraits);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetTraits_EmptyOrNullArguments_ReturnsEmptySequence()
    {
        var discoverer = new CategoryDiscoverer();

        Assert.Empty(discoverer.GetTraits(new FakeAttributeInfo()));
        Assert.Empty(discoverer.GetTraits(new FakeAttributeInfo([null])));
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(-1)]
    [InlineData(99999)]
    public void GetTraits_UndefinedEnumIntegerOrInvalidType_ReturnsEmptySequence(int invalidEnumValue)
    {
        var targetDiscoverer = new TestTargetDiscoverer();
        var categoryDiscoverer = new CategoryDiscoverer();
        var scenarioDiscoverer = new ScenarioDiscoverer();

        Assert.Empty(targetDiscoverer.GetTraits(new FakeAttributeInfo(invalidEnumValue)));
        Assert.Empty(targetDiscoverer.GetTraits(new FakeAttributeInfo((ExecutionTarget)invalidEnumValue)));
        Assert.Empty(categoryDiscoverer.GetTraits(new FakeAttributeInfo(invalidEnumValue)));
        Assert.Empty(scenarioDiscoverer.GetTraits(new FakeAttributeInfo("NonExistentScenario")));
    }
}
