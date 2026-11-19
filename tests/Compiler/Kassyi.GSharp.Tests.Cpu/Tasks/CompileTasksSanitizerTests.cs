#nullable enable
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Tasks;

[Collection(TestCollections.GlobalStateSequential)]
public class CompileTasksSanitizerTests
{
    static CompileTasksSanitizerTests()
    {
        MSBuildLocatorSetup.EnsureRegistered();
    }

    [Theory]
    [InlineData("g++", "address;undefined", "-fsanitize=address,undefined -fno-sanitize-recover=undefined -fno-omit-frame-pointer -g")]
    [InlineData("clang++", "address,undefined", "-fsanitize=address,undefined -fno-sanitize-recover=undefined -fno-omit-frame-pointer -g")]
    [InlineData("g++", "ADDRESS ; UNDEFINED", "-fsanitize=address,undefined -fno-sanitize-recover=undefined -fno-omit-frame-pointer -g")]
    [InlineData("g++", "address", "-fsanitize=address -fno-omit-frame-pointer -g")]
    [InlineData("clang++", "undefined", "-fsanitize=undefined -fno-sanitize-recover=undefined -fno-omit-frame-pointer -g")]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void CompileCppToNativeTask_GccClangToolchain_SanitizersEnabled_PassesSanitizerFlags(
        string compiler,
        string sanitizers,
        string expectedFlags)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cpp", "// source");

        var toolchain = ctx.CreateGccClangToolchain(compiler);
        var task = ctx.CreateCppTask(["kernel.cpp"], toolchain);
        task.GSharpNativeSanitizers = sanitizers;

        Assert.True(task.Execute());
        Assert.Single(task.OutputDlls);
        var compileInvocation = Assert.Single(
            ctx.Runner.Invocations,
            i => i.FileName == compiler && i.Arguments.Contains(" -c ") ||
                 (i.FileName == compiler && i.Arguments.StartsWith("-c ", StringComparison.Ordinal)));
        var linkInvocation = Assert.Single(
            ctx.Runner.Invocations,
            i => i.FileName == compiler && i.Arguments.Contains("-shared"));
        Assert.Contains(expectedFlags, compileInvocation.Arguments);
        Assert.Contains(expectedFlags, linkInvocation.Arguments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",")]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void CompileCppToNativeTask_GccClangToolchain_SanitizersDisabled_OmitsSanitizerFlags(string sanitizers)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cpp", "// source");

        var toolchain = ctx.CreateGccClangToolchain("g++");
        var task = ctx.CreateCppTask(["kernel.cpp"], toolchain);
        task.GSharpNativeSanitizers = sanitizers;

        Assert.True(task.Execute());
        var compileInvocation = Assert.Single(
            ctx.Runner.Invocations,
            i => i.FileName == "g++" && i.Arguments.StartsWith("-c ", StringComparison.Ordinal));
        var linkInvocation = Assert.Single(
            ctx.Runner.Invocations,
            i => i.FileName == "g++" && i.Arguments.Contains("-shared"));
        Assert.DoesNotContain("-fsanitize", compileInvocation.Arguments);
        Assert.DoesNotContain("-fsanitize", linkInvocation.Arguments);
    }

    [Theory]
    [InlineData("address")]
    [InlineData("address;undefined")]
    [InlineData("address,undefined")]
    [InlineData("ADDRESS")]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void CompileCppToNativeTask_MsvcToolchain_SanitizersEnabled_PassesAddressSanitizerFlags(string sanitizers)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cpp", "// source");

        var task = ctx.CreateCppTask(["kernel.cpp"], ctx.CreateMsvcToolchain());
        task.GSharpNativeSanitizers = sanitizers;

        Assert.True(task.Execute());
        Assert.Single(task.OutputDlls);
        var compileInvocation = Assert.Single(
            ctx.Runner.Invocations,
            i => i.FileName.Contains("cl.exe") && i.Arguments.Contains("/c "));
        var linkInvocation = Assert.Single(
            ctx.Runner.Invocations,
            i => i.FileName.Contains("cl.exe") && i.Arguments.Contains("/LD "));
        Assert.Contains("/fsanitize=address", compileInvocation.Arguments);
        Assert.Contains("/Zi", compileInvocation.Arguments);
        Assert.Contains("/Fd:", compileInvocation.Arguments);
        Assert.Contains("/fsanitize=address", linkInvocation.Arguments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(";")]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void CompileCppToNativeTask_MsvcToolchain_SanitizersDisabledOrUnsupported_OmitsSanitizerFlags(
        string sanitizers)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cpp", "// source");

        var task = ctx.CreateCppTask(["kernel.cpp"], ctx.CreateMsvcToolchain());
        task.GSharpNativeSanitizers = sanitizers;

        Assert.True(task.Execute());
        var compileInvocation = Assert.Single(
            ctx.Runner.Invocations,
            i => i.FileName.Contains("cl.exe") && i.Arguments.Contains("/c "));
        var linkInvocation = Assert.Single(
            ctx.Runner.Invocations,
            i => i.FileName.Contains("cl.exe") && i.Arguments.Contains("/LD "));
        Assert.DoesNotContain("/fsanitize", compileInvocation.Arguments);
        Assert.DoesNotContain("/fsanitize", linkInvocation.Arguments);
    }

    [Theory]
    [InlineData("undefined", false)]
    [InlineData("address;undefined", true)]
    [InlineData("address,undefined", true)]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void CompileCppToNativeTask_MsvcToolchain_UndefinedSanitizer_LogsWarningAndContinues(
        string sanitizers,
        bool expectAddressFlag)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cpp", "// source");

        var task = ctx.CreateCppTask(["kernel.cpp"], ctx.CreateMsvcToolchain());
        task.GSharpNativeSanitizers = sanitizers;

        Assert.True(task.Execute());
        Assert.Single(task.OutputDlls);
        Assert.Empty(ctx.BuildEngine.LoggedErrors);
        Assert.Contains(
            ctx.BuildEngine.LoggedWarnings,
            w => w.Contains("undefined", StringComparison.OrdinalIgnoreCase) &&
                 w.Contains("MSVC", StringComparison.OrdinalIgnoreCase) &&
                 w.Contains("AddressSanitizer", StringComparison.OrdinalIgnoreCase));

        var compileInvocation = Assert.Single(
            ctx.Runner.Invocations,
            i => i.FileName.Contains("cl.exe") && i.Arguments.Contains("/c "));
        var linkInvocation = Assert.Single(
            ctx.Runner.Invocations,
            i => i.FileName.Contains("cl.exe") && i.Arguments.Contains("/LD "));
        if (expectAddressFlag)
        {
            Assert.Contains("/fsanitize=address", compileInvocation.Arguments);
            Assert.Contains("/Zi", compileInvocation.Arguments);
            Assert.Contains("/fsanitize=address", linkInvocation.Arguments);
        }
        else
        {
            Assert.DoesNotContain("/fsanitize", compileInvocation.Arguments);
            Assert.DoesNotContain("/fsanitize", linkInvocation.Arguments);
        }
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void CompileCppToNativeTask_MsvcToolchain_UndefinedSanitizerWithSeveralCompilations_LogsWarningOnce()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel1.cpp", "// source 1");
        ctx.CreateDummySource("kernel2.cpp", "// source 2");

        var task = ctx.CreateCppTask(["kernel1.cpp", "kernel2.cpp"], ctx.CreateMsvcToolchain());
        task.GSharpMultiTarget = true;
        task.GSharpNativeSanitizers = "address;undefined";

        Assert.True(task.Execute());
        // Two sources for x64 and arm64: four cl.exe compilations, one DLL each
        Assert.Equal(4, task.OutputDlls.Length);
        Assert.Single(
            ctx.BuildEngine.LoggedWarnings,
            w => w.Contains("undefined", StringComparison.OrdinalIgnoreCase) &&
                 w.Contains("MSVC", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("adress")]
    [InlineData("address;adress")]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("none")]
    [InlineData("thread")]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void CompileCppToNativeTask_UnknownSanitizerName_LogsErrorAndFails(string invalidSanitizers)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cpp", "// source");

        var toolchain = ctx.CreateGccClangToolchain("g++");
        var task = ctx.CreateCppTask(["kernel.cpp"], toolchain);
        task.GSharpNativeSanitizers = invalidSanitizers;

        Assert.False(task.Execute());
        Assert.Empty(task.OutputDlls);
        var error = Assert.Single(ctx.BuildEngine.LoggedErrors);
        Assert.Contains("address", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("undefined", error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ctx.Runner.Invocations, i => i.FileName == "g++" && i.Arguments.Contains("-shared"));
        Assert.DoesNotContain(
            ctx.Runner.Invocations,
            i => i.FileName == "g++" && i.Arguments.StartsWith("-c ", StringComparison.Ordinal));
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void CompileCppToNativeTask_MsvcToolchain_MultiUnitAddressSanitizer_UsesDistinctPerUnitPdb()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("Module.g.cpp", "// unit 0");
        ctx.CreateDummySource("Module.1.g.cpp", "// unit 1");
        ctx.CreateDummySource("Module.2.g.cpp", "// unit 2");

        var task = ctx.CreateCppTask(
            ["Module.g.cpp", "Module.1.g.cpp", "Module.2.g.cpp"],
            ctx.CreateMsvcToolchain());
        task.GSharpNativeSanitizers = "address";

        Assert.True(task.Execute());
        Assert.Single(task.OutputDlls);

        var compileInvocations = ctx.Runner.Invocations
            .Where(i => i.FileName.Contains("cl.exe") && i.Arguments.Contains("/c "))
            .ToArray();
        Assert.Equal(3, compileInvocations.Length);

        var pdbPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var invocation in compileInvocations)
        {
            Assert.Contains("/fsanitize=address", invocation.Arguments);
            Assert.Contains("/Zi", invocation.Arguments);
            var fdIndex = invocation.Arguments.IndexOf("/Fd:\"", StringComparison.Ordinal);
            Assert.True(fdIndex >= 0);
            var span = invocation.Arguments.AsSpan(fdIndex + 5);
            var endQuote = span.IndexOf('"');
            Assert.True(endQuote > 0);
            Assert.True(pdbPaths.Add(span.Slice(0, endQuote).ToString()));
        }

        Assert.Equal(3, pdbPaths.Count);
        var linkInvocation = Assert.Single(
            ctx.Runner.Invocations,
            i => i.FileName.Contains("cl.exe") && i.Arguments.Contains("/LD "));
        Assert.Contains("/fsanitize=address", linkInvocation.Arguments);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void CompileCppToNativeTask_MsvcToolchain_MultiTargetAddressSanitizer_IsolatesArchIntermediates()
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("Module.g.cpp", "// unit 0");
        ctx.CreateDummySource("Module.1.g.cpp", "// unit 1");

        var objRoot = Path.Combine(ctx.TempDir, "objs");
        var task = ctx.CreateCppTask(
            ["Module.g.cpp", "Module.1.g.cpp"],
            ctx.CreateMsvcToolchain(),
            objRoot);
        task.GSharpMultiTarget = true;
        task.GSharpNativeSanitizers = "address";

        Assert.True(task.Execute());
        Assert.Equal(2, task.OutputDlls.Length);
        Assert.True(File.Exists(Path.Combine(objRoot, "x64", "Module.obj")));
        Assert.True(File.Exists(Path.Combine(objRoot, "x64", "Module.1.obj")));
        Assert.True(File.Exists(Path.Combine(objRoot, "arm64", "Module.obj")));
        Assert.True(File.Exists(Path.Combine(objRoot, "arm64", "Module.1.obj")));
        Assert.True(File.Exists(Path.Combine(objRoot, "x64", "Module.vc.pdb")));
        Assert.True(File.Exists(Path.Combine(objRoot, "arm64", "Module.vc.pdb")));
        Assert.True(File.Exists(Path.Combine(objRoot, "x64", "Module.1.vc.pdb")));
        Assert.True(File.Exists(Path.Combine(objRoot, "arm64", "Module.1.vc.pdb")));
    }
}
