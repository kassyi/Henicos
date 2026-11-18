#nullable enable
using System.Diagnostics;
using Kassyi.GSharp.Compiler.Tasks;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Tasks;

/// <summary>
/// The CUDA release check of <see cref="CompileCudaToFatbinTask"/>: an nvcc older than CUDA 13.0 stops the task with
/// error GS0006 before it compiles anything.
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
[Category(TestCategory.Compiler)]
public class CompileCudaToFatbinTaskNvccVersionTests
{
    private const string TooOldErrorCode = "GS0006";

    static CompileCudaToFatbinTaskNvccVersionTests()
    {
        MSBuildLocatorSetup.EnsureRegistered();
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("12.0", "V12.0.140")]
    [InlineData("11.8", "V11.8.89")]
    public void Execute_NvccOlderThanCuda13_FailsWithGs0006BeforeCompiling(string release, string build)
    {
        using var ctx = new CompileTaskTestContext();
        var task = CreateTask(ctx, release, build);

        Assert.False(task.Execute());

        Assert.Equal([TooOldErrorCode], ctx.BuildEngine.LoggedErrorCodes);
        var message = Assert.Single(ctx.BuildEngine.LoggedErrors);
        Assert.Contains("CUDA Toolkit 13.0 or later", message, StringComparison.Ordinal);
        Assert.Contains($"is CUDA {release}.", message, StringComparison.Ordinal);
        Assert.DoesNotContain(ctx.Runner.Invocations, IsNvccFatbinRun);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("13.0", "V13.0.48")]
    [InlineData("13.4", "V13.4.59")]
    [InlineData("14.1", "V14.1.10")]
    public void Execute_NvccOfCuda13OrLater_CompilesTheFatbin(string release, string build)
    {
        using var ctx = new CompileTaskTestContext();
        var task = CreateTask(ctx, release, build);

        Assert.True(task.Execute());

        Assert.Empty(ctx.BuildEngine.LoggedErrorCodes);
        Assert.Contains(ctx.Runner.Invocations, IsNvccFatbinRun);
    }

    // An output without a readable "release major.minor" (a wrapper script, a changed format) is not rejected.
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("compiler version check ok")]
    [InlineData("Cuda compilation tools, release 12, V12\nBuild cuda_12")]
    [InlineData("Cuda compilation tools, release , V12.0.140")]
    public void Execute_VersionOutputWithoutAReleaseNumber_CompilesTheFatbin(string versionOutput)
    {
        using var ctx = new CompileTaskTestContext();
        var task = CreateTask(ctx, versionOutput);

        Assert.True(task.Execute());

        Assert.Empty(ctx.BuildEngine.LoggedErrorCodes);
        Assert.Contains(ctx.Runner.Invocations, IsNvccFatbinRun);
    }

    private static CompileCudaToFatbinTask CreateTask(CompileTaskTestContext ctx, string release, string build) =>
        CreateTask(
            ctx,
            "nvcc: NVIDIA (R) Cuda compiler driver\n" +
            $"Cuda compilation tools, release {release}, {build}\n" +
            $"Build cuda_{release}.r{release}/compiler.1");

    private static CompileCudaToFatbinTask CreateTask(CompileTaskTestContext ctx, string versionOutput)
    {
        ctx.SetEnv("GSHARP_TEST_NO_CUDA", null);
        var source = ctx.CreateDummySource("kernel1.cu", "// cuda source");
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(-10));
        ctx.Runner.VersionCheckOutput = versionOutput;
        return ctx.CreateCudaTask(["kernel1.cu"], combinedName: "");
    }

    private static bool IsNvccFatbinRun(ProcessStartInfo invocation) =>
        invocation.FileName.Contains("nvcc") && invocation.Arguments.Contains("--fatbin");
}
