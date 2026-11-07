#nullable enable
using System.Diagnostics;
using Microsoft.Build.Framework;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Tasks;

[Category(TestCategory.Compiler)]
public class CompileTaskTestHarnessTests
{
    static CompileTaskTestHarnessTests()
    {
        MSBuildLocatorSetup.EnsureRegistered();
    }

    [Fact]
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
