#nullable enable
using System;
using Kassyi.GSharp.Core;
using Xunit;
using MSLogLevel = Microsoft.Extensions.Logging.LogLevel;
using GSharpLogLevel = Kassyi.GSharp.Core.LogLevel;
using GLogger = Kassyi.GSharp.Core.Logger<Kassyi.GSharp.Tests.Shared.Core.LoggerExtendedTests>;
using Microsoft.Extensions.Logging;

namespace Kassyi.GSharp.Tests.Shared.Core;

[Collection(TestCollections.GlobalStateSequential)]
public class LoggerExtendedTests
{
    private class TestLogger : Logger
    {
        protected override string GetTag() => "TestLogger";

        public void ExposePrintAccumulatedInternal(string tag, string prefixMessage,
            GSharpLogLevel level = GSharpLogLevel.Debug)
        {
            PrintAccumulatedInternal(tag, prefixMessage, level);
        }
    }

    [Fact]
    public void ILogger_Methods_ExecuteCorrectly()
    {
        var prevLevel = Logger.ActiveLogLevel;
        var prevConsole = Logger.WriteToConsole;
        try
        {
            var logger = new TestLogger();

            Logger.ActiveLogLevel = GSharpLogLevel.Trace;
            Assert.True(logger.IsEnabled(MSLogLevel.Trace));
            Assert.True(logger.IsEnabled(MSLogLevel.Error));

            Logger.ActiveLogLevel = GSharpLogLevel.Error;
            Assert.False(logger.IsEnabled(MSLogLevel.Trace));

            var formatterCalled = false;
            Func<string, Exception?, string> formatter = (state, _) =>
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
        finally
        {
            Logger.ActiveLogLevel = prevLevel;
            Logger.WriteToConsole = prevConsole;
        }
    }

    [Fact]
    public void PrintAccumulatedInternal_ExecutesCorrectly()
    {
        var prevLevel = Logger.ActiveLogLevel;
        var prevConsole = Logger.WriteToConsole;
        var prevOut = Console.Out;
        using var sw = new StringWriter();
        try
        {
            var logger = new TestLogger();

            Logger.ClearAccumulated();
            Logger.ActiveLogLevel = GSharpLogLevel.Trace;
            Logger.WriteToConsole = true;
            Console.SetOut(sw);

            // Empty state shouldn't throw or log
            logger.ExposePrintAccumulatedInternal("Tag", "Prefix");
            Assert.Equal("", sw.ToString());

            var timer = new Logger.PerformanceTimer();
            timer.Lap("TestMetric");

            // Should print accumulated
            logger.ExposePrintAccumulatedInternal("Tag", "Prefix");
            Assert.Contains("Prefix [TestMetric:", sw.ToString());
        }
        finally
        {
            Console.SetOut(prevOut);
            Logger.ClearAccumulated();
            Logger.ActiveLogLevel = prevLevel;
            Logger.WriteToConsole = prevConsole;
        }
    }

    [Fact]
    public void MeasureScope_LogsStartAndEnd()
    {
        var prevLevel = Logger.ActiveLogLevel;
        var prevConsole = Logger.WriteToConsole;
        var prevOut = Console.Out;
        using var sw = new StringWriter();
        try
        {
            Logger.WriteToConsole = true;
            Logger.ActiveLogLevel = GSharpLogLevel.Trace;
            Console.SetOut(sw);

            using (var scope = new Logger.MeasureScope("Tag", "TestScope", GSharpLogLevel.Info))
            {
                // Do nothing
            }

            // Dispose again shouldn't throw or log a second End Scope
            var scope2 = new Logger.MeasureScope("Tag", "TestScope2", GSharpLogLevel.Info);
            scope2.Dispose();
            scope2.Dispose();

            var output = sw.ToString();
            Assert.Contains("[Start Scope] TestScope", output);
            Assert.Contains("[End Scope] TestScope", output);
            Assert.Contains("[Start Scope] TestScope2", output);
            Assert.Contains("[End Scope] TestScope2", output);
        }
        finally
        {
            Console.SetOut(prevOut);
            Logger.ActiveLogLevel = prevLevel;
            Logger.WriteToConsole = prevConsole;
        }
    }

    [Fact]
    public void LoggerOfT_StaticMethods_ExecuteCorrectly()
    {
        var prevLevel = Logger.ActiveLogLevel;
        var prevConsole = Logger.WriteToConsole;
        var prevOut = Console.Out;
        var prevErr = Console.Error;
        using var swOut = new StringWriter();
        using var swErr = new StringWriter();
        try
        {
            Logger.WriteToConsole = true;
            Logger.ActiveLogLevel = GSharpLogLevel.Trace;
            Console.SetOut(swOut);
            Console.SetError(swErr);

            GLogger.Trace("TraceMsg");
            GLogger.Verbose("VerboseMsg");
            GLogger.Debug("DebugMsg");
            GLogger.Info("InfoMsg");
            GLogger.Warning("WarnMsg");
            GLogger.Error("ErrorMsg");
            GLogger.Error(new Exception("Test"), "ExceptionMsg");

            var stdOut = swOut.ToString();
            var stdErr = swErr.ToString();
            Assert.Contains("TraceMsg", stdOut);
            Assert.Contains("VerboseMsg", stdOut);
            Assert.Contains("DebugMsg", stdOut);
            Assert.Contains("InfoMsg", stdOut);
            Assert.Contains("WarnMsg", stdOut);
            Assert.Contains("ErrorMsg", stdErr);
            Assert.Contains("ExceptionMsg", stdErr);
        }
        finally
        {
            Console.SetOut(prevOut);
            Console.SetError(prevErr);
            Logger.ActiveLogLevel = prevLevel;
            Logger.WriteToConsole = prevConsole;
        }
    }
}

