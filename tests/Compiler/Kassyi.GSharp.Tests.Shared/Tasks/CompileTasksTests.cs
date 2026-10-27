#nullable enable
using System.Collections;
using System.IO;
using Kassyi.GSharp.Compiler.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Build.Locator;
using System.Diagnostics;
using Kassyi.GSharp.Compiler.Tasks.Helpers;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Kassyi.GSharp.Tests.Shared.Tasks;

[Collection(TestCollections.GlobalStateSequential)]
public class CompileTasksTests
{
    static CompileTasksTests()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            try
            {
                MSBuildLocator.RegisterDefaults();
            }
            catch
            {
                // Best-effort: ignore if already initialized or assemblies were pre-loaded
            }
        }
    }


    [Theory]
    [InlineData(false, "kernel.cu", "nvcc", "-fatbin")]
    [InlineData(true, "kernel.cpp", "cl.exe", null)]
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

    [Fact]
    [Category(TestCategory.Compiler)]
    public void Test_CompileCudaToFatbinTask_WhenNoCudaEnv_SkipsAndSucceeds()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.SetEnv("GSHARP_TEST_NO_CUDA", "1");
        var task = ctx.CreateCudaTask(["dummy.cu"]);
        task.TargetArchitectures = "sm_86";
        Assert.True(task.Execute());
        Assert.Empty(task.OutputFatbins);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public void Test_CompileCudaToFatbinTask_WhenNvccMissing_SkipsGracefully()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.SetEnv("GSHARP_TEST_NO_CUDA", null);
        var task = ctx.CreateCudaTask(["dummy.cu"], null);
        task.ProcessRunner = new ProcessRunner();
        task.NvccPath = "non_existent_nvcc_executable_12345";
        task.TargetArchitectures = "sm_86";
        Assert.True(task.Execute());
        Assert.Empty(task.OutputFatbins);
        Assert.Contains(ctx.BuildEngine.LoggedWarnings, w => w.Contains("CUDA compiler (nvcc) was not found"));
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

    [Fact]
    [Category(TestCategory.Compiler)]
    public void Test_CompileCudaToFatbinTask_CombinedFatbinName_Specified_ShouldBundleSources()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel1.cu", "// cuda source 1");
        ctx.CreateDummySource("kernel2.cu", "// cuda source 2");

        var task = ctx.CreateCudaTask(["kernel1.cu", "kernel2.cu"], "native_benchmarks.fatbin");
        task.MockCompilation = true;

        Assert.True(task.Execute());
        var outputItem = Assert.Single(task.OutputFatbins);
        var expectedPath = Path.Combine(ctx.TempDir, "native_benchmarks.gfatbin");
        Assert.Equal(expectedPath, outputItem.ItemSpec);
        Assert.True(File.Exists(expectedPath));
        Assert.Equal("Kassyi.GSharp.Kernels.native_benchmarks.gfatbin", outputItem.GetMetadata("LogicalName"));
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
    [InlineData(new[] { "kernel1.cu", "kernel2.cu" }, "sm_86", "combined_fatbin")]
    [InlineData(new[] { "kernel.cu" }, "sm_86,compute_86;sm_89,compute_89", "combined_fatbin")]
    [InlineData(new[] { "combined_fatbin_bundle.cu", "kernel2.cu" }, "sm_86", "combined_fatbin")]
    [Category(TestCategory.Compiler)]
    public void Test_CompileCudaToFatbinTask_CombinedFatbinName_SuccessCases(string[] sources, string targetArch,
        string combinedName)
    {
        using var ctx = new CompileTaskTestContext();
        foreach (var s in sources) ctx.CreateDummySource(s);
        ctx.Runner.NextStandardOutput = "Success combined";

        var task = ctx.CreateCudaTask(sources, combinedName);
        task.TargetArchitectures = targetArch;

        Assert.True(task.Execute());
        var output = Assert.Single(task.OutputFatbins);
        Assert.Contains($"{combinedName}.gfatbin", output.ItemSpec);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
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
    [Category(TestCategory.Compiler)]
    public void Test_CompileCppToNativeTask_VswhereFails_LogsErrorAndReturnsFalse()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cpp", "// source");
        ctx.Runner.OverrideVswhere = true;

        var task = ctx.CreateCppTask(["kernel.cpp"]);
        task.MockCompilation = false;

        Assert.False(task.Execute());
        Assert.Contains(ctx.BuildEngine.LoggedErrors, e => e.Contains("Could not locate vcvarsall.bat"));
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public void Test_CompileCppToNativeTask_ClExePathResolvedFromPath()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cpp", "// source");

        var dummyClDir = Path.Combine(ctx.TempDir, "FakeMSVC");
        Directory.CreateDirectory(dummyClDir);
        File.WriteAllText(Path.Combine(dummyClDir, "cl.exe"), "");

        ctx.Runner.CmdStandardOutput = $"INCLUDE=C:\\dummy\r\nLIB=C:\\dummy\r\nPATH={dummyClDir}{Path.PathSeparator}C:\\Windows\\System32";

        var task = ctx.CreateCppTask(["kernel.cpp"]);
        task.MockCompilation = false;
        ctx.Runner.SimulateCompilationOutput = true;

        Assert.True(task.Execute());

        Assert.Contains(ctx.Runner.Invocations, i => i.FileName == Path.Combine(dummyClDir, "cl.exe"));
    }


    [Theory]
    [InlineData("combined_fatbin", true)] // Combined, UpToDate
    [InlineData("combined_fatbin", false)] // Combined, NotUpToDate
    [InlineData("", true)] // Individual, UpToDate
    [InlineData("", false)] // Individual, NotUpToDate
    [Category(TestCategory.Compiler)]
    public void Test_CompileCudaToFatbinTask_TimestampCheck(string? combinedName, bool isUpToDate)
    {
        using var ctx = new CompileTaskTestContext();
        var src = ctx.CreateDummySource("kernel1.cu", "dummy1");
        var outFileName = string.IsNullOrEmpty(combinedName) ? "kernel1.gfatbin" : $"{combinedName}.gfatbin";
        var outPath = Path.Combine(ctx.TempDir, outFileName);

        // A first build writes the fatbin and the nvcc command record, so that only the timestamps differ below
        Assert.True(ctx.CreateCudaTask(["kernel1.cu"], combinedName).Execute());
        Assert.True(File.Exists(outPath + ".nvcc"));
        ctx.Runner.Invocations.Clear();

        // If up to date: dest is newer than src. If not: src is newer than dest.
        var destOffset = isUpToDate ? 10 : -10;
        var srcOffset = isUpToDate ? -10 : 10;
        File.SetLastWriteTimeUtc(outPath, DateTime.UtcNow.AddSeconds(destOffset));
        File.SetLastWriteTimeUtc(src, DateTime.UtcNow.AddSeconds(srcOffset));

        var task = ctx.CreateCudaTask(["kernel1.cu"], combinedName);

        Assert.True(task.Execute());
        Assert.Single(task.OutputFatbins);

        if (isUpToDate)
        {
            Assert.DoesNotContain(ctx.Runner.Invocations, i => i.Arguments.Contains("--fatbin"));
        }
        else
        {
            Assert.Contains(ctx.Runner.Invocations, i => i.Arguments.Contains("--fatbin"));
        }
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public void Test_CompileCudaToFatbinTask_OnBeforeExecute_Fails()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.Runner.OverrideVswhere = true;
        var task = ctx.CreateCudaTask(["dummy.cu"]);
        Assert.False(task.Execute());
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

    // --- Infrastructure Tests ---

    [Fact]
    [Category(TestCategory.Compiler)]
    public void Test_MockBuildEngine_LogsCorrectly()
    {
        var buildEngine = new MockBuildEngine();
        buildEngine.LogErrorEvent(new BuildErrorEventArgs("subcategory", "code", "file", 1, 1, 1, 1, "Test Error",
            "helpKeyword", "senderName"));
        buildEngine.LogWarningEvent(new BuildWarningEventArgs("subcategory", "code", "file", 1, 1, 1, 1, "Test Warning",
            "helpKeyword", "senderName"));
        buildEngine.LogMessageEvent(new BuildMessageEventArgs("Test Message", "helpKeyword", "senderName",
            MessageImportance.Normal));

        Assert.Single(buildEngine.LoggedErrors);
        Assert.Contains("Test Error", buildEngine.LoggedErrors[0]);

        Assert.Single(buildEngine.LoggedWarnings);
        Assert.Contains("Test Warning", buildEngine.LoggedWarnings[0]);

        Assert.Single(buildEngine.LoggedMessages);
        Assert.Contains("Test Message", buildEngine.LoggedMessages[0]);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public void Test_MockProcessRunner_RecordsInvocationsAndReturnsOutputs()
    {
        var runner = new MockProcessRunner("C:\\dummy");
        runner.NextExitCode = 42;
        runner.NextStandardOutput = "hello";
        runner.NextStandardError = "world";

        var startInfo = new ProcessStartInfo("dummy.exe", "--test");
        var result = runner.Run(startInfo);

        Assert.Equal(42, result.ExitCode);
        Assert.Equal("hello", result.StandardOutput);
        Assert.Equal("world", result.StandardError);

        Assert.Single(runner.Invocations);
        Assert.Equal("dummy.exe", runner.Invocations[0].FileName);
        Assert.Equal("--test", runner.Invocations[0].Arguments);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public void Test_MockProcessRunner_OverrideVswhere()
    {
        var runner = new MockProcessRunner("C:\\dummy");
        runner.OverrideVswhere = true;

        var startInfo = new ProcessStartInfo("vswhere.exe", "");
        var result = runner.Run(startInfo);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Not found", result.StandardError);
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

    [Fact]
    [Category(TestCategory.Compiler)]
    public async Task Test_ProcessRunner_CancellationRequested_BeforeStart()
    {
        var runner = new ProcessRunner();
        var startInfo = new ProcessStartInfo("dummy_executable_that_will_not_be_run.exe");
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel before start

        await Assert.ThrowsAsync<TaskCanceledException>(() => runner.RunAsync(startInfo, cts.Token));
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public async Task Test_ProcessRunner_ProcessStartFailed()
    {
        var runner = new ProcessRunner();
        // Use a definitely non-existent executable to force Start() to fail
        var startInfo = new ProcessStartInfo("this_executable_should_not_exist_xyz123.exe");

        await Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(() => runner.RunAsync(startInfo));
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public async Task Test_ProcessRunner_Cancellation_KillsProcess()
    {
        var runner = new ProcessRunner();

        // An actual executable process is required to test the post-start kill callback across OSes.
        var startInfo = Kassyi.GSharp.Tests.Shared.Helpers.ShellCommand.Sleep(10);
        using var cts = new CancellationTokenSource();

        var task = runner.RunAsync(startInfo, cts.Token);

        // Cancel immediately to trigger the cancellation token registration callback.
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
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
        task.SafeDeleteFiles(null!, "");

        // Invalid path that throws an exception inside File.Exists or File.Delete
        task.SafeDeleteFiles("||invalid_path<>?*");
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

    [Fact]
    [Category(TestCategory.Compiler)]
    public void Test_CompileCudaToFatbinTask_DetectRegisterSpillSuppressGs0001()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel1.cu", "// gsharp:suppress GS0001\ndummy");

        ctx.Runner.NextStandardError = "ptxas warning : 10 bytes spill\nFunction properties for _Z3foo";

        var task = ctx.CreateCudaTask(["kernel1.cu"], null);
        Assert.True(task.Execute());
        Assert.DoesNotContain(ctx.BuildEngine.LoggedWarnings, w => w.Contains("GS0001"));
    }


    [Fact]
    [Category(TestCategory.Compiler)]
    public void Test_CompileCudaToFatbinTask_CombinedFatbinName_CreateOutputDirectory()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cu");

        var outDir = Path.Combine(ctx.TempDir, "OutDir"); // Does not exist
        var task = ctx.CreateCudaTask(["kernel.cu"]);
        task.OutputDirectory = outDir;
        task.MockCompilation = true;

        Assert.True(task.Execute());
        Assert.True(Directory.Exists(outDir));
        Assert.Single(task.OutputFatbins);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public void Test_CompileCudaToFatbinTask_CombinedFatbinName_BundleFile_WriteFailure()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cu");

        // Create a directory with the exact name of the bundle file, so File.WriteAllText throws an IOException / UnauthorizedAccessException
        Directory.CreateDirectory(Path.Combine(ctx.TempDir, "combined_fatbin_bundle.cu"));

        var task = ctx.CreateCudaTask(["kernel.cu"]);
        Assert.False(task.Execute());
        Assert.Contains(ctx.BuildEngine.LoggedErrors, e => e.Contains("Failed to write bundle file"));
    }

    [Theory]
    [InlineData("combined_fatbin")]
    [InlineData(null)]
    [Category(TestCategory.Compiler)]
    public void Test_CompileCudaToFatbinTask_OutputMissing_LogsError(string? combinedName)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cu");
        ctx.Runner.SimulateCompilationOutput = false;

        var task = ctx.CreateCudaTask(["kernel.cu"], combinedName);
        Assert.False(task.Execute());
        Assert.Contains(ctx.BuildEngine.LoggedErrors,
            e => e.Contains("nvcc succeeded but output fatbin was not found"));
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public void Test_CompileCudaToFatbinTask_CombinedFatbinName_MissingSourceFile_NotUpToDate()
    {
        using var ctx = new CompileTaskTestContext();
        // File is missing, but output exists (with the nvcc command record of a first build)
        File.WriteAllText(Path.Combine(ctx.TempDir, "combined_fatbin.gfatbin"), "old content");
        Assert.True(ctx.CreateCudaTask(["missing.cu"]).Execute());
        ctx.Runner.Invocations.Clear();

        var task = ctx.CreateCudaTask(["missing.cu"]);
        Assert.True(task.Execute());
        Assert.Single(task.OutputFatbins);
        Assert.DoesNotContain(ctx.Runner.Invocations, i => i.Arguments.Contains("--fatbin"));
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

    // --- GCC / Clang (POSIX / Linux) Toolchain Tests ---

    [Theory]
    [InlineData("g++")]
    [InlineData("clang++")]
    [Category(TestCategory.Compiler)]
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
    [Category(TestCategory.Compiler)]
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
    [Category(TestCategory.Compiler)]
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

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CompileCudaToFatbinTask_GccClangToolchain_SkipsVcvarsAndMsvcFlags()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cu", "// cuda source");
        ctx.Runner.OverrideVswhere = true; // Even if vswhere fails, Linux toolchain must not call it

        var toolchain = ctx.CreateGccClangToolchain("g++");
        var task = ctx.CreateCudaTask(["kernel.cu"], combinedName: null, toolchain: toolchain);
        task.TargetArchitectures = "compute_86,sm_86";

        Assert.True(task.Execute());
        Assert.Single(task.OutputFatbins);
        Assert.DoesNotContain(ctx.Runner.Invocations,
            i => i.FileName.EndsWith("vswhere.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(ctx.Runner.Invocations,
            i => i.FileName.Contains("nvcc") &&
                 i.Arguments.Contains("--fatbin") &&
                 !i.Arguments.Contains("/wd4819"));
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CompileCudaToFatbinTask_MsvcToolchain_PassesConformingPreprocessor()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cu", "// cuda source");

        var task = ctx.CreateCudaTask(["kernel.cu"], combinedName: null, toolchain: ctx.CreateMsvcToolchain());
        task.TargetArchitectures = "compute_86,sm_86";

        Assert.True(task.Execute());
        Assert.Contains(ctx.Runner.Invocations,
            i => i.FileName.Contains("nvcc") &&
                 i.Arguments.Contains("/Zc:preprocessor") &&
                 !i.Arguments.Contains("CCCL_IGNORE_MSVC_TRADITIONAL_PREPROCESSOR_WARNING"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("combined_fatbin")]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CompileCudaToFatbinTask_SameCommand_SecondBuildSkipsNvcc(string combinedName)
    {
        using var ctx = new CompileTaskTestContext();
        CreateOldSource(ctx);

        var first = ctx.CreateCudaTask(["kernel1.cu"], combinedName);
        Assert.True(first.Execute());
        var commandFile = Assert.Single(first.OutputFatbins).GetMetadata("NvccCommandFile");
        Assert.Equal(Assert.Single(first.OutputFatbins).ItemSpec + ".nvcc", commandFile);
        Assert.True(File.Exists(commandFile));
        Assert.Equal(1, CountNvccCompilations(ctx));

        var second = ctx.CreateCudaTask(["kernel1.cu"], combinedName);
        Assert.True(second.Execute());
        Assert.Equal(1, CountNvccCompilations(ctx));
        Assert.Equal(commandFile, Assert.Single(second.OutputFatbins).GetMetadata("NvccCommandFile"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("combined_fatbin")]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CompileCudaToFatbinTask_ArchitecturesChanged_RerunsNvcc(string combinedName)
    {
        using var ctx = new CompileTaskTestContext();
        CreateOldSource(ctx);

        Assert.True(ctx.CreateCudaTask(["kernel1.cu"], combinedName).Execute());

        var second = ctx.CreateCudaTask(["kernel1.cu"], combinedName);
        second.TargetArchitectures = "compute_89,sm_89";
        Assert.True(second.Execute());

        Assert.Equal(2, CountNvccCompilations(ctx));
        Assert.Contains("code=sm_89", File.ReadAllText(Assert.Single(second.OutputFatbins).GetMetadata("NvccCommandFile")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("combined_fatbin")]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CompileCudaToFatbinTask_CommandRecordMissing_RerunsNvcc(string combinedName)
    {
        using var ctx = new CompileTaskTestContext();
        CreateOldSource(ctx);

        var first = ctx.CreateCudaTask(["kernel1.cu"], combinedName);
        Assert.True(first.Execute());
        var fatbin = Assert.Single(first.OutputFatbins).ItemSpec;
        File.Delete(fatbin + ".nvcc");

        Assert.True(ctx.CreateCudaTask(["kernel1.cu"], combinedName).Execute());

        Assert.Equal(2, CountNvccCompilations(ctx));
        Assert.True(File.Exists(fatbin + ".nvcc"));
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CompileCudaToFatbinTask_NvccVersionChanged_RerunsNvcc()
    {
        using var ctx = new CompileTaskTestContext();
        CreateOldSource(ctx);
        ctx.Runner.VersionCheckOutput = "nvcc: NVIDIA (R) Cuda compiler driver\nBuild cuda_13.4.r13.4/compiler.1";

        Assert.True(ctx.CreateCudaTask(["kernel1.cu"], combinedName: "").Execute());

        ctx.Runner.VersionCheckOutput = "nvcc: NVIDIA (R) Cuda compiler driver\nBuild cuda_13.5.r13.5/compiler.1";
        var second = ctx.CreateCudaTask(["kernel1.cu"], combinedName: "");
        Assert.True(second.Execute());

        Assert.Equal(2, CountNvccCompilations(ctx));
        Assert.EndsWith("Build cuda_13.5.r13.5/compiler.1",
            File.ReadAllText(Assert.Single(second.OutputFatbins).GetMetadata("NvccCommandFile")));
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CompileCudaToFatbinTask_CombinedSourceAdded_RerunsNvcc()
    {
        using var ctx = new CompileTaskTestContext();
        CreateOldSource(ctx);
        CreateOldSource(ctx, "kernel2.cu");

        Assert.True(ctx.CreateCudaTask(["kernel1.cu"], "combined_fatbin").Execute());
        Assert.True(ctx.CreateCudaTask(["kernel1.cu", "kernel2.cu"], "combined_fatbin").Execute());

        // kernel2.cu is older than the fatbin, so only the recorded source list shows that the bundle changed
        Assert.Equal(2, CountNvccCompilations(ctx));
    }

    [Theory]
    [InlineData("")]
    [InlineData("combined_fatbin")]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CompileCudaToFatbinTask_SourceEdited_RerunsNvcc(string combinedName)
    {
        using var ctx = new CompileTaskTestContext();
        var source = ctx.CreateDummySource("kernel1.cu", "// cuda source");
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(-10));
        Assert.True(ctx.CreateCudaTask(["kernel1.cu"], combinedName).Execute());

        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(10));
        Assert.True(ctx.CreateCudaTask(["kernel1.cu"], combinedName).Execute());

        Assert.Equal(2, CountNvccCompilations(ctx));
    }

    // nvcc succeeds but signing fails: the record of the earlier command must not match the new unsigned fatbin
    [Theory]
    [InlineData("")]
    [InlineData("combined_fatbin")]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Test_CompileCudaToFatbinTask_SigningFailsAfterNvcc_NextBuildRerunsNvcc(string combinedName)
    {
        using var ctx = new CompileTaskTestContext();
        CreateOldSource(ctx);
        var first = ctx.CreateCudaTask(["kernel1.cu"], combinedName);
        Assert.True(first.Execute());
        var commandFile = Assert.Single(first.OutputFatbins).GetMetadata("NvccCommandFile");

        // The command changes and nvcc runs, then the invalid key makes signing fail
        ctx.SetEnv("GSHARP_SIGNING_KEY", "not-a-key");
        var second = ctx.CreateCudaTask(["kernel1.cu"], combinedName);
        second.TargetArchitectures = "compute_89,sm_89";
        Assert.False(second.Execute());
        Assert.False(File.Exists(commandFile));

        ctx.SetEnv("GSHARP_SIGNING_KEY", null);
        var third = ctx.CreateCudaTask(["kernel1.cu"], combinedName);
        third.TargetArchitectures = "compute_89,sm_89";
        Assert.True(third.Execute());

        Assert.Equal(3, CountNvccCompilations(ctx));
    }

    [Theory]
    [InlineData("")]
    [InlineData("combined_fatbin")]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Test_CompileCudaToFatbinTask_NvccFails_RemovesTheOldRecord(string combinedName)
    {
        using var ctx = new CompileTaskTestContext();
        CreateOldSource(ctx);
        var first = ctx.CreateCudaTask(["kernel1.cu"], combinedName);
        Assert.True(first.Execute());
        var commandFile = Assert.Single(first.OutputFatbins).GetMetadata("NvccCommandFile");

        ctx.Runner.NextExitCode = 1;
        var second = ctx.CreateCudaTask(["kernel1.cu"], combinedName);
        second.TargetArchitectures = "compute_89,sm_89";
        Assert.False(second.Execute());

        Assert.False(File.Exists(commandFile));
    }

    // Changing the signing key re-signs the fatbin: the record holds the public key of the key in use
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CompileCudaToFatbinTask_SigningKeyChanged_RerunsNvcc()
    {
        using var ctx = new CompileTaskTestContext();
        CreateOldSource(ctx);
        Assert.True(ctx.CreateCudaTask(["kernel1.cu"], combinedName: "").Execute());

        ctx.SetEnv("GSHARP_SIGNING_KEY", new string('a', 64));
        var second = ctx.CreateCudaTask(["kernel1.cu"], combinedName: "");
        Assert.True(second.Execute());

        Assert.Equal(2, CountNvccCompilations(ctx));
        var record = File.ReadAllText(Assert.Single(second.OutputFatbins).GetMetadata("NvccCommandFile"));
        Assert.Contains("signing-key " + ArtifactHelper.GetSigningKeyFingerprint(), record);
    }

    private static void CreateOldSource(CompileTaskTestContext ctx, string fileName = "kernel1.cu")
    {
        var source = ctx.CreateDummySource(fileName, "// cuda source");
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(-10));
    }

    private static int CountNvccCompilations(CompileTaskTestContext ctx) =>
        ctx.Runner.Invocations.Count(i => i.FileName.Contains("nvcc") && i.Arguments.Contains("--fatbin"));
}
