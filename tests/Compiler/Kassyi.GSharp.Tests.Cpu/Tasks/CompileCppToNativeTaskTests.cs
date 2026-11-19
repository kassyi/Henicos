#nullable enable
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Compiler.Tasks;
using Kassyi.GSharp.Compiler.Tasks.Toolchains;
using Microsoft.Build.Utilities;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Tasks;

[Collection(TestCollections.GlobalStateSequential)]
[Category(TestCategory.Compiler)]
public class CompileCppToNativeTaskTests
{
    static CompileCppToNativeTaskTests()
    {
        MSBuildLocatorSetup.EnsureRegistered();
    }

    [Fact]
    public void CompileCppToNativeTask_GSharpMultiTarget_CompilesForTwoArchitectures()
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
    public void CompileCppToNativeTask_ClExePathResolvedFromPath()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cpp", "// source");

        var dummyClDir = Path.Combine(ctx.TempDir, "FakeMSVC");
        Directory.CreateDirectory(dummyClDir);
        File.WriteAllText(Path.Combine(dummyClDir, "cl.exe"), "");

        ctx.Runner.CmdStandardOutput =
            $"INCLUDE=C:\\dummy\r\nLIB=C:\\dummy\r\nPATH={dummyClDir}{Path.PathSeparator}C:\\Windows\\System32";

        var task = ctx.CreateCppTask(["kernel.cpp"]);
        task.MockCompilation = false;
        ctx.Runner.SimulateCompilationOutput = true;

        Assert.True(task.Execute());

        Assert.Contains(ctx.Runner.Invocations, i => i.FileName == Path.Combine(dummyClDir, "cl.exe"));
    }

    [Theory]
    [InlineData("g++")]
    [InlineData("clang++")]
    [Scenario(TestScenario.HappyPath)]
    public void CompileCppToNativeTask_GccClangToolchain_CompilesSharedLibrary(string compiler)
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
    [Scenario(TestScenario.Boundary)]
    public void CompileCppToNativeTask_GccClangToolchain_WhenCompilerMissing_SkipsWithWarningAndSucceeds()
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
    [Scenario(TestScenario.SadPath)]
    public void CompileCppToNativeTask_GccClangToolchain_CompilationFails_LogsErrorAndReturnsFalse()
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

    [Theory]
    [InlineData("x64", "arm64")]
    [InlineData("arm64", "x64")]
    [Scenario(TestScenario.SadPath)]
    public void CompileCppToNativeTask_GccClangToolchain_MultiTarget_FailsForNonHostArchitecture(
        string hostArchitecture, string otherArchitecture)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cpp", "// source");

        var toolchain = ctx.CreateGccClangToolchain("g++", hostArchitecture);
        var task = ctx.CreateCppTask(["kernel.cpp"], toolchain);
        task.GSharpMultiTarget = true;

        Assert.False(task.Execute());
        Assert.Empty(task.OutputDlls);
        var error = Assert.Single(ctx.BuildEngine.LoggedErrors);
        Assert.Contains($"cannot build native CPU kernels for {otherArchitecture}", error);
        Assert.Contains($"host architecture ({hostArchitecture})", error);
        Assert.DoesNotContain(ctx.Runner.Invocations, i => i.FileName == "g++");
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CompileCppToNativeTask_GccClangToolchain_HostArchitecture_Compiles()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cpp", "// source");

        var hostArchitecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        var toolchain = ctx.CreateGccClangToolchain("g++");
        var task = ctx.CreateCppTask(["kernel.cpp"], toolchain);

        Assert.True(task.Execute());
        var output = Assert.Single(task.OutputDlls);
        Assert.EndsWith($"_{hostArchitecture}.dll", output.ItemSpec);
        Assert.Empty(ctx.BuildEngine.LoggedErrors);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CompileCppToNativeTask_SameSourceAndRecord_SecondRunSkipsCompileAndSigning()
    {
        using var ctx = new CompileTaskTestContext();
        var source = CreateOldSource(ctx);
        var toolchain = new CountingToolchain();

        var first = ctx.CreateCppTask(["kernel.cpp"], toolchain);
        Assert.True(first.Execute());
        var firstItem = Assert.Single(first.OutputDlls);
        var signedBytes = File.ReadAllBytes(firstItem.ItemSpec);
        Assert.Equal(firstItem.ItemSpec + CompileCppToNativeTask.CommandFileExtension,
            firstItem.GetMetadata("CxxCommandFile"));
        Assert.True(File.Exists(firstItem.GetMetadata("CxxCommandFile")));
        Assert.True(File.Exists(source));

        var second = ctx.CreateCppTask(["kernel.cpp"], toolchain);
        Assert.True(second.Execute());

        Assert.Equal(1, toolchain.CompileCount);
        var secondItem = Assert.Single(second.OutputDlls);
        Assert.Equal(firstItem.ItemSpec, secondItem.ItemSpec);
        Assert.Equal(firstItem.GetMetadata("LogicalName"), secondItem.GetMetadata("LogicalName"));
        Assert.Equal(firstItem.GetMetadata("CxxCommandFile"), secondItem.GetMetadata("CxxCommandFile"));
        Assert.Equal(signedBytes, File.ReadAllBytes(secondItem.ItemSpec));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CompileCppToNativeTask_SourceNewerThanLibrary_CompilesAgain()
    {
        using var ctx = new CompileTaskTestContext();
        var source = CreateOldSource(ctx);
        var toolchain = new CountingToolchain();
        Assert.True(ctx.CreateCppTask(["kernel.cpp"], toolchain).Execute());

        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(10));
        Assert.True(ctx.CreateCppTask(["kernel.cpp"], toolchain).Execute());

        Assert.Equal(2, toolchain.CompileCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CompileCppToNativeTask_SanitizersChanged_CompilesAgain()
    {
        using var ctx = new CompileTaskTestContext();
        CreateOldSource(ctx);
        var toolchain = new CountingToolchain();
        Assert.True(ctx.CreateCppTask(["kernel.cpp"], toolchain).Execute());

        var second = ctx.CreateCppTask(["kernel.cpp"], toolchain);
        second.GSharpNativeSanitizers = "address";
        Assert.True(second.Execute());

        Assert.Equal(2, toolchain.CompileCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CompileCppToNativeTask_ToolchainDescriptionChanged_CompilesAgain()
    {
        using var ctx = new CompileTaskTestContext();
        CreateOldSource(ctx);
        var toolchain = new CountingToolchain();
        Assert.True(ctx.CreateCppTask(["kernel.cpp"], toolchain).Execute());

        toolchain.Version = "compiler 2";
        Assert.True(ctx.CreateCppTask(["kernel.cpp"], toolchain).Execute());

        Assert.Equal(2, toolchain.CompileCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CompileCppToNativeTask_ArchitectureAdded_CompilesOnlyTheNewOne()
    {
        using var ctx = new CompileTaskTestContext();
        CreateOldSource(ctx);
        var toolchain = new CountingToolchain();
        Assert.True(ctx.CreateCppTask(["kernel.cpp"], toolchain).Execute());

        var second = ctx.CreateCppTask(["kernel.cpp"], toolchain);
        second.GSharpMultiTarget = true;
        Assert.True(second.Execute());

        Assert.Equal(2, second.OutputDlls.Length);
        Assert.Equal(2, toolchain.CompileCount);
        Assert.Contains(toolchain.CompiledArchitectures, a => a == "arm64");
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CompileCppToNativeTask_CommandRecordMissing_CompilesAgain()
    {
        using var ctx = new CompileTaskTestContext();
        CreateOldSource(ctx);
        var toolchain = new CountingToolchain();
        var first = ctx.CreateCppTask(["kernel.cpp"], toolchain);
        Assert.True(first.Execute());
        var commandFile = Assert.Single(first.OutputDlls).GetMetadata("CxxCommandFile");
        File.Delete(commandFile);

        Assert.True(ctx.CreateCppTask(["kernel.cpp"], toolchain).Execute());

        Assert.Equal(2, toolchain.CompileCount);
        Assert.True(File.Exists(commandFile));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void CompileCppToNativeTask_CompilationFails_LeavesNoRecordAndCompilesAgainNextTime()
    {
        using var ctx = new CompileTaskTestContext();
        CreateOldSource(ctx);
        var toolchain = new CountingToolchain();
        var first = ctx.CreateCppTask(["kernel.cpp"], toolchain);
        Assert.True(first.Execute());
        var commandFile = Assert.Single(first.OutputDlls).GetMetadata("CxxCommandFile");

        toolchain.Version = "compiler 2";
        toolchain.FailCompilation = true;
        Assert.False(ctx.CreateCppTask(["kernel.cpp"], toolchain).Execute());
        Assert.False(File.Exists(commandFile));

        toolchain.Version = "compiler 1";
        toolchain.FailCompilation = false;
        Assert.True(ctx.CreateCppTask(["kernel.cpp"], toolchain).Execute());

        Assert.Equal(3, toolchain.CompileCount);
        Assert.True(File.Exists(commandFile));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void MsvcToolchain_DescribeCompilation_ContainsClArgumentsAndToolsVersion()
    {
        using var ctx = new CompileTaskTestContext();
        CreateOldSource(ctx);
        ctx.Runner.CmdStandardOutput = "VCToolsVersion=14.50.1\r\nVCToolsInstallDir=C:\\vc\\14.50.1\\";
        var toolchain = ctx.CreateMsvcToolchain();
        var task = ctx.CreateCppTask(["kernel.cpp"], toolchain);
        Assert.True(task.Execute());
        var output = Assert.Single(task.OutputDlls).ItemSpec;
        var cl = Assert.Single(ctx.Runner.Invocations, i => i.FileName.EndsWith("cl.exe", StringComparison.Ordinal));
        var source = Path.Combine(ctx.TempDir, "kernel.cpp");

        var description = toolchain.DescribeCompilation(source, output, HostArchitecture, NativeSanitizers.None);

        Assert.Contains(cl.Arguments, description);
        Assert.Contains("VCToolsVersion 14.50.1", description);
        Assert.Contains("VCToolsInstallDir C:\\vc\\14.50.1\\", description);

        ctx.Runner.CmdStandardOutput = "VCToolsVersion=14.50.2\r\nVCToolsInstallDir=C:\\vc\\14.50.2\\";
        var otherToolchain = ctx.CreateMsvcToolchain();
        Assert.True(ctx.CreateCppTask(["kernel.cpp"], otherToolchain).Execute());
        Assert.NotEqual(description,
            otherToolchain.DescribeCompilation(source, output, HostArchitecture, NativeSanitizers.None));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GccClangToolchain_DescribeCompilation_ContainsCompilerVersionLineAndArguments()
    {
        using var ctx = new CompileTaskTestContext();
        CreateOldSource(ctx);
        ctx.Runner.VersionCheckOutput = "g++ (GCC) 13.2.0\nCopyright notice that is not recorded";
        var toolchain = ctx.CreateGccClangToolchain("g++");
        var task = ctx.CreateCppTask(["kernel.cpp"], toolchain);
        Assert.True(task.Execute());
        var output = Assert.Single(task.OutputDlls).ItemSpec;
        var compile = Assert.Single(ctx.Runner.Invocations,
            i => i.FileName == "g++" && !i.Arguments.Contains("--version"));
        var source = Path.Combine(ctx.TempDir, "kernel.cpp");

        var description = toolchain.DescribeCompilation(source, output, HostArchitecture, NativeSanitizers.None);

        Assert.Contains("g++ (GCC) 13.2.0", description);
        Assert.DoesNotContain("Copyright", description);
        Assert.StartsWith("g++\n", description);
        Assert.Contains(compile.Arguments, description);
    }

    private static string HostArchitecture => RuntimeInformation.ProcessArchitecture == Architecture.Arm64
        ? "arm64"
        : "x64";

    private static string CreateOldSource(CompileTaskTestContext ctx)
    {
        var source = ctx.CreateDummySource("kernel.cpp", "// source");
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(-10));
        return source;
    }

    // Counts the compilations and writes a library file; the description is the version, the architecture and the
    // sanitizers, so that a change of any of them changes the command record.
    private sealed class CountingToolchain : INativeToolchain
    {
        private readonly ConcurrentBag<string> _compiledArchitectures = [];
        private int _compileCount;

        public string Version { get; set; } = "compiler 1";
        public bool FailCompilation { get; set; }
        public int CompileCount => Volatile.Read(ref _compileCount);
        public IReadOnlyCollection<string> CompiledArchitectures => _compiledArchitectures;

        public string Name => "Counting";
        public bool IsWindows => false;

        public ToolchainPreparationResult PrepareEnvironment(
            string[] architectures,
            TaskLoggingHelper log,
            CancellationToken cancellationToken = default) => ToolchainPreparationResult.Ready;

        public Dictionary<string, string>? GetEnvironmentVariables(string architecture) => null;

        public bool TryCompileCppToSharedLibrary(
            string sourcePath,
            string outputPath,
            string architecture,
            TaskLoggingHelper log,
            CancellationToken cancellationToken = default,
            NativeSanitizers sanitizers = NativeSanitizers.None)
        {
            Interlocked.Increment(ref _compileCount);
            _compiledArchitectures.Add(architecture);
            if (FailCompilation)
            {
                return false;
            }

            File.WriteAllText(outputPath, "dll");
            return true;
        }

        public string DescribeCompilation(
            string sourcePath,
            string outputPath,
            string architecture,
            NativeSanitizers sanitizers) => $"{Version}\n{architecture}\n{sanitizers}\n{sourcePath}";
    }
}
