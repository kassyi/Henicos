#nullable enable
using System.IO;
using Kassyi.GSharp.Compiler.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Build.Locator;
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

        public bool BuildProjectFile(string projectFileName, string[] targetNames,
            System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs) => true;

        public void LogCustomEvent(CustomBuildEventArgs e)
        {
        }

        public void LogErrorEvent(BuildErrorEventArgs e)
        {
        }

        public void LogMessageEvent(BuildMessageEventArgs e)
        {
        }

        public void LogWarningEvent(BuildWarningEventArgs e)
        {
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
}
