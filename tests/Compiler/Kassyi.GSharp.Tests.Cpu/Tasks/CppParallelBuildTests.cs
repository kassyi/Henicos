#nullable enable
using System.Diagnostics;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Compiler.Tasks;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Tasks;

[Collection(TestCollections.GlobalStateSequential)]
[Category(TestCategory.Compiler)]
public class CppParallelBuildTests
{
    static CppParallelBuildTests()
    {
        MSBuildLocatorSetup.EnsureRegistered();
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CompileCppToNativeTask_IBuildEngine9_BoundsConcurrencyAndReleasesCores()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.BuildEngine.AvailableCores = 3;

        var sources = new string[6];
        for (var i = 0; i < sources.Length; i++)
        {
            var name = i == 0 ? "Mod.g.cpp" : $"Mod.{i}.g.cpp";
            ctx.CreateDummySource(name, $"// unit {i}");
            sources[i] = name;
        }

        ctx.Runner.OnRun = (info, _) =>
        {
            if (info.Arguments.StartsWith("-c ", StringComparison.Ordinal))
            {
                Thread.Sleep(25);
            }
        };

        var task = ctx.CreateCppTask(sources, ctx.CreateGccClangToolchain("g++"));
        Assert.True(task.Execute());

        Assert.Single(task.OutputDlls);
        Assert.Equal(12, task.OutputIntermediateFiles.Length);
        Assert.Equal(6, ctx.BuildEngine.RequestedCores);
        Assert.Equal(3, ctx.BuildEngine.GrantedCores);
        Assert.Equal(3, ctx.BuildEngine.ReleasedCores);
        Assert.Equal(0, ctx.BuildEngine.HeldCores);
        Assert.InRange(ctx.Runner.PeakConcurrency, 1, 3);
        Assert.Contains(ctx.BuildEngine.LoggedMessages, m => m.Contains("using 3 core(s)"));
        Assert.Contains(ctx.BuildEngine.LoggedMessages, m => m.Contains("Compiled Mod.g.cpp"));
        Assert.Contains(ctx.BuildEngine.LoggedMessages, m => m.Contains("Linked Mod_"));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CompileCppToNativeTask_IBuildEngine9SingleCore_RunsSequentially()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.BuildEngine.AvailableCores = 1;
        ctx.CreateDummySource("Mod.g.cpp", "// 0");
        ctx.CreateDummySource("Mod.1.g.cpp", "// 1");
        ctx.CreateDummySource("Mod.2.g.cpp", "// 2");

        ctx.Runner.OnRun = (info, _) =>
        {
            if (info.Arguments.StartsWith("-c ", StringComparison.Ordinal))
            {
                Thread.Sleep(15);
            }
        };

        var task = ctx.CreateCppTask(["Mod.g.cpp", "Mod.1.g.cpp", "Mod.2.g.cpp"], ctx.CreateGccClangToolchain("g++"));
        Assert.True(task.Execute());
        Assert.Equal(1, ctx.Runner.PeakConcurrency);
        Assert.Equal(0, ctx.BuildEngine.HeldCores);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void CompileCppToNativeTask_CompileFailure_ReleasesGrantedCoresAndDeletesRecord()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("Mod.g.cpp", "// 0");
        ctx.CreateDummySource("Mod.1.g.cpp", "// 1");

        var objDir = Path.Combine(ctx.TempDir, "objs");
        var first = ctx.CreateCppTask(["Mod.g.cpp", "Mod.1.g.cpp"], ctx.CreateGccClangToolchain("g++"), objDir);
        Assert.True(first.Execute());

        ctx.CreateDummySource("Mod.g.cpp", "// 0 edited");
        ctx.CreateDummySource("Mod.1.g.cpp", "// 1 edited");
        ctx.Runner.NextExitCode = 1;
        ctx.Runner.NextStandardError = "g++: error: syntax error";

        var second = ctx.CreateCppTask(["Mod.g.cpp", "Mod.1.g.cpp"], ctx.CreateGccClangToolchain("g++"), objDir);
        Assert.False(second.Execute());
        Assert.Equal(0, ctx.BuildEngine.HeldCores);
        Assert.Equal(ctx.BuildEngine.GrantedCores, ctx.BuildEngine.ReleasedCores);

        ctx.Runner.NextExitCode = 0;
        ctx.Runner.NextStandardError = "";
        ctx.Runner.Invocations.Clear();
        var third = ctx.CreateCppTask(["Mod.g.cpp", "Mod.1.g.cpp"], ctx.CreateGccClangToolchain("g++"), objDir);
        Assert.True(third.Execute());
        Assert.Equal(2, CountGccCompiles(ctx.Runner));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void CompileCppToNativeTask_RunnerThrows_ReleasesGrantedCores()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("Mod.g.cpp", "// 0");
        ctx.CreateDummySource("Mod.1.g.cpp", "// 1");

        var task = ctx.CreateCppTask(["Mod.g.cpp", "Mod.1.g.cpp"], ctx.CreateGccClangToolchain("g++"));
        ctx.Runner.NextException = new InvalidOperationException("boom");

        Assert.False(task.Execute());
        Assert.Equal(0, ctx.BuildEngine.HeldCores);
        Assert.Equal(ctx.BuildEngine.GrantedCores, ctx.BuildEngine.ReleasedCores);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void CompileCppToNativeTask_CanceledDuringParallelCompile_ReleasesCoresAndLogsWarning()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("Mod.g.cpp", "// 0");
        ctx.CreateDummySource("Mod.1.g.cpp", "// 1");
        ctx.CreateDummySource("Mod.2.g.cpp", "// 2");
        ctx.CreateDummySource("Mod.3.g.cpp", "// 3");

        var task = ctx.CreateCppTask(
            ["Mod.g.cpp", "Mod.1.g.cpp", "Mod.2.g.cpp", "Mod.3.g.cpp"],
            ctx.CreateGccClangToolchain("g++"));

        ctx.Runner.OnRun = (info, _) =>
        {
            if (info.Arguments.StartsWith("-c ", StringComparison.Ordinal))
            {
                task.Cancel();
            }
        };

        Assert.False(task.Execute());
        Assert.Equal(0, ctx.BuildEngine.HeldCores);
        Assert.Equal(ctx.BuildEngine.GrantedCores, ctx.BuildEngine.ReleasedCores);
        Assert.Contains(
            ctx.BuildEngine.LoggedWarnings,
            w => w.Contains("canceled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CompileCppToNativeTask_LegacyBuildEngine_FallsBackCleanly()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("Mod.g.cpp", "// 0");
        ctx.CreateDummySource("Mod.1.g.cpp", "// 1");

        var legacyEngine = new LegacyMockBuildEngine();
        var task = ctx.CreateCppTask(["Mod.g.cpp", "Mod.1.g.cpp"], ctx.CreateGccClangToolchain("g++"));
        task.BuildEngine = legacyEngine;

        Assert.True(task.Execute());
        Assert.Single(task.OutputDlls);
        Assert.Contains(legacyEngine.LoggedMessages, m => m.Contains("IBuildEngine9=False"));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CompileCppToNativeTask_UnchangedUnits_SkipsAllCompilesAndStillLinks()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("Mod.g.hpp", "// header");
        ctx.CreateDummySource("Mod.g.cpp", "// unit 0");
        ctx.CreateDummySource("Mod.1.g.cpp", "// unit 1");
        var objDir = Path.Combine(ctx.TempDir, "objs");

        var first = ctx.CreateCppTask(["Mod.g.cpp", "Mod.1.g.cpp"], ctx.CreateGccClangToolchain("g++"), objDir);
        Assert.True(first.Execute());
        Assert.Equal(2, CountGccCompiles(ctx.Runner));
        Assert.Equal(1, CountGccLinks(ctx.Runner));

        ctx.Runner.Invocations.Clear();
        var second = ctx.CreateCppTask(["Mod.g.cpp", "Mod.1.g.cpp"], ctx.CreateGccClangToolchain("g++"), objDir);
        Assert.True(second.Execute());
        Assert.Equal(0, CountGccCompiles(ctx.Runner));
        Assert.Equal(1, CountGccLinks(ctx.Runner));
        Assert.Equal(4, second.OutputIntermediateFiles.Length);
        Assert.Contains(ctx.BuildEngine.LoggedMessages, m => m.Contains("all 2 translation unit(s) are up-to-date"));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CompileCppToNativeTask_SingleUnitEdited_RecompilesOnlyChangedUnit()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("Mod.g.hpp", "// header");
        ctx.CreateDummySource("Mod.g.cpp", "// unit 0");
        ctx.CreateDummySource("Mod.1.g.cpp", "// unit 1");
        ctx.CreateDummySource("Mod.2.g.cpp", "// unit 2");
        var objDir = Path.Combine(ctx.TempDir, "objs");

        Assert.True(ctx.CreateCppTask(
            ["Mod.g.cpp", "Mod.1.g.cpp", "Mod.2.g.cpp"],
            ctx.CreateGccClangToolchain("g++"),
            objDir).Execute());

        ctx.Runner.Invocations.Clear();
        ctx.CreateDummySource("Mod.1.g.cpp", "// unit 1 changed");

        Assert.True(ctx.CreateCppTask(
            ["Mod.g.cpp", "Mod.1.g.cpp", "Mod.2.g.cpp"],
            ctx.CreateGccClangToolchain("g++"),
            objDir).Execute());

        Assert.Equal(1, CountGccCompiles(ctx.Runner));
        Assert.Contains(
            ctx.Runner.Invocations,
            i => i.Arguments.StartsWith("-c ", StringComparison.Ordinal) && i.Arguments.Contains("Mod.1.g.cpp"));
        Assert.Equal(1, CountGccLinks(ctx.Runner));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CompileCppToNativeTask_SharedHeaderEdited_RecompilesAllModuleUnits()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("Mod.g.hpp", "// header v1");
        ctx.CreateDummySource("Mod.g.cpp", "// unit 0");
        ctx.CreateDummySource("Mod.1.g.cpp", "// unit 1");
        var objDir = Path.Combine(ctx.TempDir, "objs");

        Assert.True(ctx.CreateCppTask(["Mod.g.cpp", "Mod.1.g.cpp"], ctx.CreateGccClangToolchain("g++"), objDir)
            .Execute());

        ctx.Runner.Invocations.Clear();
        ctx.CreateDummySource("Mod.g.hpp", "// header v2");

        Assert.True(ctx.CreateCppTask(["Mod.g.cpp", "Mod.1.g.cpp"], ctx.CreateGccClangToolchain("g++"), objDir)
            .Execute());

        Assert.Equal(2, CountGccCompiles(ctx.Runner));
        Assert.Equal(1, CountGccLinks(ctx.Runner));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Scenario(TestScenario.Boundary)]
    public void CompileCppToNativeTask_MissingObjOrRecord_RecompilesOnlyAffectedUnit(bool deleteObj)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("Mod.g.cpp", "// unit 0");
        ctx.CreateDummySource("Mod.1.g.cpp", "// unit 1");
        var objDir = Path.Combine(ctx.TempDir, "objs");

        Assert.True(ctx.CreateCppTask(["Mod.g.cpp", "Mod.1.g.cpp"], ctx.CreateGccClangToolchain("g++"), objDir)
            .Execute());

        var hostArch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        var obj1 = Path.Combine(objDir, hostArch, "Mod.1.o");
        File.Delete(deleteObj ? obj1 : obj1 + CompileCppToNativeTask.CommandRecordExtension);

        ctx.Runner.Invocations.Clear();
        Assert.True(ctx.CreateCppTask(["Mod.g.cpp", "Mod.1.g.cpp"], ctx.CreateGccClangToolchain("g++"), objDir)
            .Execute());

        Assert.Equal(1, CountGccCompiles(ctx.Runner));
        Assert.Contains(
            ctx.Runner.Invocations,
            i => i.Arguments.StartsWith("-c ", StringComparison.Ordinal) && i.Arguments.Contains("Mod.1.g.cpp"));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CompileCppToNativeTask_CompilerVersionOrSanitizersChanged_RecompilesAllUnits()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("Mod.g.cpp", "// unit 0");
        ctx.CreateDummySource("Mod.1.g.cpp", "// unit 1");
        var objDir = Path.Combine(ctx.TempDir, "objs");

        ctx.Runner.VersionCheckOutput = "g++ (Ubuntu) 13.2.0";
        Assert.True(ctx.CreateCppTask(["Mod.g.cpp", "Mod.1.g.cpp"], ctx.CreateGccClangToolchain("g++"), objDir)
            .Execute());

        ctx.Runner.Invocations.Clear();
        ctx.Runner.VersionCheckOutput = "g++ (Ubuntu) 14.1.0";
        Assert.True(ctx.CreateCppTask(["Mod.g.cpp", "Mod.1.g.cpp"], ctx.CreateGccClangToolchain("g++"), objDir)
            .Execute());
        Assert.Equal(2, CountGccCompiles(ctx.Runner));

        ctx.Runner.Invocations.Clear();
        var sanitizedTask = ctx.CreateCppTask(
            ["Mod.g.cpp", "Mod.1.g.cpp"],
            ctx.CreateGccClangToolchain("g++"),
            objDir);
        sanitizedTask.GSharpNativeSanitizers = "address";
        Assert.True(sanitizedTask.Execute());
        Assert.Equal(2, CountGccCompiles(ctx.Runner));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CompileCppToNativeTask_MsvcCompilerPathChanged_RecompilesAllUnits()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("Mod.g.cpp", "// unit 0");
        var objDir = Path.Combine(ctx.TempDir, "objs");

        var clDir1 = Path.Combine(ctx.TempDir, "msvc1");
        var clDir2 = Path.Combine(ctx.TempDir, "msvc2");
        Directory.CreateDirectory(clDir1);
        Directory.CreateDirectory(clDir2);
        File.WriteAllText(Path.Combine(clDir1, "cl.exe"), "");
        File.WriteAllText(Path.Combine(clDir2, "cl.exe"), "");

        ctx.Runner.CmdStandardOutput = $"VCToolsVersion=14.40.0\r\nPATH={clDir1}";
        Assert.True(ctx.CreateCppTask(["Mod.g.cpp"], ctx.CreateMsvcToolchain(), objDir).Execute());

        ctx.Runner.Invocations.Clear();
        ctx.Runner.CmdStandardOutput = $"VCToolsVersion=14.40.0\r\nPATH={clDir2}";
        Assert.True(ctx.CreateCppTask(["Mod.g.cpp"], ctx.CreateMsvcToolchain(), objDir).Execute());

        Assert.Single(ctx.Runner.Invocations, i => i.FileName.Contains("cl.exe") && i.Arguments.Contains("/c "));
    }

    // cl.exe adds the CL variable to its arguments, so a CL that changes between builds must recompile the unit.
    // mutant: MsvcToolchain.GetCompilerIdentity without the environment argument (the record misses CL)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CompileCppToNativeTask_MsvcClEnvironmentChanged_RecompilesTheUnit()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("Mod.g.cpp", "// unit 0");
        var objDir = Path.Combine(ctx.TempDir, "objs");
        var clDir = Path.Combine(ctx.TempDir, "msvc");
        Directory.CreateDirectory(clDir);
        File.WriteAllText(Path.Combine(clDir, "cl.exe"), "");

        ctx.Runner.CmdStandardOutput = $"VCToolsVersion=14.40.0\r\nPATH={clDir}";
        Assert.True(ctx.CreateCppTask(["Mod.g.cpp"], ctx.CreateMsvcToolchain(), objDir).Execute());

        ctx.Runner.Invocations.Clear();
        ctx.Runner.CmdStandardOutput = $"VCToolsVersion=14.40.0\r\nPATH={clDir}\r\nCL=/DFOO=1";
        Assert.True(ctx.CreateCppTask(["Mod.g.cpp"], ctx.CreateMsvcToolchain(), objDir).Execute());

        Assert.Single(ctx.Runner.Invocations, i => i.FileName.Contains("cl.exe") && i.Arguments.Contains("/c "));
    }

    // cl.exe starts from the build's environment with the vcvars variables over it, so a CL that only the build's
    // environment sets must also recompile the unit.
    // mutant: DescribeEnvironment reads only the vcvars variables when it has them
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CompileCppToNativeTask_MsvcClOutsideVcvarsChanged_RecompilesTheUnit()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("Mod.g.cpp", "// unit 0");
        var objDir = Path.Combine(ctx.TempDir, "objs");
        var clDir = Path.Combine(ctx.TempDir, "msvc");
        Directory.CreateDirectory(clDir);
        File.WriteAllText(Path.Combine(clDir, "cl.exe"), "");
        ctx.Runner.CmdStandardOutput = $"VCToolsVersion=14.40.0\r\nPATH={clDir}";

        ctx.SetEnv("CL", null);
        Assert.True(ctx.CreateCppTask(["Mod.g.cpp"], ctx.CreateMsvcToolchain(), objDir).Execute());

        ctx.Runner.Invocations.Clear();
        ctx.SetEnv("CL", "/DFOO=1");
        Assert.True(ctx.CreateCppTask(["Mod.g.cpp"], ctx.CreateMsvcToolchain(), objDir).Execute());

        Assert.Single(ctx.Runner.Invocations, i => i.FileName.Contains("cl.exe") && i.Arguments.Contains("/c "));
    }

    // GCC and Clang read include paths, extra options and the folders of their compiler passes from the
    // environment, so a change of any of them between builds must recompile every unit.
    // mutant: GccClangToolchain.GetCompilerIdentity without the environment argument, or without one of the names
    [Theory]
    [InlineData("g++", "CPATH", "/opt/include")]
    [InlineData("g++", "CPLUS_INCLUDE_PATH", "/opt/include")]
    [InlineData("clang++", "CCC_OVERRIDE_OPTIONS", "+-DFOO=1")]
    [InlineData("g++", "GCC_EXEC_PREFIX", "/opt/gcc/lib/gcc/")]
    [InlineData("g++", "COMPILER_PATH", "/opt/gcc/libexec")]
    [Scenario(TestScenario.Boundary)]
    public void CompileCppToNativeTask_GccClangEnvironmentChanged_RecompilesAllUnits(
        string compiler, string name, string value)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("Mod.g.cpp", "// unit 0");
        ctx.CreateDummySource("Mod.1.g.cpp", "// unit 1");
        var objDir = Path.Combine(ctx.TempDir, "objs");

        ctx.SetEnv(name, null);
        Assert.True(ctx.CreateCppTask(["Mod.g.cpp", "Mod.1.g.cpp"], ctx.CreateGccClangToolchain(compiler), objDir)
            .Execute());

        ctx.Runner.Invocations.Clear();
        ctx.SetEnv(name, value);
        Assert.True(ctx.CreateCppTask(["Mod.g.cpp", "Mod.1.g.cpp"], ctx.CreateGccClangToolchain(compiler), objDir)
            .Execute());

        Assert.Equal(2, ctx.Runner.Invocations.Count(i =>
            i.FileName == compiler && i.Arguments.StartsWith("-c ", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Scenario(TestScenario.Boundary)]
    public void CompileCppToNativeTask_ZeroByteObjectWithCurrentRecord_RecompilesAndSucceeds(bool isMsvc)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("Mod.g.hpp", "// header");
        ctx.CreateDummySource("Mod.g.cpp", "// unit 0");
        ctx.CreateDummySource("Mod.1.g.cpp", "// unit 1");
        var objDir = Path.Combine(ctx.TempDir, "objs");
        var toolchain = isMsvc
            ? (Kassyi.GSharp.Compiler.Tasks.Toolchains.INativeToolchain)ctx.CreateMsvcToolchain()
            : ctx.CreateGccClangToolchain("g++");

        Assert.True(ctx.CreateCppTask(["Mod.g.cpp", "Mod.1.g.cpp"], toolchain, objDir).Execute());

        var hostArch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        var objExt = isMsvc ? ".obj" : ".o";
        var obj1Path = Path.Combine(objDir, hostArch, $"Mod.1{objExt}");
        var rec1Path = obj1Path + CompileCppToNativeTask.CommandRecordExtension;
        Assert.True(File.Exists(rec1Path));

        // Truncate the object file to 0 bytes while keeping its command record intact.
        File.WriteAllBytes(obj1Path, []);
        Assert.Equal(0L, new FileInfo(obj1Path).Length);

        ctx.Runner.Invocations.Clear();
        var second = ctx.CreateCppTask(["Mod.g.cpp", "Mod.1.g.cpp"], toolchain, objDir);
        Assert.True(second.Execute());
        Assert.True(new FileInfo(obj1Path).Length > 0);

        var compileCalls = ctx.Runner.Invocations.Where(i => isMsvc
            ? i.FileName.Contains("cl.exe") && i.Arguments.Contains("/c ")
            : i.FileName == "g++" && i.Arguments.StartsWith("-c ", StringComparison.Ordinal)).ToArray();
        var compiledUnit = Assert.Single(compileCalls);
        Assert.Contains("Mod.1.g.cpp", compiledUnit.Arguments);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CompileCppToNativeTask_NativeBarrierModelChanged_RecompilesAllUnits()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("Mod.g.cpp", "// unit 0");
        ctx.CreateDummySource("Mod.1.g.cpp", "// unit 1");
        var objDir = Path.Combine(ctx.TempDir, "objs");
        var hostArch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";

        var stateMachine1 = ctx.CreateCppTask(
            ["Mod.g.cpp", "Mod.1.g.cpp"], ctx.CreateGccClangToolchain("g++"), objDir);
        stateMachine1.GSharpNativeBarrierModel = "StateMachine";
        Assert.True(stateMachine1.Execute());
        Assert.Equal(2, CountGccCompiles(ctx.Runner));

        ctx.Runner.Invocations.Clear();
        var stateMachine2 = ctx.CreateCppTask(
            ["Mod.g.cpp", "Mod.1.g.cpp"], ctx.CreateGccClangToolchain("g++"), objDir);
        stateMachine2.GSharpNativeBarrierModel = "StateMachine";
        Assert.True(stateMachine2.Execute());
        Assert.Equal(0, CountGccCompiles(ctx.Runner));

        ctx.Runner.Invocations.Clear();
        var stackTask = ctx.CreateCppTask(
            ["Mod.g.cpp", "Mod.1.g.cpp"], ctx.CreateGccClangToolchain("g++"), objDir);
        stackTask.GSharpNativeBarrierModel = "Stack";
        Assert.True(stackTask.Execute());
        Assert.Equal(2, CountGccCompiles(ctx.Runner));
        Assert.All(
            ctx.Runner.Invocations.Where(i => i.FileName == "g++" && i.Arguments.StartsWith("-c ", StringComparison.Ordinal)),
            i => Assert.Contains("-fstack-clash-protection", i.Arguments, StringComparison.Ordinal));
        var recordPath = Path.Combine(objDir, hostArch, "Mod.o" + CompileCppToNativeTask.CommandRecordExtension);
        Assert.Contains("-fstack-clash-protection", File.ReadAllText(recordPath), StringComparison.Ordinal);

        ctx.Runner.Invocations.Clear();
        var stateMachine3 = ctx.CreateCppTask(
            ["Mod.g.cpp", "Mod.1.g.cpp"], ctx.CreateGccClangToolchain("g++"), objDir);
        stateMachine3.GSharpNativeBarrierModel = "StateMachine";
        Assert.True(stateMachine3.Execute());
        Assert.Equal(2, CountGccCompiles(ctx.Runner));
        Assert.All(
            ctx.Runner.Invocations.Where(i => i.FileName == "g++" && i.Arguments.StartsWith("-c ", StringComparison.Ordinal)),
            i => Assert.DoesNotContain("-fstack-clash-protection", i.Arguments, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Scenario(TestScenario.SadPath)]
    public void Toolchain_TryCompileToObjectEmptyOutputWithExitZero_FailsAndOmitsRecord(bool isMsvc)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("Mod.g.cpp", "// unit 0");
        var objDir = Path.Combine(ctx.TempDir, "objs");
        var hostArch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        var objExt = isMsvc ? ".obj" : ".o";
        var objPath = Path.Combine(objDir, hostArch, $"Mod{objExt}");

        ctx.Runner.SimulateCompilationOutput = false;
        ctx.Runner.OnRun = (info, _) =>
        {
            var isCompile = isMsvc
                ? info.FileName.Contains("cl.exe") && info.Arguments.Contains("/c ")
                : info.FileName == "g++" && info.Arguments.StartsWith("-c ", StringComparison.Ordinal);
            if (isCompile)
            {
                Directory.CreateDirectory(Path.Combine(objDir, hostArch));
                File.WriteAllBytes(objPath, []);
            }
        };

        var toolchain = isMsvc
            ? (Kassyi.GSharp.Compiler.Tasks.Toolchains.INativeToolchain)ctx.CreateMsvcToolchain()
            : ctx.CreateGccClangToolchain("g++");
        var task = ctx.CreateCppTask(["Mod.g.cpp"], toolchain, objDir);

        Assert.False(task.Execute());
        Assert.Contains(ctx.BuildEngine.LoggedErrors, e => e.Contains("exited with code 0"));
        Assert.False(File.Exists(objPath + CompileCppToNativeTask.CommandRecordExtension));
    }

    private static int CountGccCompiles(MockProcessRunner runner) =>
        runner.Invocations.Count(i =>
            i.FileName == "g++" && i.Arguments.StartsWith("-c ", StringComparison.Ordinal));

    private static int CountGccLinks(MockProcessRunner runner) =>
        runner.Invocations.Count(i =>
            i.FileName == "g++" && i.Arguments.Contains("-shared"));
}
