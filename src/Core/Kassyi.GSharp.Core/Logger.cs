using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MSLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Kassyi.GSharp.Core;

/// <summary>
/// Specifies the severity and verbosity levels for diagnostic and performance logs.
/// </summary>
// ReSharper disable once UnusedMember.Global -- Public API exposed for library consumers
public enum LogLevel
{
    /// <summary>Detailed diagnostic and tracing information.</summary>
    Trace = 0,

    /// <summary>Verbose execution flow details.</summary>
    Verbose = 1,

    /// <summary>Debug information useful during active development.</summary>
    Debug = 2,

    /// <summary>General informational messages.</summary>
    Info = 3,

    /// <summary>Warning messages indicating non-fatal issues or unexpected conditions.</summary>
    Warning = 4,

    /// <summary>Error messages indicating failures or unhandled exceptions.</summary>
    Error = 5,

    /// <summary>Disables all logging output.</summary>
    None = 6
}

/// <summary>
/// Provides logging and performance interval tracking.
/// </summary>
/// <remarks>
/// <para>Log messages are written synchronously to the standard output, delegating actual I/O responsibility to the console host.</para>
/// </remarks>
// ReSharper disable once UnusedMember.Global -- Public API exposed for library consumers
// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
public abstract class Logger : ILogger
{
    private static readonly ConcurrentDictionary<string, long> s_accumulatedTimes = new();

    /// <summary>
    /// Gets or sets the minimum active log level for message processing.
    /// </summary>
    public static LogLevel ActiveLogLevel
    {
        get; set;
    } =
        LogLevel.Trace; // Trace by default to output everything during debugging

    /// <summary>
    /// Gets or sets a value indicating whether log entries are written to standard console output.
    /// </summary>
    public static bool WriteToConsole { get; set; } = true;

    /// <summary>
    /// Writes a log entry.
    /// </summary>
    /// <typeparam name="TState">The type of the object to be written.</typeparam>
    /// <param name="logLevel">Entry will be written on this level.</param>
    /// <param name="eventId">Id of the event.</param>
    /// <param name="state">The entry to be written. Can be also an object.</param>
    /// <param name="exception">The exception related to this entry.</param>
    /// <param name="formatter">Function to create a string message of the state and exception.</param>
    public void Log<TState>(
        MSLogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        var customLevel = MapLogLevel(logLevel);

        Log(
            customLevel,
            GetTag(),
            exception != null
                ? $"{message}\nException: {exception.GetType().FullName} - {exception.Message}\nStack Trace: {exception.StackTrace}"
                : message
        );
    }

    /// <summary>
    /// Checks if the given <paramref name="logLevel"/> is enabled.
    /// </summary>
    /// <param name="logLevel">level to be checked.</param>
    /// <returns><c>true</c> if enabled; otherwise, <c>false</c>.</returns>
    public bool IsEnabled(MSLogLevel logLevel) => MapLogLevel(logLevel) >= ActiveLogLevel;

    /// <summary>
    /// Begins a logical operation scope.
    /// </summary>
    /// <typeparam name="TState">The type of the state to begin scope for.</typeparam>
    /// <param name="state">The identifier for the scope.</param>
    /// <returns>An <see cref="IDisposable"/> that ends the logical operation scope on dispose, or <see langword="null"/> if scopes are not supported.</returns>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull =>
        null;


    /// <summary>
    /// Queues a formatted log message to the background logging channel and writes to console if enabled.
    /// </summary>
    /// <param name="level">The severity level of the log.</param>
    /// <param name="tag">The component tag identifying the log source.</param>
    /// <param name="message">The message to log. If <see langword="null"/>, the operation is ignored.</param>
    public static void Log(LogLevel level, string tag, string? message)
    {
        if (level < ActiveLogLevel)
        {
            return;
        }

        if (message == null)
        {
            return;
        }

        var timestamp = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}]";
        var levelStr = level.ToString().ToUpper();

        var logLine = $"{timestamp} [{levelStr}] [{tag}] {message}";

        if (WriteToConsole)
        {
            if (level >= LogLevel.Error)
            {
                Console.Error.WriteLine(logLine);
            }
            else
            {
                Console.WriteLine(logLine);
            }
        }

        // Debug output for VS Code output window
        System.Diagnostics.Debug.WriteLine(logLine);
    }

    /// <summary>Logs a trace message with a specified tag.</summary>
    /// <param name="tag">A category or component identifier for the log entry.</param>
    /// <param name="message">The message text to log.</param>
    public static void Trace(string tag, string message) => Log(LogLevel.Trace, tag, message);

    /// <summary>Logs a verbose message with a specified tag.</summary>
    /// <param name="tag">A category or component identifier for the log entry.</param>
    /// <param name="message">The message text to log.</param>
    public static void Verbose(string tag, string message) => Log(LogLevel.Verbose, tag, message);

    /// <summary>Logs a debug message with a specified tag.</summary>
    /// <param name="tag">A category or component identifier for the log entry.</param>
    /// <param name="message">The message text to log.</param>
    public static void Debug(string tag, string message) => Log(LogLevel.Debug, tag, message);

    /// <summary>Logs an informational message with a specified tag.</summary>
    /// <param name="tag">A category or component identifier for the log entry.</param>
    /// <param name="message">The message text to log.</param>
    public static void Info(string tag, string message) => Log(LogLevel.Info, tag, message);

    /// <summary>Logs a warning message with a specified tag.</summary>
    /// <param name="tag">A category or component identifier for the log entry.</param>
    /// <param name="message">The message text to log.</param>
    public static void Warning(string tag, string message) => Log(LogLevel.Warning, tag, message);

    /// <summary>Logs an error message with a specified tag.</summary>
    /// <param name="tag">A category or component identifier for the log entry.</param>
    /// <param name="message">The message text to log.</param>
    public static void Error(string tag, string message) => Log(LogLevel.Error, tag, message);

    /// <summary>Logs an exception and associated error message with a specified tag.</summary>
    /// <param name="tag">A category or component identifier for the log entry.</param>
    /// <param name="ex">The exception to format and record.</param>
    /// <param name="message">An explanatory message accompanying the exception.</param>
    public static void Error(string tag, Exception ex, string message) =>
        Log(LogLevel.Error, tag,
            $"{message}\nException: {ex.GetType().FullName} - {ex.Message}\nStack Trace: {ex.StackTrace}");


    /// <summary>
    /// Accumulates the elapsed time for a given performance metric key.
    /// </summary>
    /// <param name="key">The performance metric identifier.</param>
    /// <param name="elapsedMilliseconds">The elapsed time to add, in milliseconds.</param>
    protected static void AddInterval(string key, long elapsedMilliseconds) =>
        s_accumulatedTimes.AddOrUpdate(key, elapsedMilliseconds, (_, current) => current + elapsedMilliseconds);

    /// <summary>
    /// Queues a log message containing all accumulated performance intervals.
    /// </summary>
    /// <param name="tag">The component tag identifying the log source.</param>
    /// <param name="prefixMessage">The message to prefix before the interval summary.</param>
    /// <param name="level">The severity level to log at.</param>
    protected static void PrintAccumulatedInternal(string tag, string prefixMessage, LogLevel level = LogLevel.Debug)
    {
        if (level < ActiveLogLevel || s_accumulatedTimes.IsEmpty)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.Append(prefixMessage).Append(" [");
        var first = true;
        foreach (var kv in s_accumulatedTimes)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append(kv.Key).Append(": ").Append(kv.Value).Append(" ms");
        }

        sb.Append(']');

        Log(
            level,
            tag,
            sb.ToString()
        );
    }

    /// <summary>
    /// Clears all accumulated performance intervals.
    /// </summary>
    public static void ClearAccumulated() => s_accumulatedTimes.Clear();

    // ============================================================
    // Microsoft.Extensions.Logging.ILogger Implementation
    // ============================================================

    /// <summary>
    /// Gets the component tag used to identify the logger's source.
    /// </summary>
    /// <returns>A string representing the log tag.</returns>
    protected abstract string GetTag();

    private static LogLevel MapLogLevel(MSLogLevel msLevel)
    {
        return msLevel switch
        {
            MSLogLevel.Trace => LogLevel.Trace,
            MSLogLevel.Debug => LogLevel.Debug,
            MSLogLevel.Information => LogLevel.Info,
            MSLogLevel.Warning => LogLevel.Warning,
            MSLogLevel.Error => LogLevel.Error,
            MSLogLevel.Critical => LogLevel.Error,
            _ => LogLevel.None
        };
    }

    /// <summary>
    /// Utility for tracking and recording elapsed intervals across named pipeline stages.
    /// </summary>
    // ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
    // ReSharper disable once UnusedMember.Global -- Public API exposed for library consumers
    public class PerformanceTimer
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();

        /// <summary>
        /// Records the elapsed milliseconds under the specified metric key and restarts the stopwatch.
        /// </summary>
        /// <param name="key">The metric key identifying the measured segment.</param>
        /// <returns>The elapsed milliseconds since the last lap or initialization.</returns>
        public long Lap(string key)
        {
            var elapsed = _sw.ElapsedMilliseconds;
            AddInterval(key, elapsed);
            _sw.Restart();
            return elapsed;
        }
    }

    /// <summary>
    /// A disposable scope that automatically logs the duration of a code block upon disposal.
    /// </summary>
    public class MeasureScope : IDisposable
    {
        private readonly LogLevel _level;
        private readonly Stopwatch _sw;

        private readonly string _tag,
            _scopeName;

        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="MeasureScope"/> class and logs scope start.
        /// </summary>
        /// <param name="tag">The component or category tag.</param>
        /// <param name="scopeName">The name of the measured operation.</param>
        /// <param name="level">The logging level to emit.</param>
        public MeasureScope(string tag, string scopeName, LogLevel level)
        {
            (_tag, _scopeName, _level, _sw) = (tag, scopeName, level, Stopwatch.StartNew());
            Log(_level, _tag, $"[Start Scope] {_scopeName}");
        }

        /// <summary>
        /// Stops the stopwatch and writes the elapsed scope execution duration to the log.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _sw.Stop();
            Log(_level, _tag, $"[End Scope] {_scopeName} took {_sw.ElapsedMilliseconds} ms");
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}

/// <summary>
/// Provides strongly-typed static logging helpers tagged with the generic type name.
/// </summary>
/// <typeparam name="T">The type used to identify log message sources.</typeparam>
// ReSharper disable once UnusedMember.Global -- Public API exposed for library consumers
// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
public class Logger<T> : Logger
{
    /// <inheritdoc />
    protected override string GetTag() => typeof(T).Name;

    /// <summary>Logs a trace message.</summary>
    /// <param name="message">The message text to log.</param>
    public static void Trace(string message) => Log(LogLevel.Trace, typeof(T).Name, message);

    /// <summary>Logs a verbose message.</summary>
    /// <param name="message">The message text to log.</param>
    public static void Verbose(string message) => Log(LogLevel.Verbose, typeof(T).Name, message);

    /// <summary>Logs a debug message.</summary>
    /// <param name="message">The message text to log.</param>
    public static void Debug(string message) => Log(LogLevel.Debug, typeof(T).Name, message);

    /// <summary>Logs an informational message.</summary>
    /// <param name="message">The message text to log.</param>
    public static void Info(string message) => Log(LogLevel.Info, typeof(T).Name, message);

    /// <summary>Logs a warning message.</summary>
    /// <param name="message">The message text to log.</param>
    public static void Warning(string message) => Log(LogLevel.Warning, typeof(T).Name, message);

    /// <summary>Logs an error message.</summary>
    /// <param name="message">The message text to log.</param>
    public static void Error(string message) => Log(LogLevel.Error, typeof(T).Name, message);

    /// <summary>Logs an exception and associated error message.</summary>
    /// <param name="ex">The exception to format and record.</param>
    /// <param name="message">An explanatory message accompanying the exception.</param>
    public static void Error(Exception ex, string message) => Log(LogLevel.Error, typeof(T).Name, $"{message}\n{ex}");
}

