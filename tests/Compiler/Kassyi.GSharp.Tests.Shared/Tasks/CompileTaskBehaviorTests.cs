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
public class CompileTaskBehaviorTests
{
    static CompileTaskBehaviorTests()
    {
        MSBuildLocatorSetup.EnsureRegistered();
    }


    [Theory]
    [InlineData(false, "kernel.cu", "nvcc", "-fatbin")]
    [InlineData(true, "kernel.cpp", "cl.exe", "/bigobj")]
    [Category(TestCategory.Compiler)]
    public void Test_CompileTask_RealCompilation_CallsCompiler(bool isCpp, string sourceName, string expectedExe,
        string? expectedArg)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.SetEnv("GSHARP_TEST_NO_CUDA", null);
        ctx.CreateDummySource(sourceName, "// source");

        var task = ctx.CreateTask(isCpp, [sourceName]);
        if (task is CompileCudaToFatbinTask cudaTask)
        {
            cudaTask.TargetArchitectures = "sm_86";
        }

        Assert.True(task.Execute());
        if (isCpp)
        {
            Assert.NotEmpty(((CompileCppToNativeTask)task).OutputDlls);
        }
        else
        {
            Assert.NotEmpty(((CompileCudaToFatbinTask)task).OutputFatbins);
        }

        Assert.Contains(ctx.Runner.Invocations,
            i => i.FileName.Contains(expectedExe) && (expectedArg == null || i.Arguments.Contains(expectedArg)));
    }

    [Theory]
    [InlineData(0)] // Cuda Individual
    [InlineData(1)] // Cuda Combined
    [InlineData(2)] // Cpp
    [InlineData(3)] // KernelTaskBase
    [Category(TestCategory.Compiler)]
    public void Test_CompileTask_EmptySources_ReturnsTrue(int taskKind)
    {
        using var ctx = new CompileTaskTestContext();
        CompileKernelTaskBase task = taskKind switch
        {
            0 => ctx.CreateCudaTask([], combinedName: ""),
            1 => ctx.CreateCudaTask([], combinedName: "combined.fatbin"),
            2 => ctx.CreateCppTask([]),
            _ => new TestCompileKernelTask()
        };

        Assert.True(task.Execute());
        if (task is CompileCudaToFatbinTask cuda) Assert.Empty(cuda.OutputFatbins);
        if (task is CompileCppToNativeTask cpp) Assert.Empty(cpp.OutputDlls);
    }

    [Theory]
    [InlineData(false, "kernel.cu")]
    [InlineData(true, "kernel.cpp")]
    [Category(TestCategory.Compiler)]
    public void Test_CompileTask_AlwaysRecompiles_And_IgnoresCache(bool isCpp, string sourceName)
    {
        using var ctx = new CompileTaskTestContext();
        var sourcePath = ctx.CreateDummySource(sourceName, "// source");
        var baseTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(sourcePath, baseTime);

        var task = ctx.CreateTask(isCpp, [sourceName], combinedFatbinName: null);
        task.MockCompilation = true;

        // 1. Initial compilation generates the output artifact
        Assert.True(task.Execute());
        var outputItem = isCpp
            ? Assert.Single(((CompileCppToNativeTask)task).OutputDlls).ItemSpec
            : Assert.Single(((CompileCudaToFatbinTask)task).OutputFatbins).ItemSpec;
        Assert.True(File.Exists(outputItem));

        // Write custom sentinel
        File.WriteAllText(outputItem, "PRESERVED_CACHED_CONTENT");
        File.SetLastWriteTimeUtc(outputItem, baseTime.AddSeconds(10));

        // 2. Execute again -> should unconditionally recompile and overwrite cached output
        Assert.True(task.Execute());
        Assert.NotEqual("PRESERVED_CACHED_CONTENT", File.ReadAllText(outputItem));
    }

    [Theory]
    [InlineData(false, "kernel.cu", "Mock NVCC Error", "")]
    [InlineData(true, "kernel.cpp", "Mock CL Error", "")]
    [InlineData(false, "kernel.cu", "nvcc compilation failed with exit code 1", "combined_fatbin")]
    [Category(TestCategory.Compiler)]
    public void Test_CompileTask_ProcessFails_LogsErrorAndReturnsFalse(bool isCpp, string sourceName,
        string errorMessage, string? combinedName)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource(sourceName, "// source");
        ctx.Runner.NextExitCode = 1;
        ctx.Runner.NextStandardError = errorMessage;
        ctx.Runner.NextStandardOutput = errorMessage;

        var task = ctx.CreateTask(isCpp, [sourceName], combinedFatbinName: combinedName);

        Assert.False(task.Execute());
        Assert.Contains(ctx.BuildEngine.LoggedErrors, e => e.Contains(errorMessage));
    }

    [Theory]
    [InlineData(false, "dummy.cu")]
    [InlineData(true, "dummy.cpp")]
    [Category(TestCategory.Compiler)]
    public void Test_CompileTask_NoVswhere(bool isCpp, string sourceName)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.SetEnv("GSHARP_TEST_NO_CUDA", null);
        ctx.CreateDummySource(sourceName, "dummy");
        ctx.Runner.OverrideVswhere = true;

        var task = ctx.CreateTask(isCpp, [sourceName], combinedFatbinName: "");

        Assert.False(task.Execute());
        Assert.Contains(ctx.BuildEngine.LoggedErrors, e => e.Contains("vcvarsall"));
    }

    [Theory]
    [InlineData(false, "kernel1.cu", "nvcc")]
    [InlineData(true, "kernel1.cpp", "cl.exe")]
    [Category(TestCategory.Compiler)]
    public void Test_CompileTask_RunProcess_ThrowsException(bool isCpp, string sourceName, string expectedExe)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource(sourceName, "dummy1");
        ctx.Runner.NextException = new System.Exception("Simulated process start failure");

        var task = ctx.CreateTask(isCpp, [sourceName], combinedFatbinName: "");

        Assert.False(task.Execute());
        Assert.Contains(ctx.BuildEngine.LoggedErrors, e => e.Contains($"Failed to start {expectedExe}"));
    }

    [Theory]
    [InlineData(false, "kernel.cu", null)]
    [InlineData(false, "kernel.cu", "combined_fatbin")]
    [InlineData(true, "kernel.cpp", null)]
    [Category(TestCategory.Compiler)]
    public void Test_CompileTask_PropagatesCancellationToken_ToProcessRunner(bool isCpp, string sourceName, string? combinedName)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.SetEnv("GSHARP_TEST_NO_CUDA", null);
        ctx.CreateDummySource(sourceName, "// source");

        var task = ctx.CreateTask(isCpp, [sourceName], combinedFatbinName: combinedName);
        if (task is CompileCudaToFatbinTask cudaTask)
        {
            cudaTask.TargetArchitectures = "sm_86";
        }

        Assert.True(task.Execute());
        Assert.NotEmpty(ctx.Runner.ObservedTokens);
        Assert.All(ctx.Runner.ObservedTokens, token => Assert.True(token.CanBeCanceled));
    }

    [Theory]
    [InlineData(false, "kernel.cu", null)]
    [InlineData(false, "kernel.cu", "combined_fatbin")]
    [InlineData(true, "kernel.cpp", null)]
    [Category(TestCategory.Compiler)]
    public void Test_CompileTask_CancelBeforeExecute_ReturnsFalseAndLogsWarning(bool isCpp, string sourceName, string? combinedName)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.SetEnv("GSHARP_TEST_NO_CUDA", null);
        ctx.CreateDummySource(sourceName, "// source");

        var task = ctx.CreateTask(isCpp, [sourceName], combinedFatbinName: combinedName);
        if (task is CompileCudaToFatbinTask cudaTask)
        {
            cudaTask.TargetArchitectures = "sm_86";
        }

        task.Cancel();

        Assert.False(task.Execute());
        Assert.Empty(ctx.BuildEngine.LoggedErrors);
        Assert.Contains(ctx.BuildEngine.LoggedWarnings, w => w.Contains("canceled", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(false, "kernel.cu", null)]
    [InlineData(false, "kernel.cu", "combined_fatbin")]
    [InlineData(true, "kernel.cpp", null)]
    [Category(TestCategory.Compiler)]
    public void Test_CompileTask_CancelDuringProcessRun_ReturnsFalseAndLogsWarning(bool isCpp, string sourceName, string? combinedName)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.SetEnv("GSHARP_TEST_NO_CUDA", null);
        ctx.CreateDummySource(sourceName, "// source");

        var task = ctx.CreateTask(isCpp, [sourceName], combinedFatbinName: combinedName);
        if (task is CompileCudaToFatbinTask cudaTask)
        {
            cudaTask.TargetArchitectures = "sm_86";
        }

        ctx.Runner.OnRun = (startInfo, token) =>
        {
            if (startInfo.FileName.Contains("nvcc") || startInfo.FileName.Contains("cl.exe"))
            {
                task.Cancel();
            }
        };

        Assert.False(task.Execute());
        Assert.Empty(ctx.BuildEngine.LoggedErrors);
        Assert.Contains(ctx.BuildEngine.LoggedWarnings, w => w.Contains("canceled", StringComparison.OrdinalIgnoreCase));
    }
}
