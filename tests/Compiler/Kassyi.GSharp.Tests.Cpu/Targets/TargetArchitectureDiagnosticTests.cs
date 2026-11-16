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
    public void Generator_TargetArchitectures_SetTheSharedMemoryLimitOfTheLaunchers(string dialect)
    {
        var source = RoslynTestUtility.ReadTestCase("Generator/AotConfiguration_MyKernel.cu.cs");

        var byDefault = string.Join("\n", GeneratedSources(source, dialect, null));
        var hopper = string.Join("\n", GeneratedSources(source, dialect, "compute_90,sm_90"));
        var blackwell107 = string.Join("\n", GeneratedSources(source, dialect, "sm_107a"));
        var unknown = string.Join("\n", GeneratedSources(source, dialect, "compute_130,sm_130"));

        const string check = "global::Kassyi.GSharp.Runtime.LaunchLimits.CheckTargetSharedMemory(sharedMemBytes, 0, ";
        const string defaultCall = check + "101376, \"sm_86\", \"MyKernel.Run\");";
        Assert.Contains(defaultCall, byDefault);
        Assert.Contains(check + "232448, \"sm_90\", \"MyKernel.Run\");", hopper);
        Assert.Contains(check + "232448, \"sm_107\", \"MyKernel.Run\");", blackwell107);
        Assert.DoesNotContain("CheckTargetSharedMemory", unknown);

        // LaunchRun comes first, then LaunchRunPtr
        var ptrIndex = byDefault.IndexOf("public static unsafe void LaunchRunPtr(", StringComparison.Ordinal);
        Assert.True(ptrIndex > 0);
        var callIndex = byDefault.IndexOf(defaultCall, StringComparison.Ordinal);
        Assert.InRange(callIndex, 0, ptrIndex);
        var ptrCallIndex = byDefault.IndexOf(defaultCall, ptrIndex, StringComparison.Ordinal);
        if (dialect == "Cuda")
        {
            // In the C# execution branch of LaunchRun, before the kernel runs; the pointer launcher's branch throws
            var branchIndex = byDefault.IndexOf(
                "if (global::Kassyi.GSharp.Runtime.GpuExecution.Current == " +
                "global::Kassyi.GSharp.Runtime.KernelExecution.CSharp)",
                StringComparison.Ordinal);
            var runnerIndex = byDefault.IndexOf(
                "global::Kassyi.GSharp.Runtime.Aot.CSharpKernelRunner.Run(", StringComparison.Ordinal);
            Assert.True(branchIndex >= 0 && callIndex > branchIndex && runnerIndex > callIndex);
            Assert.Equal(-1, ptrCallIndex);
        }
        else
        {
            Assert.True(ptrCallIndex > ptrIndex);
        }
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
