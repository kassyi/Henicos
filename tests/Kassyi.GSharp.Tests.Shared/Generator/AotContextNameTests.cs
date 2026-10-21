using Kassyi.GSharp.Tests.Shared.Generator.AotContextName;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// Verifies that two structs which would share one <c>AotContexts</c> name are reported instead of failing in the
/// generated code with CS0102.
/// </summary>
[Category(TestCategory.Compiler)]
public class AotContextNameTests
{
    // The sources are test cases that must not build with the test project, so they are read from TestCases.
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("AotContextName_Namespaces.cs", "Cpu")]
    [InlineData("AotContextName_Namespaces.cs", "Cuda")]
    [InlineData("AotContextName_ContextSuffix.cs", "Cpu")]
    [InlineData("AotContextName_ContextSuffix.cs", "Cuda")]
    [InlineData("AotContextName_GenericConstructions.cs", "Cpu")]
    [InlineData("AotContextName_GenericConstructions.cs", "Cuda")]
    public void Test_StructsWithSameName_ReportGsh0060(string file, string dialect)
    {
        var source = File.ReadAllText(Path.Combine(RoslynTestUtility.GetTestCasesDirectory(), "Generator", file));

        var (_, output, diagnostics) = RoslynTestUtility.RunGenerator(source, dialect);

        diagnostics.AssertContains(GSharpDiagnostics.DuplicateAotContextName);
        // The second struct gets no helper, so the generated code does not add CS0102 on top of the report.
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Id == "CS0102");
    }

    // A struct used by several kernels, and a generic struct used with one type argument, each keep one readable name.
    // The project builds only when the generated AotContexts members are unique.
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_OneNameUsedTwice_KeepsReadableNames()
    {
        Assert.Equal("Pair", nameof(AotContextNameKernels.AotContexts.Pair));
        Assert.Equal("Box", nameof(AotContextNameKernels.AotContexts.Box));
    }
}
