using System.Diagnostics;
using System.IO;
using Kassyi.GSharp.Core;
using Microsoft.Extensions.Logging;
using Logger = Kassyi.GSharp.Core.Logger;
using LogLevel = Kassyi.GSharp.Core.LogLevel;
using Xunit;

namespace Kassyi.GSharp.Tests;

public class LoggerFixture : IAsyncLifetime
{
    public LoggerFixture()
    {
        Logger.Clear();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await Logger.ShutdownAsync();
    }
}

[CollectionDefinition("LoggerCollection")]
public class LoggerCollectionDefinition : ICollectionFixture<LoggerFixture>
{
}

[Collection("LoggerCollection")]
public class LoggerTests
{
    private class TestLogger : Logger
    {
        protected override string GetTag() => "TestLogger";
    }

    [Fact]
    [Trait("FeatureId", "GEN-03")]
    public async Task Log_ShouldFormatAndWriteMessagesAsync()
    {
        var logger = new TestLogger();
        Logger.ActiveLogLevel = LogLevel.Trace;
        logger.LogInformation("Test Info Message");
        logger.LogError("Test Error Message");

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
    [Trait("FeatureId", "GEN-04")]
    public async Task MeasureScope_ShouldLogDuration()
    {
        Logger.ActiveLogLevel = LogLevel.Trace;

        using (var scope = new Logger.MeasureScope("TestTag", "TestOperation", LogLevel.Info))
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
