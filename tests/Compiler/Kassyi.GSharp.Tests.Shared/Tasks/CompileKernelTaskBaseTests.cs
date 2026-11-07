#nullable enable
using Kassyi.GSharp.Compiler.Tasks;
using Microsoft.Build.Utilities;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Tasks;

[Collection(TestCollections.GlobalStateSequential)]
[Category(TestCategory.Compiler)]
public class CompileKernelTaskBaseTests
{
    static CompileKernelTaskBaseTests()
    {
        MSBuildLocatorSetup.EnsureRegistered();
    }

    [Fact]
    public void Test_CompileKernelTaskBase_ProcessSourceReturnsNull_Fails()
    {
        using var ctx = new CompileTaskTestContext();
        var task = new TestCompileKernelTask
        {
            OutputDirectory = ctx.TempDir,
            SourcesFunc = () => [new TaskItem("test.cs")],
            ProcessFunc = _ => null // Simulates failure
        };
        Assert.False(task.Execute());
    }

    [Fact]
    public void Test_CompileKernelTaskBase_SafeDeleteFiles_IgnoresExceptions()
    {
        var task = new TestCompileKernelTask();
        // [WHY] Intentionally pass null element to verify defensive guard against null file paths in SafeDeleteFiles.
        Assert.Null(Record.Exception(() => task.SafeDeleteFiles(null!, "")));

        // Invalid path that throws an exception inside File.Exists or File.Delete
        Assert.Null(Record.Exception(() => task.SafeDeleteFiles("||invalid_path<>?*")));
    }

    [Fact]
    public void Test_CompileKernelTaskBase_GetVcvarsEnvironment_CachesAMissingResult()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.Runner.OverrideVswhere = true;

        var task = new TestCompileKernelTask
        {
            ProcessRunner = ctx.Runner,
            Toolchain = ctx.CreateMsvcToolchain(disableCache: false),
            DisableVcvarsCache = false
        };

        var env1 = task.GetVcvarsEnvironment("missing_arch_cache_test");
        Assert.Null(env1);

        // Call again to hit the cache
        var env2 = task.GetVcvarsEnvironment("missing_arch_cache_test");
        Assert.Null(env2);

        // Assert that runner was only called once because the result (even null) is cached
        Assert.Single(ctx.Runner.Invocations);
    }

    [Fact]
    public void Test_CompileKernelTaskBase_GetVcvarsEnvironment_EmptyVswhereOutput()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.Runner.VswhereOutput = "   ";

        AssertNoVcvarsEnvironment(ctx);
    }

    [Fact]
    public void Test_CompileKernelTaskBase_GetVcvarsEnvironment_VcvarsallMissing()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.Runner.VswhereOutput = ctx.TempDir;

        AssertNoVcvarsEnvironment(ctx);
    }

    [Fact]
    public void Test_CompileKernelTaskBase_GetVcvarsEnvironment_CmdFails()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.Runner.CmdExitCode = 1;

        AssertNoVcvarsEnvironment(ctx);
    }

    private static void AssertNoVcvarsEnvironment(CompileTaskTestContext ctx)
    {
        var task = new TestCompileKernelTask
        {
            ProcessRunner = ctx.Runner,
            Toolchain = ctx.CreateMsvcToolchain(disableCache: true),
            DisableVcvarsCache = true
        };
        Assert.Null(task.GetVcvarsEnvironment("x64_fail_test"));
    }
}
