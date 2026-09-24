using Microsoft.Extensions.Logging;
using Logger = Kassyi.GSharp.Core.Logger;
using LogLevel = Kassyi.GSharp.Core.LogLevel;

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

    public static async Task<string> WaitForLogContentAsync(string logPath, Func<string, bool> predicate,
        int timeoutMs = 3000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long lastPosition = 0;
        var fullContent = new System.Text.StringBuilder();

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (File.Exists(logPath))
            {
                using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length > lastPosition)
                {
                    fs.Position = lastPosition;
                    using var reader = new StreamReader(fs);
                    var newContent = await reader.ReadToEndAsync();
                    fullContent.Append(newContent);
                    lastPosition = fs.Position;

                    if (predicate(fullContent.ToString()))
                    {
                        return fullContent.ToString();
                    }
                }
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Log content did not satisfy predicate within {timeoutMs}ms");
    }
}

[CollectionDefinition("LoggerCollection")]
public class LoggerCollectionDefinition : ICollectionFixture<LoggerFixture>;

[Collection("LoggerCollection")]
public class LoggerTests
    : TranspilerTestBase
{
    private class TestLogger : Logger
    {
        protected override string GetTag() => "TestLogger";
    }

    [SkippableFact]
    [Trait("FeatureId", "GEN-03")]
    public async Task Log_ShouldFormatAndWriteMessagesAsync()
    {
        var logger = new TestLogger();
        Logger.ActiveLogLevel = LogLevel.Trace;
        logger.LogInformation("Test Info Message");
        logger.LogError("Test Error Message");

        var marker = $"Marker-{Guid.NewGuid()}";
        logger.LogInformation(marker);

        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gsharp_diagnostics.log");
        var content = await LoggerFixture.WaitForLogContentAsync(logPath, c => c.Contains(marker));

        Assert.Contains("Test Info Message", content);
        Assert.Contains("[INFO]", content);
        Assert.Contains("Test Error Message", content);
        Assert.Contains("[ERROR]", content);
    }

    [SkippableFact]
    [Trait("Category", "Stress")]
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

        var marker = $"Marker-{Guid.NewGuid()}";
        logger.LogInformation(marker);

        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gsharp_diagnostics.log");
        var content = await LoggerFixture.WaitForLogContentAsync(logPath, c => c.Contains(marker), 30000);

        var messages = new HashSet<string>(
            content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                {
                    var lastBracket = line.LastIndexOf(']');
                    return lastBracket >= 0 ? line[(lastBracket + 1)..].Trim() : line.Trim();
                }));

        for (int t = 0; t < threadCount; t++)
        {
            for (int i = 0; i < messagesPerThread; i++)
            {
                var expected = $"Concurrent message T{t}-M{i}";
                Assert.Contains(expected, messages);
            }
        }
    }

    [SkippableFact]
    [Trait("FeatureId", "GEN-03")]
    public async Task Log_ExtremeMessageSize_EdgeCase()
    {
        var logger = new TestLogger();
        Logger.ActiveLogLevel = LogLevel.Info;

        // 1MB string
        var giantString = new string('A', 1024 * 1024);
        logger.LogInformation(giantString);

        var marker = $"Marker-{Guid.NewGuid()}";
        logger.LogInformation(marker);

        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gsharp_diagnostics.log");
        var content = await LoggerFixture.WaitForLogContentAsync(logPath, c => c.Contains(marker), 30000);

        Assert.Contains(giantString, content);
    }

    [SkippableFact]
    [Trait("FeatureId", "GEN-03")]
    public async Task Log_LogLevelNone_EdgeCase()
    {
        var logger = new TestLogger();
        Logger.ActiveLogLevel = LogLevel.None;
        var uniqueGuid = Guid.NewGuid().ToString();

        logger.LogInformation($"Should not appear {uniqueGuid}");
        logger.LogError($"Should not appear {uniqueGuid}");

        Logger.ActiveLogLevel = LogLevel.Info;
        var marker = $"Marker-{Guid.NewGuid()}";
        logger.LogInformation(marker);

        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gsharp_diagnostics.log");
        var content = await LoggerFixture.WaitForLogContentAsync(logPath, c => c.Contains(marker));
        Assert.DoesNotContain(uniqueGuid, content);
    }

    [SkippableFact]
    [Trait("FeatureId", "GEN-04")]
    public async Task MeasureScope_ShouldLogDuration()
    {
        Logger.ActiveLogLevel = LogLevel.Trace;

        using (var scope = new Logger.MeasureScope("TestTag", "TestOperation", LogLevel.Info))
        {
            await Task.Delay(10);
        }

        var marker = $"Marker-{Guid.NewGuid()}";
        // Cannot use logger because we need it to use the static Log internally maybe?
        // Wait, we can just use the static Logger method indirectly or create a new logger.
        var logger = new TestLogger();
        logger.LogInformation(marker);

        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gsharp_diagnostics.log");
        var content = await LoggerFixture.WaitForLogContentAsync(logPath, c => c.Contains(marker));

        Assert.Contains("[Start Scope] TestOperation", content);
        Assert.Contains("[End Scope] TestOperation took", content);
        Assert.Contains("ms", content);
    }
}
