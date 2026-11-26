#nullable enable
using System.Linq;
using Xunit.Abstractions;

namespace Kassyi.GSharp.Tests.Infrastructure.Tests;

/// <summary>
/// Verifies <see cref="PlatformAttribute"/> and <see cref="PlatformDiscoverer"/> trait discovery without colliding with
/// <c>Category</c>.
/// </summary>
public sealed class PlatformTraitTests
{
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void PlatformDiscoverer_WindowsEnumArgument_EmitsOnlyPlatformTrait()
    {
        var discoverer = new PlatformDiscoverer();
        var traits = discoverer.GetTraits(new FakeAttributeInfo(TestPlatform.Windows)).ToList();

        var trait = Assert.Single(traits);
        Assert.Equal("Platform", trait.Key);
        Assert.Equal("Windows", trait.Value);
        Assert.DoesNotContain(traits, static pair => pair.Key == "Category");
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [InlineData((int)TestPlatform.Windows)]
    [InlineData("Windows")]
    public void PlatformDiscoverer_SerializedConstructorArguments_EmitsPlatformWindows(object arg)
    {
        var discoverer = new PlatformDiscoverer();
        var traits = discoverer.GetTraits(new FakeAttributeInfo(arg)).ToList();

        var trait = Assert.Single(traits);
        Assert.Equal("Platform", trait.Key);
        Assert.Equal("Windows", trait.Value);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void PlatformDiscoverer_UndefinedValue_EmitsNoTraits()
    {
        var discoverer = new PlatformDiscoverer();

        Assert.Empty(discoverer.GetTraits(new FakeAttributeInfo(999)));
        Assert.Empty(discoverer.GetTraits(new FakeAttributeInfo()));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void PlatformAttribute_ConstructedWithWindows_PreservesPlatformProperty()
    {
        var attribute = new PlatformAttribute(TestPlatform.Windows);

        Assert.Equal(TestPlatform.Windows, attribute.Platform);
    }

    private sealed class FakeAttributeInfo(params object?[] args) : IAttributeInfo
    {
        public IEnumerable<object?> GetConstructorArguments() => args;

        public IEnumerable<IAttributeInfo> GetCustomAttributes(string assemblyQualifiedAttributeTypeName) => [];

        public TValue GetNamedArgument<TValue>(string argumentName) =>
            default!; // Xunit.Abstractions.IAttributeInfo declares non-nullable TValue for missing named arguments.
    }
}
