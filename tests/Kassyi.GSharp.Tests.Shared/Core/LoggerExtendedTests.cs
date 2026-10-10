#nullable enable
using System;
using Kassyi.GSharp.Core;
using Xunit;
using MSLogLevel = Microsoft.Extensions.Logging.LogLevel;
using GSharpLogLevel = Kassyi.GSharp.Core.LogLevel;
using Microsoft.Extensions.Logging;

namespace Kassyi.GSharp.Tests.Shared.Core;

public class LoggerExtendedTests
{
    private class TestLogger : Logger
    {
        protected override string GetTag() => "TestLogger";

        public void ExposePrintAccumulatedInternal(string tag, string prefixMessage, GSharpLogLevel level = GSharpLogLevel.Debug)
        {
            PrintAccumulatedInternal(tag, prefixMessage, level);
        }
    }

    [Fact]
    public void ILogger_Methods_ExecuteCorrectly()
    {
        var logger = new TestLogger();
        
        Logger.ActiveLogLevel = GSharpLogLevel.Trace;
        Assert.True(logger.IsEnabled(MSLogLevel.Trace));
        Assert.True(logger.IsEnabled(MSLogLevel.Error));

        Logger.ActiveLogLevel = GSharpLogLevel.Error;
        Assert.False(logger.IsEnabled(MSLogLevel.Trace));

        var formatterCalled = false;
        Func<string, Exception?, string> formatter = (state, ex) =>
        {
            formatterCalled = true;
            return state;
        };

        // Suppress actual console output for the test
        Logger.WriteToConsole = false;

        // Level is Error, so Error should be logged
        logger.Log(MSLogLevel.Error, new EventId(1), "TestState", new Exception("TestEx"), formatter);
        Assert.True(formatterCalled);

        formatterCalled = false;
        // Level is Trace, which is < Error, so it shouldn't log
        logger.Log(MSLogLevel.Trace, new EventId(1), "TestState", null, formatter);
        Assert.False(formatterCalled);

        // BeginScope returns null
        Assert.Null(logger.BeginScope("test"));
    }

    [Fact]
    public void PrintAccumulatedInternal_ExecutesCorrectly()
    {
        var logger = new TestLogger();
        
        Logger.ClearAccumulated();
        Logger.ActiveLogLevel = GSharpLogLevel.Trace;
        Logger.WriteToConsole = false;

        // Empty state shouldn't throw or log
        logger.ExposePrintAccumulatedInternal("Tag", "Prefix");

        var timer = new Logger.PerformanceTimer();
        timer.Lap("TestMetric");

        // Should print accumulated
        logger.ExposePrintAccumulatedInternal("Tag", "Prefix");
        
        Logger.ClearAccumulated();
    }

    [Fact]
    public void MeasureScope_LogsStartAndEnd()
    {
        Logger.WriteToConsole = false;
        Logger.ActiveLogLevel = GSharpLogLevel.Trace;

        using (var scope = new Logger.MeasureScope("Tag", "TestScope", GSharpLogLevel.Info))
        {
            // Do nothing
        }

        // Dispose again shouldn't throw
        var scope2 = new Logger.MeasureScope("Tag", "TestScope2", GSharpLogLevel.Info);
        scope2.Dispose();
        scope2.Dispose();
    }

    [Fact]
    public void LoggerOfT_StaticMethods_ExecuteCorrectly()
    {
        Logger.WriteToConsole = false;
        Logger.ActiveLogLevel = GSharpLogLevel.Trace;

        Kassyi.GSharp.Core.Logger<LoggerExtendedTests>.Trace("TraceMsg");
        Kassyi.GSharp.Core.Logger<LoggerExtendedTests>.Verbose("VerboseMsg");
        Kassyi.GSharp.Core.Logger<LoggerExtendedTests>.Debug("DebugMsg");
        Kassyi.GSharp.Core.Logger<LoggerExtendedTests>.Info("InfoMsg");
        Kassyi.GSharp.Core.Logger<LoggerExtendedTests>.Warning("WarnMsg");
        Kassyi.GSharp.Core.Logger<LoggerExtendedTests>.Error("ErrorMsg");
        Kassyi.GSharp.Core.Logger<LoggerExtendedTests>.Error(new Exception("Test"), "ExceptionMsg");
    }
}
