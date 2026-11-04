#nullable enable
using Microsoft.Build.Locator;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Tasks;

[Collection(TestCollections.GlobalStateSequential)]
public class CompileTasksSanitizerTests
{
    static CompileTasksSanitizerTests()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            try
            {
                MSBuildLocator.RegisterDefaults();
            }
            catch (InvalidOperationException)
            {
                // Best-effort: ignore if already initialized or assemblies were pre-loaded
            }
        }
    }

    [Theory]
    [InlineData("g++", "address;undefined", "-fsanitize=address,undefined -fno-sanitize-recover=undefined -fno-omit-frame-pointer -g")]
    [InlineData("clang++", "address,undefined", "-fsanitize=address,undefined -fno-sanitize-recover=undefined -fno-omit-frame-pointer -g")]
    [InlineData("g++", "ADDRESS ; UNDEFINED", "-fsanitize=address,undefined -fno-sanitize-recover=undefined -fno-omit-frame-pointer -g")]
    [InlineData("g++", "address", "-fsanitize=address -fno-omit-frame-pointer -g")]
    [InlineData("clang++", "undefined", "-fsanitize=undefined -fno-sanitize-recover=undefined -fno-omit-frame-pointer -g")]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CompileCppToNativeTask_GccClangToolchain_SanitizersEnabled_PassesSanitizerFlags(
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
        var invocation = Assert.Single(ctx.Runner.Invocations, i => i.FileName == compiler && i.Arguments.Contains("-shared"));
        Assert.Contains(expectedFlags, invocation.Arguments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",")]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CompileCppToNativeTask_GccClangToolchain_SanitizersDisabled_OmitsSanitizerFlags(string sanitizers)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cpp", "// source");

        var toolchain = ctx.CreateGccClangToolchain("g++");
        var task = ctx.CreateCppTask(["kernel.cpp"], toolchain);
        task.GSharpNativeSanitizers = sanitizers;

        Assert.True(task.Execute());
        var invocation = Assert.Single(ctx.Runner.Invocations, i => i.FileName == "g++" && i.Arguments.Contains("-shared"));
        Assert.DoesNotContain("-fsanitize", invocation.Arguments);
    }

    [Theory]
    [InlineData("address")]
    [InlineData("address;undefined")]
    [InlineData("address,undefined")]
    [InlineData("ADDRESS")]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CompileCppToNativeTask_MsvcToolchain_SanitizersEnabled_PassesAddressSanitizerFlags(string sanitizers)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cpp", "// source");

        var task = ctx.CreateCppTask(["kernel.cpp"], ctx.CreateMsvcToolchain());
        task.GSharpNativeSanitizers = sanitizers;

        Assert.True(task.Execute());
        Assert.Single(task.OutputDlls);
        var invocation = Assert.Single(ctx.Runner.Invocations, i => i.FileName.Contains("cl.exe"));
        Assert.Contains("/fsanitize=address", invocation.Arguments);
        Assert.Contains("/Zi", invocation.Arguments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(";")]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CompileCppToNativeTask_MsvcToolchain_SanitizersDisabledOrUnsupported_OmitsSanitizerFlags(string sanitizers)
    {
        using var ctx = new CompileTaskTestContext();
        ctx.CreateDummySource("kernel.cpp", "// source");

        var task = ctx.CreateCppTask(["kernel.cpp"], ctx.CreateMsvcToolchain());
        task.GSharpNativeSanitizers = sanitizers;

        Assert.True(task.Execute());
        var invocation = Assert.Single(ctx.Runner.Invocations, i => i.FileName.Contains("cl.exe"));
        Assert.DoesNotContain("/fsanitize", invocation.Arguments);
    }

    [Theory]
    [InlineData("undefined", false)]
    [InlineData("address;undefined", true)]
    [InlineData("address,undefined", true)]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CompileCppToNativeTask_MsvcToolchain_UndefinedSanitizer_LogsWarningAndContinues(
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

        var invocation = Assert.Single(ctx.Runner.Invocations, i => i.FileName.Contains("cl.exe"));
        if (expectAddressFlag)
        {
            Assert.Contains("/fsanitize=address", invocation.Arguments);
            Assert.Contains("/Zi", invocation.Arguments);
        }
        else
        {
            Assert.DoesNotContain("/fsanitize", invocation.Arguments);
        }
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CompileCppToNativeTask_MsvcToolchain_UndefinedSanitizerWithSeveralCompilations_LogsWarningOnce()
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
    public void Test_CompileCppToNativeTask_UnknownSanitizerName_LogsErrorAndFails(string invalidSanitizers)
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
    }
}
