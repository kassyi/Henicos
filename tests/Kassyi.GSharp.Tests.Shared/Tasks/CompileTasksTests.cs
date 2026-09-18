#nullable enable
using System.IO;
using Kassyi.GSharp.Compiler.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Build.Locator;
using Xunit;

namespace Kassyi.GSharp.Tests.Tasks;

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
    [Trait("FeatureId", "COMP-TASKS")]
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
    [Trait("FeatureId", "COMP-TASKS")]
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
    [Trait("FeatureId", "COMP-TASKS")]
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
    [Trait("FeatureId", "COMP-TASKS")]
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
}
