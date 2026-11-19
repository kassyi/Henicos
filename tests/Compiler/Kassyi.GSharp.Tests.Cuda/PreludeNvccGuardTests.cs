#nullable enable
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Tasks;
using Kassyi.GSharp.Tests.Shared.Tasks;
using Microsoft.Build.Utilities;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// Compiles the guard at the start of the generated CUDA prelude through <see cref="CompileCudaToFatbinTask"/> with
/// <c>__CUDACC_VER_MAJOR__</c> set to a chosen value, and checks the MSBuild error that a release below the minimum
/// (or a compiler that does not define the macro) produces.
/// </summary>
/// <remarks>
/// [WHY] Sequential: the other tests of the compile tasks set and clear <c>GSHARP_TEST_NO_CUDA</c>, which would make
/// this compilation skip. The thresholds 12 and 13 are written out on purpose: derived from
/// <c>NvccMinimum.MajorRelease</c>, a lowered constant would move the test with it and survive.
/// </remarks>
[Collection(TestCollections.GlobalStateSequential)]
[Category(TestCategory.Compiler)]
public sealed class PreludeNvccGuardTests
{
    static PreludeNvccGuardTests()
    {
        MSBuildLocatorSetup.EnsureRegistered();
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Guard_MajorBelowMinimum_ReportsToolkitErrorInBuildLog()
    {
        var (succeeded, errors) = Compile("#undef __CUDACC_VER_MAJOR__\n#define __CUDACC_VER_MAJOR__ 12\n");

        Assert.False(succeeded, "The build must stop with the guard's #error for release 12.");
        Assert.Contains(errors, static e => e.Contains("G# needs the CUDA Toolkit 13.0", StringComparison.Ordinal));
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Guard_MajorAtMinimum_CompilesWithoutError()
    {
        var (succeeded, errors) = Compile("#undef __CUDACC_VER_MAJOR__\n#define __CUDACC_VER_MAJOR__ 13\n");

        Assert.True(succeeded, string.Join(Environment.NewLine, errors));
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Guard_MajorNotDefined_ReportsThatNvccIsRequired()
    {
        var (succeeded, errors) = Compile("#undef __CUDACC_VER_MAJOR__\n");

        Assert.False(succeeded, "The build must stop with the guard's #error when the macro is not defined.");
        Assert.Contains(errors, static e => e.Contains("must be compiled by nvcc", StringComparison.Ordinal));
    }

    // Compiles the probe: the version lines, then the guard of the generated prelude, then an empty kernel so that nvcc
    // has device code to put into the fatbin. Skips the test when nvcc is not available.
    private static (bool Succeeded, List<string> Errors) Compile(string versionLines)
    {
        Skip.If(Environment.GetEnvironmentVariable("GSHARP_TEST_NO_CUDA") == "1", "GSHARP_TEST_NO_CUDA=1 skips nvcc.");
        var directory = Path.Combine(Path.GetTempPath(), "gsharp-prelude-nvcc-guard-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "guard_probe.cu");
            File.WriteAllText(source, versionLines + ExtractGuard(GSharpPreludeResources.Cuda) + "\n" + ProbeKernel);
            var engine = new MockBuildEngine();
            var task = new CompileCudaToFatbinTask
            {
                BuildEngine = engine,
                CudaSources = [new TaskItem(source)],
                OutputDirectory = directory,
                CombinedFatbinName = "guard_probe",
                TargetArchitectures = CompileCudaToFatbinTask.DefaultTargetArchitectures
            };
            var succeeded = task.Execute();

            Skip.If(engine.LoggedWarnings.Exists(static w => w.Contains("CUDA compiler (nvcc) was not found")),
                "nvcc is not available.");
            return (succeeded, engine.LoggedErrors);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private const string ProbeKernel = "__global__ void GuardProbeKernel() {}\n";

    // The guard is the part of the prelude before the first #endif
    private static string ExtractGuard(string cudaPrelude)
    {
        const string GuardStart = "#if !defined(__CUDACC_VER_MAJOR__)";
        const string EndIf = "#endif";
        Assert.StartsWith(GuardStart, cudaPrelude, StringComparison.Ordinal);
        var span = cudaPrelude.AsSpan();
        var endIfIndex = span.IndexOf(EndIf, StringComparison.Ordinal);
        Assert.True(endIfIndex >= 0, "Expected #endif closing the __CUDACC_VER_MAJOR__ guard.");
        return span[..(endIfIndex + EndIf.Length)].ToString();
    }
}
