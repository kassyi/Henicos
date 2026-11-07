#nullable enable
using Kassyi.GSharp.Compiler.Tasks;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Tasks;

[Collection(TestCollections.GlobalStateSequential)]
[Category(TestCategory.Compiler)]
[Scenario(TestScenario.SadPath)]
public class ToolchainTests
{
    static ToolchainTests()
    {
        MSBuildLocatorSetup.EnsureRegistered();
    }

    [Fact]
    public void Test_MsvcToolchain_ExitCodeZeroWithC4068_LogsMsBuildWarning()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cpp", "// source");
        ctx.Runner.NextStandardOutput = @"C:\temp\kernel.cpp(42): warning C4068: unknown pragma 'unroll'";

        var task = ctx.CreateCppTask(["kernel.cpp"], ctx.CreateMsvcToolchain());
        Assert.True(task.Execute());
        Assert.Contains(ctx.BuildEngine.LoggedWarnings, w => w.Contains("unknown pragma 'unroll'"));
    }

    [Fact]
    public void Test_GccClangToolchain_ExitCodeZeroWithWarning_LogsMsBuildWarning()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cpp", "// source");
        ctx.Runner.NextStandardError = "kernel.cpp:42:5: warning: label 'unused' defined but not used [-Wunused-label]";

        var task = ctx.CreateCppTask(["kernel.cpp"], ctx.CreateGccClangToolchain("g++"));
        Assert.True(task.Execute());
        Assert.Contains(ctx.BuildEngine.LoggedWarnings, w => w.Contains("-Wunused-label"));
    }
}
