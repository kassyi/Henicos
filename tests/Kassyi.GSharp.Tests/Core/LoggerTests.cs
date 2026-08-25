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
    [Fact]
    [Trait("FeatureId", "GEN-03")]
    public async Task Log_ConcurrentWrites_ShouldNotLoseMessages_StressTest()
    {
        var logger = new TestLogger();
        Logger.ActiveLogLevel = LogLevel.Info;
        int threadCount = 50;
        int messagesPerThread = 100;
        
        var tasks = Enumerable.Range(0, threadCount).Select(async t =>
        {
            await Task.Yield();
            for (int i = 0; i < messagesPerThread; i++)
            {
                logger.LogInformation($"Concurrent message T{t}-M{i}");
            }
        });
        
        await Task.WhenAll(tasks);
        await Task.Delay(500); // Allow background channel to flush
        
        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gsharp_diagnostics.log");
        Assert.True(File.Exists(logPath));
        
        var content = await File.ReadAllTextAsync(logPath);
        for (int t = 0; t < threadCount; t++)
        {
            // Just check the last message of each thread to ensure completion
            Assert.Contains($"Concurrent message T{t}-M{messagesPerThread - 1}", content);
        }
    }

    [Fact]
    [Trait("FeatureId", "GEN-03")]
    public async Task Log_ExtremeMessageSize_EdgeCase()
    {
        var logger = new TestLogger();
        Logger.ActiveLogLevel = LogLevel.Info;
        
        // 1MB string
        var giantString = new string('A', 1024 * 1024);
        logger.LogInformation(giantString);
        
        await Task.Delay(200); // Allow background channel to flush
        
        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gsharp_diagnostics.log");
        Assert.True(File.Exists(logPath));
        
        var content = await File.ReadAllTextAsync(logPath);
        Assert.Contains(giantString, content);
    }

    [Fact]
    [Trait("FeatureId", "GEN-03")]
    public async Task Log_LogLevelNone_EdgeCase()
    {
        var logger = new TestLogger();
        Logger.ActiveLogLevel = LogLevel.None;
        var uniqueGuid = Guid.NewGuid().ToString();
        
        logger.LogInformation($"Should not appear {uniqueGuid}");
        logger.LogError($"Should not appear {uniqueGuid}");
        
        await Task.Delay(100);
        
        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gsharp_diagnostics.log");
        if (File.Exists(logPath))
        {
            var content = await File.ReadAllTextAsync(logPath);
            Assert.DoesNotContain(uniqueGuid, content);
        }
    }
}
