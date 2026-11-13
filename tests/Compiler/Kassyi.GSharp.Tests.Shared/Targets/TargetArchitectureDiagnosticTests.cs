#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Targets;

/// <summary>
/// GSH0114, which the generator reports once per project with kernels when <c>GSharpTargetArchitectures</c> names a
/// compute capability that has no launch limits in G#'s table, and the generated output, which the setting leaves as
/// it is.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class TargetArchitectureDiagnosticTests
{
    private const string Id = "GSH0114";
    private const string ArchitecturesKey = "build_property.GSharpTargetArchitectures";

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Cuda")]
    [InlineData("Cpu")]
    public void Generator_UnknownComputeCapabilities_WarnsOnceWithoutALocation(string dialect)
    {
        var diagnostics = Run("Generator/AotConfiguration_MyKernel.cu.cs", dialect,
            "compute_86,sm_86;compute_130,sm_130;sm_72");

        var warning = Assert.Single(diagnostics, d => d.Id == Id);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(Location.None, warning.Location);
        Assert.Contains("sm_130, sm_72, compute_72", warning.GetMessage());
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(null)]
    [InlineData("compute_90,sm_90;sm_120f")]
    public void Generator_KnownComputeCapabilities_DoesNotWarn(string? architectures)
    {
        var diagnostics = Run("Generator/AotConfiguration_MyKernel.cu.cs", "Cuda", architectures);

        Assert.DoesNotContain(diagnostics, d => d.Id == Id);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Generator_UnknownComputeCapabilityWithoutKernels_DoesNotWarn()
    {
        var diagnostics = Run("Generator/AotManifest_NoKernels.cs", "Cuda", "compute_130,sm_130");

        Assert.DoesNotContain(diagnostics, d => d.Id == Id);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cuda")]
    [InlineData("Cpu")]
    public void Generator_TargetArchitectures_LeaveTheGeneratedSourcesAsTheyAre(string dialect)
    {
        var source = RoslynTestUtility.ReadTestCase("Generator/AotConfiguration_MyKernel.cu.cs");

        var byDefault = GeneratedSources(source, dialect, null);
        var hopper = GeneratedSources(source, dialect, "compute_90,sm_90");
        var unknown = GeneratedSources(source, dialect, "compute_130,sm_130");

        Assert.NotEmpty(byDefault);
        Assert.Equal(byDefault, hopper);
        Assert.Equal(byDefault, unknown);
    }

    private static ImmutableArray<Diagnostic> Run(string testCase, string dialect, string? architectures)
    {
        var source = RoslynTestUtility.ReadTestCase(testCase);
        var (_, _, diagnostics) = GeneratorTestRunner.RunGenerator(source, dialect, Options(architectures));
        return diagnostics;
    }

    private static string[] GeneratedSources(string source, string dialect, string? architectures)
    {
        var (driver, _, _) = GeneratorTestRunner.RunGenerator(source, dialect, Options(architectures));
        return driver.GetRunResult().GeneratedTrees
            .Select(tree => tree.FilePath + "\n" + tree.GetText())
            .ToArray();
    }

    private static Dictionary<string, string>? Options(string? architectures) =>
        architectures is null ? null : new Dictionary<string, string> { [ArchitecturesKey] = architectures };
}
