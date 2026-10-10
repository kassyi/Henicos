#nullable enable
using System.IO;
using Kassyi.GSharp.Compiler.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Build.Locator;
using System.Diagnostics;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Tasks;

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

    private class MockBuildEngine : IBuildEngine
    {
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => "";

        public virtual bool BuildProjectFile(string projectFileName, string[] targetNames,
            System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs) => true;

        public virtual void LogCustomEvent(CustomBuildEventArgs e)
        {
        }

        public virtual void LogErrorEvent(BuildErrorEventArgs e)
        {
            System.Console.WriteLine($"ERROR: {e.Message}");
        }

        public virtual void LogMessageEvent(BuildMessageEventArgs e)
        {
        }

        public virtual void LogWarningEvent(BuildWarningEventArgs e)
        {
            System.Console.WriteLine($"WARN: {e.Message}");
        }
    }

    private class MockProcessRunner : Kassyi.GSharp.Compiler.Tasks.Helpers.IProcessRunner
    {
        private readonly string _mockVsPath;

        public MockProcessRunner(string tempDir)
        {
            _mockVsPath = Path.Combine(tempDir, "VS");
            var vcvarsDir = Path.Combine(_mockVsPath, "VC", "Auxiliary", "Build");
            Directory.CreateDirectory(vcvarsDir);
            File.WriteAllText(Path.Combine(vcvarsDir, "vcvarsall.bat"), "@echo off");
        }

        public System.Collections.Generic.List<System.Diagnostics.ProcessStartInfo> Invocations { get; } = new();
        public int NextExitCode { get; set; } = 0;
        public string NextStandardOutput { get; set; } = "";
        public string NextStandardError { get; set; } = "";

        public bool OverrideVswhere { get; set; } = false;

        public Kassyi.GSharp.Compiler.Tasks.Helpers.ProcessResult Run(System.Diagnostics.ProcessStartInfo startInfo, System.Threading.CancellationToken cancellationToken = default)
        {
            Invocations.Add(startInfo);
            // vswhere mock
            if (startInfo.FileName.EndsWith("vswhere.exe", System.StringComparison.OrdinalIgnoreCase))
            {
                if (OverrideVswhere) return new Kassyi.GSharp.Compiler.Tasks.Helpers.ProcessResult(1, "", "Not found");
                return new Kassyi.GSharp.Compiler.Tasks.Helpers.ProcessResult(0, _mockVsPath, "");
            }
            if (startInfo.FileName.EndsWith("cmd.exe", System.StringComparison.OrdinalIgnoreCase))
            {
                // env mock
                return new Kassyi.GSharp.Compiler.Tasks.Helpers.ProcessResult(0, "INCLUDE=C:\\dummy\r\nLIB=C:\\dummy", "");
            }
            if (startInfo.Arguments.Contains("--version"))
            {
                return new Kassyi.GSharp.Compiler.Tasks.Helpers.ProcessResult(0, "nvcc: NVIDIA (R) Cuda compiler driver", "");
            }
            // nvcc / cl
            if (startInfo.FileName.Contains("nvcc") || startInfo.FileName.Contains("cl.exe"))
            {
                // To avoid breaking the task check "if (File.Exists(outputFatbinPath))", simulate compilation output
                var argsStr = startInfo.Arguments;
                var outFile = "";
                if (argsStr.Contains("-o "))
                {
                    outFile = argsStr.Split("-o ")[1].Split(" ")[0].Trim('"');
                }
                else if (argsStr.Contains("/Fe:"))
                {
                    outFile = argsStr.Split("/Fe:")[1].Split(" ")[0].Trim('"');
                }
                
                if (!string.IsNullOrEmpty(outFile) && outFile.EndsWith(".gfatbin"))
                {
                    File.WriteAllText(outFile, "Mock Fatbin Content from MockProcessRunner");
                }
                else if (!string.IsNullOrEmpty(outFile) && outFile.EndsWith(".dll"))
                {
                    File.WriteAllText(outFile, "Mock DLL Content from MockProcessRunner");
                }
            }
            return new Kassyi.GSharp.Compiler.Tasks.Helpers.ProcessResult(NextExitCode, NextStandardOutput, NextStandardError);
        }

        public System.Threading.Tasks.Task<Kassyi.GSharp.Compiler.Tasks.Helpers.ProcessResult> RunAsync(System.Diagnostics.ProcessStartInfo startInfo, System.Threading.CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.FromResult(Run(startInfo, cancellationToken));
        }
    }

    private class MockBuildEngineWithVerification : MockBuildEngine
    {
        public System.Collections.Generic.List<string> LoggedErrors { get; } = new();
        public System.Collections.Generic.List<string> LoggedWarnings { get; } = new();
        public System.Collections.Generic.List<string> LoggedMessages { get; } = new();

        public override void LogErrorEvent(BuildErrorEventArgs e)
        {
            if (e.Message != null) LoggedErrors.Add(e.Message);
            base.LogErrorEvent(e);
        }

        public override void LogWarningEvent(BuildWarningEventArgs e)
        {
            if (e.Message != null) LoggedWarnings.Add(e.Message);
            base.LogWarningEvent(e);
        }

        public override void LogMessageEvent(BuildMessageEventArgs e)
        {
            if (e.Message != null) LoggedMessages.Add(e.Message);
            base.LogMessageEvent(e);
        }
    }

    [Fact]
    public void Test_CompileCudaToFatbinTask_RealCompilation_CallsNvcc()
    {
        var prevEnv = Environment.GetEnvironmentVariable("GSHARP_TEST_NO_CUDA");
        var tempDir = Path.Combine(Path.GetTempPath(), "CompileCudaRealTaskTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            Environment.SetEnvironmentVariable("GSHARP_TEST_NO_CUDA", null);

            var sourcePath = Path.Combine(tempDir, "kernel.cu");
            File.WriteAllText(sourcePath, "// cuda source");

            var runner = new MockProcessRunner(tempDir);
            var task = new CompileCudaToFatbinTask
            {
                BuildEngine = new MockBuildEngine(),
                CudaSources = [new TaskItem(sourcePath)],
                OutputDirectory = tempDir,
                MockCompilation = false,
                DisableVcvarsCache = true,
                TargetArchitectures = "sm_86",
                ProcessRunner = runner
            };

            Assert.True(task.Execute());
            Assert.NotEmpty(task.OutputFatbins);
            
            // Should have invoked vswhere, cmd, nvcc --version, and nvcc compilation
            Assert.Contains(runner.Invocations, i => i.FileName.Contains("nvcc") && i.Arguments.Contains("-fatbin"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GSHARP_TEST_NO_CUDA", prevEnv);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Test_CompileCppToNativeTask_RealCompilation_CallsCl()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CompileCppRealTaskTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "kernel.cpp");
            File.WriteAllText(sourcePath, "// cpp source");

            var runner = new MockProcessRunner(tempDir);
            var task = new CompileCppToNativeTask
            {
                BuildEngine = new MockBuildEngine(),
                CppSources = [new TaskItem(sourcePath)],
                OutputDirectory = tempDir,
                MockCompilation = false,
                DisableVcvarsCache = true,
                GSharpMultiTarget = false,
                ProcessRunner = runner
            };

            Assert.True(task.Execute());
            Assert.NotEmpty(task.OutputDlls);
            
            // Should have invoked vswhere, cmd, and cl.exe
            Assert.Contains(runner.Invocations, i => i.FileName.Contains("cl.exe"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Test_CompileCudaToFatbinTask_EmptySources_ReturnsTrue()
    {
        var task = new CompileCudaToFatbinTask
        {
            BuildEngine = new MockBuildEngine(),
            CudaSources = [],
            OutputDirectory = "dummy",
            TargetArchitectures = "sm_50"
        };
        // Empty sources should succeed without doing anything
        Assert.True(task.Execute());
    }

    [Fact]
    public void Test_CompileCppToNativeTask_EmptySources_ReturnsTrue()
    {
        var task = new CompileCppToNativeTask
        {
            BuildEngine = new MockBuildEngine(),
            CppSources = [],
            OutputDirectory = "dummy",
            GSharpMultiTarget = false
        };
        // Empty sources should succeed without doing anything
        Assert.True(task.Execute());
    }

    [Fact]
    public void Test_CompileCudaToFatbinTask_WhenNoCudaEnv_SkipsAndSucceeds()
    {
        var prevEnv = Environment.GetEnvironmentVariable("GSHARP_TEST_NO_CUDA");
        try
        {
            Environment.SetEnvironmentVariable("GSHARP_TEST_NO_CUDA", "1");
            var task = new CompileCudaToFatbinTask
            {
                BuildEngine = new MockBuildEngine(),
                CudaSources = [new TaskItem("dummy.cu")],
                OutputDirectory = "dummy_out",
                TargetArchitectures = "sm_86"
            };
            Assert.True(task.Execute());
            Assert.Empty(task.OutputFatbins);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GSHARP_TEST_NO_CUDA", prevEnv);
        }
    }


    [Fact]
    public void Test_CompileCudaToFatbinTask_WhenNvccMissing_SkipsGracefully()
    {
        var prevEnv = Environment.GetEnvironmentVariable("GSHARP_TEST_NO_CUDA");
        try
        {
            Environment.SetEnvironmentVariable("GSHARP_TEST_NO_CUDA", null);
            var task = new CompileCudaToFatbinTask
            {
                BuildEngine = new MockBuildEngine(),
                CudaSources = [new TaskItem("dummy.cu")],
                OutputDirectory = "dummy_out",
                NvccPath = "non_existent_nvcc_executable_12345",
                TargetArchitectures = "sm_86"
            };
            Assert.True(task.Execute());
            Assert.Empty(task.OutputFatbins);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GSHARP_TEST_NO_CUDA", prevEnv);
        }
    }

    [Fact]
    public void Test_CompileCppToNativeTask_AlwaysRecompiles_And_IgnoresCache()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CompileCppTaskTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "kernel.cpp");
            File.WriteAllText(sourcePath, "// cpp source");
            var baseTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(sourcePath, baseTime);

            var task = new CompileCppToNativeTask
            {
                BuildEngine = new MockBuildEngine(),
                CppSources = [new TaskItem(sourcePath)],
                OutputDirectory = tempDir,
                MockCompilation = true,
                GSharpMultiTarget = false
            };

            // 1. Initial compilation generates the output artifact
            Assert.True(task.Execute());
            var outputDll = Assert.Single(task.OutputDlls).ItemSpec;
            Assert.True(File.Exists(outputDll));

            // Write custom sentinel
            File.WriteAllText(outputDll, "PRESERVED_CACHED_CONTENT");
            File.SetLastWriteTimeUtc(outputDll, baseTime.AddSeconds(10));

            // 2. Execute again -> should unconditionally recompile and overwrite cached output
            Assert.True(task.Execute());
            Assert.NotEqual("PRESERVED_CACHED_CONTENT", File.ReadAllText(outputDll));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Test_CompileCudaToFatbinTask_AlwaysRecompiles_And_IgnoresCache()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CompileCudaTaskTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "kernel.cu");
            File.WriteAllText(sourcePath, "// cuda source");
            var baseTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(sourcePath, baseTime);

            var task = new CompileCudaToFatbinTask
            {
                BuildEngine = new MockBuildEngine(),
                CudaSources = [new TaskItem(sourcePath)],
                OutputDirectory = tempDir,
                MockCompilation = true,
                TargetArchitectures = "sm_86"
            };

            // 1. Initial compilation generates the output fatbin
            Assert.True(task.Execute());
            var outputFatbin = Assert.Single(task.OutputFatbins).ItemSpec;
            Assert.True(File.Exists(outputFatbin));

            // Write custom sentinel
            File.WriteAllText(outputFatbin, "PRESERVED_CACHED_FATBIN");
            File.SetLastWriteTimeUtc(outputFatbin, baseTime.AddSeconds(10));

            // 2. Execute again -> should unconditionally recompile and overwrite cached output
            Assert.True(task.Execute());
            Assert.NotEqual("PRESERVED_CACHED_FATBIN", File.ReadAllText(outputFatbin));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Test_CompileCudaToFatbinTask_CombinedFatbinName_Specified_EmptySources_ReturnsTrue()
    {
        var task = new CompileCudaToFatbinTask
        {
            BuildEngine = new MockBuildEngine(),
            CudaSources = [],
            CombinedFatbinName = "bundled.fatbin",
            OutputDirectory = "dummy",
            TargetArchitectures = "sm_86"
        };
        Assert.True(task.Execute());
        Assert.Empty(task.OutputFatbins);
    }

    [Fact]
    public void Test_CompileCudaToFatbinTask_CombinedFatbinName_Specified_ShouldBundleSources()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CompileCudaBundleTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var src1 = Path.Combine(tempDir, "kernel1.cu");
            var src2 = Path.Combine(tempDir, "kernel2.cu");
            File.WriteAllText(src1, "// cuda source 1");
            File.WriteAllText(src2, "// cuda source 2");

            var task = new CompileCudaToFatbinTask
            {
                BuildEngine = new MockBuildEngine(),
                CudaSources = [new TaskItem(src1), new TaskItem(src2)],
                CombinedFatbinName = "native_benchmarks.fatbin",
                OutputDirectory = tempDir,
                MockCompilation = true,
                TargetArchitectures = "sm_86"
            };

            Assert.True(task.Execute());
            var outputItem = Assert.Single(task.OutputFatbins);
            var expectedPath = Path.Combine(tempDir, "native_benchmarks.gfatbin");
            Assert.Equal(expectedPath, outputItem.ItemSpec);
            Assert.True(File.Exists(expectedPath));
            Assert.Equal("Kassyi.GSharp.Kernels.native_benchmarks.gfatbin", outputItem.GetMetadata("LogicalName"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
    [Fact]
    public void Test_CompileCudaToFatbinTask_ProcessFails_LogsErrorAndReturnsFalse()
    {
        var prevEnv = Environment.GetEnvironmentVariable("GSHARP_TEST_NO_CUDA");
        var tempDir = Path.Combine(Path.GetTempPath(), "CompileCudaRealTaskTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            Environment.SetEnvironmentVariable("GSHARP_TEST_NO_CUDA", null);

            var sourcePath = Path.Combine(tempDir, "kernel.cu");
            File.WriteAllText(sourcePath, "// cuda source");

            var runner = new MockProcessRunner(tempDir) { NextExitCode = 1, NextStandardError = "Mock NVCC Error" };
            var buildEngine = new MockBuildEngineWithVerification();
            var task = new CompileCudaToFatbinTask
            {
                BuildEngine = buildEngine,
                CudaSources = [new TaskItem(sourcePath)],
                OutputDirectory = tempDir,
                MockCompilation = false,
                TargetArchitectures = "sm_86",
                ProcessRunner = runner
            };

            Assert.False(task.Execute());
            Assert.Contains(buildEngine.LoggedErrors, e => e.Contains("Mock NVCC Error"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GSHARP_TEST_NO_CUDA", prevEnv);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Test_CompileCppToNativeTask_ProcessFails_LogsErrorAndReturnsFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CompileCppRealTaskTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "kernel.cpp");
            File.WriteAllText(sourcePath, "// cpp source");

            var runner = new MockProcessRunner(tempDir) { NextExitCode = 1, NextStandardError = "Mock CL Error" };
            var buildEngine = new MockBuildEngineWithVerification();
            var task = new CompileCppToNativeTask
            {
                BuildEngine = buildEngine,
                CppSources = [new TaskItem(sourcePath)],
                OutputDirectory = tempDir,
                MockCompilation = false,
                GSharpMultiTarget = false,
                ProcessRunner = runner
            };

            Assert.False(task.Execute());
            Assert.Contains(buildEngine.LoggedErrors, e => e.Contains("Mock CL Error"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Test_CompileCudaToFatbinTask_CombinedFatbinName_Success()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile1 = Path.Combine(tempDir, "kernel1.cu");
            var sourceFile2 = Path.Combine(tempDir, "kernel2.cu");
            File.WriteAllText(sourceFile1, "dummy1");
            File.WriteAllText(sourceFile2, "dummy2");
            var buildEngine = new MockBuildEngineWithVerification();

            var sourceItem1 = new TaskItem(sourceFile1);
            var sourceItem2 = new TaskItem(sourceFile2);

            var combinedFatbinPath = Path.Combine(tempDir, "combined_fatbin.gfatbin");
            var runner = new MockProcessRunner(tempDir) 
            { 
                NextExitCode = 0, 
                NextStandardOutput = "Success combined"
            };

            var task = new CompileCudaToFatbinTask
            {
                BuildEngine = buildEngine,
                CudaSources = [sourceItem1, sourceItem2],
                OutputDirectory = tempDir,
                ProcessRunner = runner,
                CombinedFatbinName = "combined_fatbin",
                MockCompilation = false,
                TargetArchitectures = "sm_86"
            };

            var result = task.Execute();

            Assert.True(result);
            Assert.Single(task.OutputFatbins);
            Assert.Contains("combined_fatbin.gfatbin", task.OutputFatbins[0].ItemSpec);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void Test_CompileCudaToFatbinTask_CombinedFatbinName_UpToDate()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile1 = Path.Combine(tempDir, "kernel1.cu");
            File.WriteAllText(sourceFile1, "dummy1");
            var combinedFatbinPath = Path.Combine(tempDir, "combined_fatbin.gfatbin");
            File.WriteAllText(combinedFatbinPath, "old content");
            
            // Make dest newer than src
            File.SetLastWriteTimeUtc(combinedFatbinPath, DateTime.UtcNow.AddSeconds(10));
            File.SetLastWriteTimeUtc(sourceFile1, DateTime.UtcNow.AddSeconds(-10));

            var buildEngine = new MockBuildEngineWithVerification();
            var sourceItem1 = new TaskItem(sourceFile1);

            var runner = new MockProcessRunner(tempDir) { NextExitCode = 0 };

            var task = new CompileCudaToFatbinTask
            {
                BuildEngine = buildEngine,
                CudaSources = [sourceItem1],
                OutputDirectory = tempDir,
                ProcessRunner = runner,
                CombinedFatbinName = "combined_fatbin",
                MockCompilation = false
            };

            var result = task.Execute();

            Assert.True(result);
            Assert.Single(task.OutputFatbins);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void Test_CompileCudaToFatbinTask_Individual_UpToDate()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile1 = Path.Combine(tempDir, "kernel1.cu");
            File.WriteAllText(sourceFile1, "dummy1");
            var fatbinPath = Path.Combine(tempDir, "kernel1.gfatbin");
            File.WriteAllText(fatbinPath, "old content");
            
            // Make dest newer than src
            File.SetLastWriteTimeUtc(fatbinPath, DateTime.UtcNow.AddSeconds(10));
            File.SetLastWriteTimeUtc(sourceFile1, DateTime.UtcNow.AddSeconds(-10));

            var buildEngine = new MockBuildEngineWithVerification();
            var sourceItem1 = new TaskItem(sourceFile1);

            var runner = new MockProcessRunner(tempDir) { NextExitCode = 0 };

            var task = new CompileCudaToFatbinTask
            {
                BuildEngine = buildEngine,
                CudaSources = [sourceItem1],
                OutputDirectory = tempDir,
                ProcessRunner = runner,
                MockCompilation = false, // test caching logic
                DisableVcvarsCache = true,
                CombinedFatbinName = "" // individual
            };

            var result = task.Execute();

            Assert.True(result);
            Assert.Single(task.OutputFatbins);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void Test_CompileCudaToFatbinTask_RunNvcc_Fails()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile1 = Path.Combine(tempDir, "kernel1.cu");
            File.WriteAllText(sourceFile1, "dummy1");
            var buildEngine = new MockBuildEngineWithVerification();
            var sourceItem1 = new TaskItem(sourceFile1);

            var runner = new MockProcessRunner(tempDir) 
            { 
                NextExitCode = 1, // trigger failure
                NextStandardError = "error: syntax error",
            };

            var task = new CompileCudaToFatbinTask
            {
                BuildEngine = buildEngine,
                CudaSources = [sourceItem1],
                OutputDirectory = tempDir,
                ProcessRunner = runner,
                MockCompilation = false,
                DisableVcvarsCache = true,
                CombinedFatbinName = ""
            };

            var result = task.Execute();

            Assert.False(result); // nvcc failed
            Assert.Contains(buildEngine.LoggedErrors, e => e.Contains("syntax error"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void Test_CompileCudaToFatbinTask_NoVswhere()
    {
        var prevEnv = Environment.GetEnvironmentVariable("GSHARP_TEST_NO_CUDA");
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            Environment.SetEnvironmentVariable("GSHARP_TEST_NO_CUDA", null);

            var sourceFile = Path.Combine(tempDir, "dummy.cu");
            File.WriteAllText(sourceFile, "dummy");

            var buildEngine = new MockBuildEngineWithVerification();

            // Setup a process runner that fails when vswhere is called
            var runner = new MockProcessRunner(tempDir);
            runner.OverrideVswhere = true; // We'll add this to mock

            var task = new CompileCudaToFatbinTask
            {
                BuildEngine = buildEngine,
                CudaSources = [new TaskItem(sourceFile)],
                OutputDirectory = tempDir,
                ProcessRunner = runner,
                MockCompilation = false,
                DisableVcvarsCache = true,
                CombinedFatbinName = ""
            };

            var result = task.Execute();

            Assert.False(result); // OnBeforeExecute should fail
            Assert.Contains(buildEngine.LoggedErrors, e => e.Contains("vcvarsall"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GSHARP_TEST_NO_CUDA", prevEnv);
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    // --- Infrastructure Tests ---

    [Fact]
    public void Test_MockBuildEngineWithVerification_LogsCorrectly()
    {
        var buildEngine = new MockBuildEngineWithVerification();
        buildEngine.LogErrorEvent(new BuildErrorEventArgs("subcategory", "code", "file", 1, 1, 1, 1, "Test Error", "helpKeyword", "senderName"));
        buildEngine.LogWarningEvent(new BuildWarningEventArgs("subcategory", "code", "file", 1, 1, 1, 1, "Test Warning", "helpKeyword", "senderName"));
        buildEngine.LogMessageEvent(new BuildMessageEventArgs("Test Message", "helpKeyword", "senderName", MessageImportance.Normal));

        Assert.Single(buildEngine.LoggedErrors);
        Assert.Contains("Test Error", buildEngine.LoggedErrors[0]);

        Assert.Single(buildEngine.LoggedWarnings);
        Assert.Contains("Test Warning", buildEngine.LoggedWarnings[0]);

        Assert.Single(buildEngine.LoggedMessages);
        Assert.Contains("Test Message", buildEngine.LoggedMessages[0]);
    }

    [Fact]
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

        // Next time it should reset (unless specifically implemented otherwise)
        // But the current mock just keeps returning NextExitCode.
    }

    [Fact]
    public void Test_MockProcessRunner_OverrideVswhere()
    {
        var runner = new MockProcessRunner("C:\\dummy");
        runner.OverrideVswhere = true;

        var startInfo = new ProcessStartInfo("vswhere.exe", "");
        var result = runner.Run(startInfo);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Not found", result.StandardError);
    }
}
