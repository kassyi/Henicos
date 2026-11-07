#nullable enable
using Kassyi.GSharp.Compiler.Tasks;
using Microsoft.Build.Framework;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Tasks;

[Collection(TestCollections.GlobalStateSequential)]
[Category(TestCategory.Compiler)]
public class CompileTaskBehaviorTests
{
    static CompileTaskBehaviorTests()
    {
        MSBuildLocatorSetup.EnsureRegistered();
    }

    [Theory]
    [InlineData(CompileTaskKind.Cuda, "kernel.cu", "nvcc", "-fatbin")]
    [InlineData(CompileTaskKind.Cpp, "kernel.cpp", "cl.exe", "/bigobj")]
    public void Test_CompileTask_RealCompilation_CallsCompiler(
        CompileTaskKind kind,
        string sourceName,
        string expectedExe,
        string? expectedArg)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.SetEnv("GSHARP_TEST_NO_CUDA", null);
        ctx.CreateDummySource(sourceName, "// source");

        var task = ctx.CreateTask(kind, [sourceName]);

        Assert.True(task.Execute());
        Assert.NotEmpty(OutputsOf(task));

        Assert.Contains(ctx.Runner.Invocations,
            i => i.FileName.Contains(expectedExe) && (expectedArg == null || i.Arguments.Contains(expectedArg)));
    }

    [Theory]
    [InlineData(CompileTaskKind.Cuda)]
    [InlineData(CompileTaskKind.CudaCombined)]
    [InlineData(CompileTaskKind.Cpp)]
    [InlineData(CompileTaskKind.KernelTaskBase)]
    public void Test_CompileTask_EmptySources_ReturnsTrue(CompileTaskKind kind)
    {
        using var ctx = new CompileTaskTestContext();
        var task = ctx.CreateTask(kind, []);

        Assert.True(task.Execute());
        Assert.Empty(OutputsOf(task));
    }

    [Theory]
    [InlineData(CompileTaskKind.Cuda, "kernel.cu")]
    [InlineData(CompileTaskKind.Cpp, "kernel.cpp")]
    public void Test_CompileTask_AlwaysRecompiles_And_IgnoresCache(CompileTaskKind kind, string sourceName)
    {
        using var ctx = new CompileTaskTestContext();
        var sourcePath = ctx.CreateDummySource(sourceName, "// source");
        var baseTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(sourcePath, baseTime);

        var task = ctx.CreateTask(kind, [sourceName]);
        task.MockCompilation = true;

        // 1. Initial compilation generates the output artifact
        Assert.True(task.Execute());
        var outputItem = Assert.Single(OutputsOf(task)).ItemSpec;
        Assert.True(File.Exists(outputItem));

        // Write custom sentinel
        File.WriteAllText(outputItem, "PRESERVED_CACHED_CONTENT");
        File.SetLastWriteTimeUtc(outputItem, baseTime.AddSeconds(10));

        // 2. Execute again -> should unconditionally recompile and overwrite cached output
        Assert.True(task.Execute());
        Assert.NotEqual("PRESERVED_CACHED_CONTENT", File.ReadAllText(outputItem));
    }

    [Theory]
    [InlineData(CompileTaskKind.Cuda, "kernel.cu", "Mock NVCC Error")]
    [InlineData(CompileTaskKind.Cpp, "kernel.cpp", "Mock CL Error")]
    [InlineData(CompileTaskKind.CudaCombined, "kernel.cu", "nvcc compilation failed with exit code 1")]
    public void Test_CompileTask_ProcessFails_LogsErrorAndReturnsFalse(
        CompileTaskKind kind,
        string sourceName,
        string errorMessage)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource(sourceName, "// source");
        ctx.Runner.NextExitCode = 1;
        ctx.Runner.NextStandardError = errorMessage;
        ctx.Runner.NextStandardOutput = errorMessage;

        var task = ctx.CreateTask(kind, [sourceName]);

        Assert.False(task.Execute());
        Assert.Contains(ctx.BuildEngine.LoggedErrors, e => e.Contains(errorMessage));
    }

    [Theory]
    [InlineData(CompileTaskKind.Cuda, "dummy.cu")]
    [InlineData(CompileTaskKind.Cpp, "dummy.cpp")]
    public void Test_CompileTask_NoVswhere(CompileTaskKind kind, string sourceName)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.SetEnv("GSHARP_TEST_NO_CUDA", null);
        ctx.CreateDummySource(sourceName, "dummy");
        ctx.Runner.OverrideVswhere = true;

        var task = ctx.CreateTask(kind, [sourceName]);

        Assert.False(task.Execute());
        Assert.Contains(ctx.BuildEngine.LoggedErrors, e => e.Contains("vcvarsall"));
    }

    [Theory]
    [InlineData(CompileTaskKind.Cuda, "kernel1.cu", "nvcc")]
    [InlineData(CompileTaskKind.Cpp, "kernel1.cpp", "cl.exe")]
    public void Test_CompileTask_RunProcess_ThrowsException(CompileTaskKind kind, string sourceName, string expectedExe)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource(sourceName, "dummy1");
        ctx.Runner.NextException = new System.Exception("Simulated process start failure");

        var task = ctx.CreateTask(kind, [sourceName]);

        Assert.False(task.Execute());
        Assert.Contains(ctx.BuildEngine.LoggedErrors, e => e.Contains($"Failed to start {expectedExe}"));
    }

    [Theory]
    [InlineData(CompileTaskKind.Cuda, "kernel.cu")]
    [InlineData(CompileTaskKind.CudaCombined, "kernel.cu")]
    [InlineData(CompileTaskKind.Cpp, "kernel.cpp")]
    public void Test_CompileTask_PropagatesCancellationToken_ToProcessRunner(CompileTaskKind kind, string sourceName)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.SetEnv("GSHARP_TEST_NO_CUDA", null);
        ctx.CreateDummySource(sourceName, "// source");

        var task = ctx.CreateTask(kind, [sourceName]);

        Assert.True(task.Execute());
        Assert.NotEmpty(ctx.Runner.ObservedTokens);
        Assert.All(ctx.Runner.ObservedTokens, token => Assert.True(token.CanBeCanceled));
    }

    [Theory]
    [InlineData(CompileTaskKind.Cuda, "kernel.cu")]
    [InlineData(CompileTaskKind.CudaCombined, "kernel.cu")]
    [InlineData(CompileTaskKind.Cpp, "kernel.cpp")]
    public void Test_CompileTask_CancelBeforeExecute_ReturnsFalseAndLogsWarning(CompileTaskKind kind, string sourceName)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.SetEnv("GSHARP_TEST_NO_CUDA", null);
        ctx.CreateDummySource(sourceName, "// source");

        var task = ctx.CreateTask(kind, [sourceName]);

        task.Cancel();

        Assert.False(task.Execute());
        Assert.Empty(ctx.BuildEngine.LoggedErrors);
        Assert.Contains(
            ctx.BuildEngine.LoggedWarnings,
            w => w.Contains("canceled", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(CompileTaskKind.Cuda, "kernel.cu")]
    [InlineData(CompileTaskKind.CudaCombined, "kernel.cu")]
    [InlineData(CompileTaskKind.Cpp, "kernel.cpp")]
    public void Test_CompileTask_CancelDuringProcessRun_ReturnsFalseAndLogsWarning(
        CompileTaskKind kind,
        string sourceName)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.SetEnv("GSHARP_TEST_NO_CUDA", null);
        ctx.CreateDummySource(sourceName, "// source");

        var task = ctx.CreateTask(kind, [sourceName]);

        ctx.Runner.OnRun = (startInfo, token) =>
        {
            if (startInfo.FileName.Contains("nvcc") || startInfo.FileName.Contains("cl.exe"))
            {
                task.Cancel();
            }
        };

        Assert.False(task.Execute());
        Assert.Empty(ctx.BuildEngine.LoggedErrors);
        Assert.Contains(
            ctx.BuildEngine.LoggedWarnings,
            w => w.Contains("canceled", StringComparison.OrdinalIgnoreCase));
    }

    private static ITaskItem[] OutputsOf(CompileKernelTaskBase task) => task switch
    {
        CompileCppToNativeTask cpp => cpp.OutputDlls,
        CompileCudaToFatbinTask cuda => cuda.OutputFatbins,
        _ => []
    };
}
