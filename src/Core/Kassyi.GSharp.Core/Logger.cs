using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
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
/// Provides high-throughput, non-blocking asynchronous logging and performance interval tracking.
/// </summary>
/// <remarks>
/// <para>Log messages are queued into a bounded lock-free channel and processed in a dedicated background task to eliminate I/O blocking in performance-critical execution paths.</para>
/// </remarks>
// ReSharper disable once UnusedMember.Global -- Public API exposed for library consumers
// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
public abstract class Logger : ILogger
{
    private const int MaxRetries = 3;
    private const int BufferCapacity = 1000000;
    private const int RetryDelayMs = 50;
    private const string ClearCommand = "__CLEAR__";

    private static readonly string s_logPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "gsharp_diagnostics.log"
    );

    private static readonly BoundedChannelOptions s_channelOptions = new(BufferCapacity)
    {
        FullMode = BoundedChannelFullMode.DropOldest
    };

    private static readonly Channel<string> s_logChannel = Channel.CreateBounded<string>(s_channelOptions);

    private static readonly Task s_loggingTask;

    private static readonly ConcurrentDictionary<string, long> s_accumulatedTimes = new();

    static Logger()
    {
        // バックグラウンドでログ書き込みタスクを開始
        s_loggingTask = Task.Run(ProcessLogQueueAsync);
    }

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

    private static async Task ProcessLogQueueAsync()
    {
        // リアロケーションの O(N) を排除するため、Channelの最大容量で初期化
        var buffer = new List<string>(BufferCapacity);

        while (await s_logChannel.Reader.WaitToReadAsync().ConfigureAwait(false))
        {
            try
            {
                // I/O回数のオーダーを O(N/100) -> O(1) に削減するため全量を吸い上げる
                while (s_logChannel.Reader.TryRead(out var msg))
                {
                    if (msg == ClearCommand)
                    {
                        if (File.Exists(s_logPath))
                        {
                            File.Delete(s_logPath);
                        }

                        buffer.Clear();
                        continue;
                    }

                    buffer.Add(msg);
                }

                if (buffer.Count == 0)
                {
                    continue;
                }

                var retryCount = 0;
                while (retryCount < MaxRetries)
                {
                    try
                    {
                        await File.AppendAllLinesAsync(s_logPath, buffer).ConfigureAwait(false);
                        break;
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        if (retryCount >= MaxRetries)
                        {
                            // 非同期awaitの O(N) 回のコンテキストスイッチを排除し、O(1) 回で出力
                            var errBody = string.Join(Environment.NewLine, buffer);
                            var errLine =
                                $"[LOGGER_FALLBACK_ERROR] Failed to write logs (Reason: {ex.Message}){Environment.NewLine}{errBody}";
                            await Console.Error.WriteLineAsync(errLine).ConfigureAwait(false);
                            System.Diagnostics.Debug.WriteLine(errLine);
                        }
                        else
                        {
                            await Task.Delay(RetryDelayMs * retryCount).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LOGGER_FATAL] Background logging task iteration failed: {ex}");
            }
            finally
            {
                buffer.Clear();
            }
        }
    }

    /// <summary>
    /// Asynchronously flushes the log queue and shuts down the background logging task.
    /// </summary>
    /// <returns>A task that represents the asynchronous shutdown operation.</returns>
    public static async Task ShutdownAsync()
    {
        s_logChannel.Writer.Complete();
        try
        {
            await s_loggingTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch
        {
            // Ignore timeout or task cancellation errors on shutdown
        }
    }

    /// <summary>
    /// Synchronously flushes the log queue and waits for pending writes to complete.
    /// </summary>
    /// <param name="timeoutMs">Maximum milliseconds to wait for the flush.</param>
    public static void Flush(int timeoutMs = 3000)
    {
        try
        {
            ShutdownAsync().Wait(TimeSpan.FromMilliseconds(timeoutMs));
        }
        catch
        {
            // Ignore timeout on flush
        }
    }

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

        // Queue plain text to file
        s_logChannel.Writer.TryWrite(logLine);
    }

    /// <summary>Logs a trace message with a specified tag.</summary>
    public static void Trace(string tag, string message) => Log(LogLevel.Trace, tag, message);

    /// <summary>Logs a verbose message with a specified tag.</summary>
    public static void Verbose(string tag, string message) => Log(LogLevel.Verbose, tag, message);

    /// <summary>Logs a debug message with a specified tag.</summary>
    public static void Debug(string tag, string message) => Log(LogLevel.Debug, tag, message);

    /// <summary>Logs an informational message with a specified tag.</summary>
    public static void Info(string tag, string message) => Log(LogLevel.Info, tag, message);

    /// <summary>Logs a warning message with a specified tag.</summary>
    public static void Warning(string tag, string message) => Log(LogLevel.Warning, tag, message);

    /// <summary>Logs an error message with a specified tag.</summary>
    public static void Error(string tag, string message) => Log(LogLevel.Error, tag, message);

    /// <summary>Logs an exception and associated error message with a specified tag.</summary>
    public static void Error(string tag, Exception ex, string message) =>
        Log(LogLevel.Error, tag, $"{message}\nException: {ex.GetType().FullName} - {ex.Message}\nStack Trace: {ex.StackTrace}");

    /// <summary>
    /// Queues a command to clear the physical log file and flush the internal buffer.
    /// </summary>
    public static void Clear() => s_logChannel.Writer.TryWrite(ClearCommand);

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
    protected override string GetTag() => typeof(T).Name;

    /// <summary>Logs a trace message.</summary>
    public static void Trace(string message) => Log(LogLevel.Trace, typeof(T).Name, message);
    /// <summary>Logs a verbose message.</summary>
    public static void Verbose(string message) => Log(LogLevel.Verbose, typeof(T).Name, message);
    /// <summary>Logs a debug message.</summary>
    public static void Debug(string message) => Log(LogLevel.Debug, typeof(T).Name, message);
    /// <summary>Logs an informational message.</summary>
    public static void Info(string message) => Log(LogLevel.Info, typeof(T).Name, message);
    /// <summary>Logs a warning message.</summary>
    public static void Warning(string message) => Log(LogLevel.Warning, typeof(T).Name, message);
    /// <summary>Logs an error message.</summary>
    public static void Error(string message) => Log(LogLevel.Error, typeof(T).Name, message);
    /// <summary>Logs an exception and associated error message.</summary>
    public static void Error(Exception ex, string message) => Log(LogLevel.Error, typeof(T).Name, $"{message}\n{ex}");
}
