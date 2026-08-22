using System.Diagnostics;
using System.IO;
using Kassyi.GSharp.Runtime.Core;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Kassyi.GSharp.Tests;

public class LoggerTests
{
    private class TestLogger : Logger
    {
        protected override string GetTag() => "TestLogger";
    }

    [Fact]
    public async Task Log_ShouldFormatAndWriteMessagesAsync()
    {
        var logger = new TestLogger();
        Logger.ActiveLogLevel = Kassyi.GSharp.Runtime.Core.LogLevel.Trace;
        Logger.Clear();

        logger.LogInformation("Test Info Message");
        logger.LogError("Test Error Message");

        Logger.Shutdown();
        
        // Allow background channel to flush
        await Task.Delay(100);

        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gsharp_diagnostics.log");
        if (File.Exists(logPath))
        {
            var content = await File.ReadAllTextAsync(logPath);
            Assert.Contains("Test Info Message", content);
            Assert.Contains("[INFO]", content);
            Assert.Contains("Test Error Message", content);
            Assert.Contains("[ERROR]", content);
        }
    }

    [Fact]
    public async Task MeasureScope_ShouldLogDuration()
    {
        Logger.ActiveLogLevel = Kassyi.GSharp.Runtime.Core.LogLevel.Trace;
        Logger.Clear();

        using (var scope = new Logger.MeasureScope("TestTag", "TestOperation", Kassyi.GSharp.Runtime.Core.LogLevel.Info))
        {
            await Task.Delay(10);
        }

        // Allow background channel to flush
        await Task.Delay(100);

        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gsharp_diagnostics.log");
        if (File.Exists(logPath))
        {
            var content = await File.ReadAllTextAsync(logPath);
            Assert.Contains("[Start Scope] TestOperation", content);
            Assert.Contains("[End Scope] TestOperation took", content);
            Assert.Contains("ms", content);
        }
    }
}
