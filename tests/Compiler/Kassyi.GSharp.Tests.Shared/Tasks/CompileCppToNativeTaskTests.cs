#nullable enable
using Kassyi.GSharp.Compiler.Tasks;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Tasks;

[Collection(TestCollections.GlobalStateSequential)]
[Category(TestCategory.Compiler)]
public class CompileCppToNativeTaskTests
{
    static CompileCppToNativeTaskTests()
    {
        MSBuildLocatorSetup.EnsureRegistered();
    }

    [Fact]
    public void Test_CompileCppToNativeTask_GSharpMultiTarget_CompilesForTwoArchitectures()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cpp", "// source");

        var task = ctx.CreateCppTask(["kernel.cpp"]);
        task.GSharpMultiTarget = true;
        task.MockCompilation = true;

        Assert.True(task.Execute());

        Assert.Equal(2, task.OutputDlls.Length);
        Assert.Contains(task.OutputDlls, d => d.ItemSpec.EndsWith("_x64.dll"));
        Assert.Contains(task.OutputDlls, d => d.ItemSpec.EndsWith("_arm64.dll"));
    }

    [Fact]
    public void Test_CompileCppToNativeTask_ClExePathResolvedFromPath()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cpp", "// source");

        var dummyClDir = Path.Combine(ctx.TempDir, "FakeMSVC");
        Directory.CreateDirectory(dummyClDir);
        File.WriteAllText(Path.Combine(dummyClDir, "cl.exe"), "");

        ctx.Runner.CmdStandardOutput =
            $"INCLUDE=C:\\dummy\r\nLIB=C:\\dummy\r\nPATH={dummyClDir}{Path.PathSeparator}C:\\Windows\\System32";

        var task = ctx.CreateCppTask(["kernel.cpp"]);
        task.MockCompilation = false;
        ctx.Runner.SimulateCompilationOutput = true;

        Assert.True(task.Execute());

        Assert.Contains(ctx.Runner.Invocations, i => i.FileName == Path.Combine(dummyClDir, "cl.exe"));
    }

    [Theory]
    [InlineData("g++")]
    [InlineData("clang++")]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CompileCppToNativeTask_GccClangToolchain_CompilesSharedLibrary(string compiler)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cpp", "// source");

        var toolchain = ctx.CreateGccClangToolchain(compiler);
        var task = ctx.CreateCppTask(["kernel.cpp"], toolchain);

        Assert.True(task.Execute());
        Assert.Single(task.OutputDlls);
        Assert.DoesNotContain(ctx.Runner.Invocations,
            i => i.FileName.EndsWith("vswhere.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(ctx.Runner.Invocations,
            i => i.FileName == compiler &&
                 i.Arguments.Contains("-shared") &&
                 i.Arguments.Contains("-fPIC") &&
                 i.Arguments.Contains("-fopenmp"));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_CompileCppToNativeTask_GccClangToolchain_WhenCompilerMissing_SkipsWithWarningAndSucceeds()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cpp", "// source");
        ctx.Runner.VersionCheckExitCode = 1;

        var toolchain = ctx.CreateGccClangToolchain("missing_g++");
        var task = ctx.CreateCppTask(["kernel.cpp"], toolchain);

        Assert.True(task.Execute());
        Assert.Empty(task.OutputDlls);
        Assert.Empty(ctx.BuildEngine.LoggedErrors);
        Assert.Contains(ctx.BuildEngine.LoggedWarnings,
            w => w.Contains("C++ compiler was not found") && w.Contains("Skipping native CPU kernel compilation"));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_CompileCppToNativeTask_GccClangToolchain_CompilationFails_LogsErrorAndReturnsFalse()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cpp", "// source");
        ctx.Runner.NextExitCode = 1;
        ctx.Runner.NextStandardError = "g++: error: unrecognized command-line option";

        var toolchain = ctx.CreateGccClangToolchain("g++");
        var task = ctx.CreateCppTask(["kernel.cpp"], toolchain);

        Assert.False(task.Execute());
        Assert.Contains(ctx.BuildEngine.LoggedErrors, e => e.Contains("g++ compilation failed with exit code 1"));
        Assert.Contains(ctx.BuildEngine.LoggedErrors, e => e.Contains("unrecognized command-line option"));
    }
}
