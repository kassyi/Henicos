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
public class CompileCudaToFatbinTaskTests
{
    static CompileCudaToFatbinTaskTests()
    {
        MSBuildLocatorSetup.EnsureRegistered();
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

    [Theory]
    [InlineData("")]
    [InlineData("combined_fatbin")]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CompileCudaToFatbinTask_DefaultArchitectures_EmitSass86AndPtx86(string combinedName)
    {
        using var ctx = new CompileTaskTestContext();
        CreateOldSource(ctx);

        var task = ctx.CreateCudaTask(["kernel1.cu"], combinedName);
        Assert.True(task.Execute());

        var nvcc = Assert.Single(ctx.Runner.Invocations, i => i.FileName.Contains("nvcc") && i.Arguments.Contains("--fatbin"));
        Assert.Contains("-gencode arch=compute_86,code=sm_86 ", nvcc.Arguments);
        Assert.Contains("-gencode arch=compute_86,code=compute_86 ", nvcc.Arguments);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Test_CompileCudaToFatbin_ExitCodeZeroWithWarnings_LogsMsBuildWarnings()
    {
        using var ctx = new CompileTaskTestContext();
        CreateOldSource(ctx);
        ctx.Runner.NextStandardOutput = @"C:\temp\kernel1.cu(102): warning #550-D: variable ""dummy"" was set but never used";

        var task = ctx.CreateCudaTask(["kernel1.cu"], combinedName: "");
        Assert.True(task.Execute());
        Assert.Contains(ctx.BuildEngine.LoggedWarnings, w => w.Contains("dummy") && w.Contains("550"));
    }

    private static void CreateOldSource(CompileTaskTestContext ctx, string fileName = "kernel1.cu")
    {
        var source = ctx.CreateDummySource(fileName, "// cuda source");
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(-10));
    }

    private static int CountNvccCompilations(CompileTaskTestContext ctx) =>
        ctx.Runner.Invocations.Count(i => i.FileName.Contains("nvcc") && i.Arguments.Contains("--fatbin"));
}
