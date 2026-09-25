#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Tasks.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Tasks;

public class ProcessRunnerTests
{
    [Fact]
    [Trait("FeatureId", "GEN-55")]
    public async Task Test_ProcessRunner_Echo_Success()
    {
        var runner = ProcessRunner.Default;
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c echo Hello World"
        };

        var result = await runner.RunAsync(startInfo);
        
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Hello World", result.StandardOutput);
        Assert.Empty(result.StandardError.Trim());
    }

    [Fact]
    [Trait("FeatureId", "GEN-55")]
    [Trait("Category", "SadPath")]
    public async Task Test_ProcessRunner_FileNotFound_ThrowsException()
    {
        var runner = ProcessRunner.Default;
        var startInfo = new ProcessStartInfo
        {
            FileName = "this_executable_does_not_exist.exe"
        };

        await Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(() => runner.RunAsync(startInfo));
    }

    [Fact]
    [Trait("FeatureId", "GEN-55")]
    [Trait("Category", "Stress")]
    public void Test_ProcessRunner_LargeOutput_NoDeadlock()
    {
        // Generate a large output to fill the buffer (usually 4KB on Windows).
        // If we read synchronously, this would deadlock.
        var runner = ProcessRunner.Default;
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c \"for /L %i in (1,1,1000) do @echo Output line %i\""
        };

        // Run synchronously to prove GetAwaiter().GetResult() doesn't deadlock inside.
        var result = runner.Run(startInfo);
        
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Output line 1000", result.StandardOutput);
    }
}
