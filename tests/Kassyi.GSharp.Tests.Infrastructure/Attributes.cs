using Xunit.Sdk;

namespace Kassyi.GSharp.Tests.Infrastructure;

/// <summary>
/// Specifies the execution target environment (GPU vs CPU) for an xUnit test.
/// </summary>
[TraitDiscoverer("Kassyi.GSharp.Tests.Infrastructure.TestTargetDiscoverer", "Kassyi.GSharp.Tests.Infrastructure")]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class TestTargetAttribute(ExecutionTarget target) : Attribute, ITraitAttribute
{
    public ExecutionTarget Target { get; } = target;
}

/// <summary>
/// Specifies the functional subsystem or domain category for an xUnit test.
/// </summary>
[TraitDiscoverer("Kassyi.GSharp.Tests.Infrastructure.CategoryDiscoverer", "Kassyi.GSharp.Tests.Infrastructure")]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class CategoryAttribute(TestCategory category) : Attribute, ITraitAttribute
{
    public TestCategory Category { get; } = category;
}

/// <summary>
/// Specifies the behavioral scenario (HappyPath, SadPath, Boundary) for an xUnit test.
/// </summary>
[TraitDiscoverer("Kassyi.GSharp.Tests.Infrastructure.ScenarioDiscoverer", "Kassyi.GSharp.Tests.Infrastructure")]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class ScenarioAttribute(TestScenario scenario) : Attribute, ITraitAttribute
{
    public TestScenario Scenario { get; } = scenario;
}
