#nullable enable
using System.Collections;
using System.IO;
using Kassyi.GSharp.Compiler.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.Diagnostics;
using Kassyi.GSharp.Compiler.Tasks.Helpers;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Kassyi.GSharp.Tests.Shared.Tasks;

[Collection(TestCollections.GlobalStateSequential)]
public class CompileKernelTaskBaseTests
{
    static CompileKernelTaskBaseTests()
    {
        MSBuildLocatorSetup.EnsureRegistered();
    }


    [Fact]
    [Category(TestCategory.Compiler)]
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
    [Category(TestCategory.Compiler)]
    public void Test_CompileKernelTaskBase_SafeDeleteFiles_IgnoresExceptions()
    {
        var task = new TestCompileKernelTask();
        // [WHY] Intentionally pass null element to verify defensive guard against null file paths in SafeDeleteFiles.
        Assert.Null(Record.Exception(() => task.SafeDeleteFiles(null!, "")));

        // Invalid path that throws an exception inside File.Exists or File.Delete
        Assert.Null(Record.Exception(() => task.SafeDeleteFiles("||invalid_path<>?*")));
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public void Test_CompileKernelTaskBase_VcvarsCache_And_MissingVswhere()
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

    [Theory]
    [InlineData("   ", 0)] // empty vswhere
    [InlineData("missing_bat_dir", 0)] // vcvarsall.bat not found
    [InlineData(null, 1)] // cmd.exe failed
    [Category(TestCategory.Compiler)]
    public void Test_CompileKernelTaskBase_GetVcvarsEnvironment_FailureCases(string? vswhereOutput, int cmdExitCode)
    {
        using var ctx = new CompileTaskTestContext();
        if (vswhereOutput == "missing_bat_dir")
        {
            ctx.Runner.VswhereOutput = ctx.TempDir;
        }
        else if (vswhereOutput != null)
        {
            ctx.Runner.VswhereOutput = vswhereOutput;
        }

        ctx.Runner.CmdExitCode = cmdExitCode;

        var task = new TestCompileKernelTask
        {
            ProcessRunner = ctx.Runner,
            Toolchain = ctx.CreateMsvcToolchain(disableCache: true),
            DisableVcvarsCache = true
        };
        Assert.Null(task.GetVcvarsEnvironment("x64_fail_test"));
    }
}
