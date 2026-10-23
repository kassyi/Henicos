namespace Kassyi.GSharp.Tests.Shared.Generators;

/// <summary>Kernel parameters named after C# keywords must still give a host wrapper that compiles.</summary>
[Category(TestCategory.Compiler)]
public class KeywordParameterNameGeneratorTests
{
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_KeywordParameterNames_GenerateCompilableWrappers(string dialect)
    {
        var testConfig = RoslynTestUtility.TestGenerator("Generators/GpuKernel_KeywordParameterNames.cu.cs")
            .WithDialect(dialect);

        await testConfig.HasNoErrorsAsync();
    }
}
