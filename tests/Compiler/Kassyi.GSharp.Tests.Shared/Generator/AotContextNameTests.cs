using Kassyi.GSharp.Tests.Shared.Generator.AotContextName;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// Verifies that two structs which would share one <c>AotContexts</c> name are reported instead of failing in the
/// generated code with CS0102.
/// </summary>
[Category(TestCategory.Compiler)]
public class AotContextNameTests
{
    // The sources are test cases that must not build with the test project, so they are read from TestCases. The
    // analyzer reports the second struct at its declaration; the generator reports nothing.
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("AotContextName_Namespaces.cs", "Cpu", 4, 30, 4, 34)]
    [InlineData("AotContextName_Namespaces.cs", "Cuda", 4, 30, 4, 34)]
    [InlineData("AotContextName_ContextSuffix.cs", "Cpu", 7, 19, 7, 22)]
    [InlineData("AotContextName_ContextSuffix.cs", "Cuda", 7, 19, 7, 22)]
    [InlineData("AotContextName_GenericConstructions.cs", "Cpu", 5, 19, 5, 22)]
    [InlineData("AotContextName_GenericConstructions.cs", "Cuda", 5, 19, 5, 22)]
    public async Task Test_StructsWithSameName_ReportGsh0058(string file, string dialect, int startLine, int startColumn,
        int endLine, int endColumn)
    {
        var source = RoslynTestUtility.ReadTestCase(Path.Combine("Generator", file));

        var diagnostics = await KernelDiagnostics.RunAsync(source, dialect);
        Assert.Equal(
            [(DiagnosticOrigin.Analyzer, startLine, startColumn, endLine, endColumn)],
            diagnostics.Ranges(GSharpDiagnostics.DuplicateAotContextName.Id));

        // The second struct gets no helper, so the generated code does not add CS0102 on top of the report.
        var (_, output, _) = RoslynTestUtility.RunGenerator(source, dialect);
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
