namespace Kassyi.GSharp.Tests.Shared.Generators;

/// <summary>
/// Kernel parameters and sibling types named after host-wrapper locals, helper members, or unqualified BCL/runtime
/// types must still produce a host wrapper that compiles without name collisions (#406).
/// </summary>
[Category(TestCategory.Compiler)]
public class HostWrapperNameCollisionGeneratorTests
{
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_HostWrapperNameCollisions_GenerateCompilableWrappers(string dialect)
    {
        var testConfig = RoslynTestUtility.TestGenerator("Generators/GpuKernel_HostWrapperNames.cu.cs")
            .WithDialect(dialect);

        await testConfig.HasNoErrorsAsync();
    }
}
