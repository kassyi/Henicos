#nullable enable
using System.Runtime.InteropServices;
using Kassyi.GSharp.Compiler.Tasks;
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
                 i.Arguments.StartsWith("-c ", StringComparison.Ordinal) &&
                 i.Arguments.Contains("-O2") &&
                 i.Arguments.Contains("-fPIC") &&
                 i.Arguments.Contains("-std=c++17") &&
                 i.Arguments.Contains("-fopenmp") &&
                 i.Arguments.Contains("-Wno-unused-label"));
        Assert.Contains(ctx.Runner.Invocations,
            i => i.FileName == compiler &&
                 i.Arguments.Contains("-shared") &&
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
    public void CompileCppToNativeTask_SplitUnits_LinksSingleModuleDllInManifestOrder()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("Kassyi.GSharp.Kernels_MyAsm.g.cpp", "// unit 0");
        ctx.CreateDummySource("Kassyi.GSharp.Kernels_MyAsm.1.g.cpp", "// unit 1");
        ctx.CreateDummySource("Kassyi.GSharp.Kernels_MyAsm.2.g.cpp", "// unit 2");

        var task = ctx.CreateCppTask([
            "Kassyi.GSharp.Kernels_MyAsm.g.cpp",
            "Kassyi.GSharp.Kernels_MyAsm.1.g.cpp",
            "Kassyi.GSharp.Kernels_MyAsm.2.g.cpp"
        ]);

        var hostArch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        Assert.True(task.Execute());
        var output = Assert.Single(task.OutputDlls);
        Assert.Equal(
            $"Kassyi.GSharp.Kernels_MyAsm_{hostArch}.dll",
            Path.GetFileName(output.ItemSpec));
        Assert.Equal(
            $"Kassyi.GSharp.Kernels.Kassyi.GSharp.Kernels_MyAsm_{hostArch}.dll",
            output.GetMetadata("LogicalName"));

        var linkInvocation = Assert.Single(
            ctx.Runner.Invocations,
            i => i.FileName.Contains("cl.exe") && i.Arguments.Contains("/LD "));
        var obj0Idx = linkInvocation.Arguments.IndexOf("Kassyi.GSharp.Kernels_MyAsm.obj", StringComparison.Ordinal);
        var obj1Idx = linkInvocation.Arguments.IndexOf("Kassyi.GSharp.Kernels_MyAsm.1.obj", StringComparison.Ordinal);
        var obj2Idx = linkInvocation.Arguments.IndexOf("Kassyi.GSharp.Kernels_MyAsm.2.obj", StringComparison.Ordinal);
        Assert.True(obj0Idx >= 0 && obj1Idx > obj0Idx && obj2Idx > obj1Idx);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Scenario(TestScenario.Boundary)]
    public void CompileCppToNativeTask_StaleObjectsInObjDir_ExcludesUnlistedObjectsFromLink(bool isMsvc)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("Module.g.cpp", "// unit 0");
        ctx.CreateDummySource("Module.1.g.cpp", "// unit 1");

        var objDir = Path.Combine(ctx.TempDir, "objs");
        var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        var archDir = Path.Combine(objDir, arch);
        Directory.CreateDirectory(archDir);
        var ext = isMsvc ? ".obj" : ".o";
        File.WriteAllText(Path.Combine(archDir, $"Module.99{ext}"), "stale unit");
        File.WriteAllText(Path.Combine(archDir, $"OldModule{ext}"), "stale module");

        var toolchain = isMsvc ? (Kassyi.GSharp.Compiler.Tasks.Toolchains.INativeToolchain)ctx.CreateMsvcToolchain()
            : ctx.CreateGccClangToolchain("g++");
        var task = ctx.CreateCppTask(["Module.g.cpp", "Module.1.g.cpp"], toolchain, objDir);

        Assert.True(task.Execute());
        Assert.Single(task.OutputDlls);

        var linkInvocation = Assert.Single(
            ctx.Runner.Invocations,
            i => isMsvc
                ? i.FileName.Contains("cl.exe") && i.Arguments.Contains("/LD ")
                : i.FileName == "g++" && i.Arguments.Contains("-shared"));
        Assert.Contains($"Module{ext}", linkInvocation.Arguments);
        Assert.Contains($"Module.1{ext}", linkInvocation.Arguments);
        Assert.DoesNotContain($"Module.99{ext}", linkInvocation.Arguments);
        Assert.DoesNotContain($"OldModule{ext}", linkInvocation.Arguments);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CompileCppToNativeTask_NonNumericDotSuffix_KeepsSeparateModuleDlls()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("MyMod.g.cpp", "// main");
        ctx.CreateDummySource("MyMod.extra.g.cpp", "// extra");
        ctx.CreateDummySource("kernel1.cpp", "// k1");
        ctx.CreateDummySource("kernel2.cpp", "// k2");

        var task = ctx.CreateCppTask(["MyMod.g.cpp", "MyMod.extra.g.cpp", "kernel1.cpp", "kernel2.cpp"]);

        Assert.True(task.Execute());
        Assert.Equal(4, task.OutputDlls.Length);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Scenario(TestScenario.Boundary)]
    public void CompileCppToNativeTask_DuplicateIdenticalSources_DeduplicatesAndLinksOnce(bool isMsvc)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("Mod.g.hpp", "// header");
        ctx.CreateDummySource("Mod.g.cpp", "// unit 0");
        ctx.CreateDummySource("Mod.1.g.cpp", "// unit 1");

        var toolchain = isMsvc
            ? (Kassyi.GSharp.Compiler.Tasks.Toolchains.INativeToolchain)ctx.CreateMsvcToolchain()
            : ctx.CreateGccClangToolchain("g++");
        var task = ctx.CreateCppTask(["Mod.g.cpp", "Mod.g.cpp", "Mod.1.g.cpp"], toolchain);
        task.CppSources =
        [
            new Microsoft.Build.Utilities.TaskItem(Path.Combine(ctx.TempDir, "Mod.g.cpp")),
            new Microsoft.Build.Utilities.TaskItem(Path.Combine(ctx.TempDir, ".", "Mod.g.cpp")),
            new Microsoft.Build.Utilities.TaskItem(Path.Combine(ctx.TempDir, "Mod.1.g.cpp"))
        ];

        Assert.True(task.Execute());
        Assert.Empty(ctx.BuildEngine.LoggedErrors);
        Assert.Single(task.OutputDlls);
        Assert.Equal(4, task.OutputIntermediateFiles.Length);

        var compileCalls = ctx.Runner.Invocations.Where(i => isMsvc
            ? i.FileName.Contains("cl.exe") && i.Arguments.Contains("/c ")
            : i.FileName == "g++" && i.Arguments.StartsWith("-c ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, compileCalls.Length);
    }

    [Theory]
    [InlineData("Mod.g.cpp", "Mod.cpp")]
    [InlineData("dirA/kernel.cpp", "dirB/kernel.cpp")]
    [Scenario(TestScenario.SadPath)]
    public void CompileCppToNativeTask_DistinctSourcesWithCollidingObjectStem_LogsErrorAndFails(
        string firstSource,
        string secondSource)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource(firstSource, "// first");
        ctx.CreateDummySource(secondSource, "// second");

        var task = ctx.CreateCppTask([firstSource, secondSource], ctx.CreateGccClangToolchain("g++"));

        Assert.False(task.Execute());
        Assert.Empty(task.OutputDlls);
        var error = Assert.Single(ctx.BuildEngine.LoggedErrors);
        Assert.Contains("same object unit stem", error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            ctx.Runner.Invocations,
            i => i.FileName == "g++" && i.Arguments.StartsWith("-c ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(".g.cpp")]
    [InlineData(".cpp")]
    [Scenario(TestScenario.SadPath)]
    public void CompileCppToNativeTask_ExtensionOnlySourceFileName_LogsErrorAndFails(string fileName)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource(fileName, "// invalid stem");

        var task = ctx.CreateCppTask([fileName], ctx.CreateGccClangToolchain("g++"));

        Assert.False(task.Execute());
        Assert.Empty(task.OutputDlls);
        var error = Assert.Single(ctx.BuildEngine.LoggedErrors);
        Assert.Contains("non-empty file stem", error);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CompileCppToNativeTask_CaseDistinctSourcesOnPosixToolchain_BuildsSeparateModules()
    {
        using var ctx = new CompileTaskTestContext();
        // On a Windows NTFS host, mod.o and Mod.o alias on disk even while mocking a case-sensitive POSIX toolchain.
        ctx.BuildEngine.AvailableCores = 1;
        ctx.CreateDummySource("sub1/mod.cpp", "// lower");
        ctx.CreateDummySource("sub2/Mod.cpp", "// upper");

        var hostArch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        var task = ctx.CreateCppTask(["sub1/mod.cpp", "sub2/Mod.cpp"], ctx.CreateGccClangToolchain("g++"));

        Assert.True(task.Execute());
        Assert.Equal(2, task.OutputDlls.Length);

        var dllNames = new HashSet<string>(
            task.OutputDlls.Select(static item => Path.GetFileName(item.ItemSpec)),
            StringComparer.Ordinal);
        Assert.Contains($"mod_{hostArch}.dll", dllNames);
        Assert.Contains($"Mod_{hostArch}.dll", dllNames);

        var linkCalls = ctx.Runner.Invocations
            .Where(i => i.FileName == "g++" && i.Arguments.Contains("-shared"))
            .ToArray();
        Assert.Equal(2, linkCalls.Length);
    }
}
