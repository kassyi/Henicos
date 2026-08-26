using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using MSLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Kassyi.GSharp.Core;

/// <summary>ログの出力レベルを表します。</summary>
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for library consumers")]
public enum LogLevel
{
    Trace = 0,
    Verbose = 1,
    Debug = 2,
    Info = 3,
    Warning = 4,
    Error = 5,
    None = 6
}

/// <summary>パフォーマンス計測および診断ログの出力を行う基底クラスです。</summary>
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for library consumers")]
[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for library consumers")]
public abstract class Logger : ILogger
{
    private const int MaxRetries = 3;
    private const int BufferCapacity = 10000;
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

    public bool IsEnabled(MSLogLevel logLevel) => MapLogLevel(logLevel) >= ActiveLogLevel;

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
                            Debug.WriteLine(errLine);
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
                Debug.WriteLine($"[LOGGER_FATAL] Background logging task iteration failed: {ex}");
            }
            finally
            {
                buffer.Clear();
            }
        }
    }

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

    protected static void Log(LogLevel level, string tag, string? message)
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

        // Debug output for VS Code output window
        Debug.WriteLine(logLine);

        // Queue plain text to file
        s_logChannel.Writer.TryWrite(logLine);
    }

    public static void Clear() => s_logChannel.Writer.TryWrite(ClearCommand);

    protected static void AddInterval(string key, long elapsedMilliseconds) =>
        s_accumulatedTimes.AddOrUpdate(key, elapsedMilliseconds, (_, current) => current + elapsedMilliseconds);

    protected static void PrintAccumulatedInternal(string tag, string prefixMessage, LogLevel level = LogLevel.Debug)
    {
        if (level < ActiveLogLevel || s_accumulatedTimes.IsEmpty)
        {
            return;
        }

        Log(
            level,
            tag,
            $"{prefixMessage} [{string.Join(", ", s_accumulatedTimes.Select(kv => $"{kv.Key}: {kv.Value} ms"))}]"
        );
    }

    public static void ClearAccumulated() => s_accumulatedTimes.Clear();

    // ============================================================
    // Microsoft.Extensions.Logging.ILogger Implementation
    // ============================================================

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

    /// <summary>処理時間の累積計測を行うためのタイマーです。</summary>
    [SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for library consumers")]
    [SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for library consumers")]
    public class PerformanceTimer
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();

        public long Lap(string key)
        {
            var elapsed = _sw.ElapsedMilliseconds;
            AddInterval(key, elapsed);
            _sw.Restart();
            return elapsed;
        }
    }

    /// <summary>指定されたスコープの実行時間を計測しログ出力するためのディスポーザブルなスコープです。</summary>
    public class MeasureScope : IDisposable
    {
        private readonly LogLevel _level;
        private readonly Stopwatch _sw;

        private readonly string _tag,
            _scopeName;

        private bool _disposed;

        public MeasureScope(string tag, string scopeName, LogLevel level)
        {
            (_tag, _scopeName, _level, _sw) = (tag, scopeName, level, Stopwatch.StartNew());
            Log(_level, _tag, $"[Start Scope] {_scopeName}");
        }

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

/// <summary>型パラメータに基づく静的ロギングメソッドを提供する具象ロガーです。</summary>
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for library consumers")]
[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for library consumers")]
public class Logger<T> : Logger
{
    protected override string GetTag() => typeof(T).Name;

    public static void Trace(string message) => Log(LogLevel.Trace, typeof(T).Name, message);
    public static void Debug(string message) => Log(LogLevel.Debug, typeof(T).Name, message);
    public static void Info(string message) => Log(LogLevel.Info, typeof(T).Name, message);
    public static void Warning(string message) => Log(LogLevel.Warning, typeof(T).Name, message);
    public static void Error(string message) => Log(LogLevel.Error, typeof(T).Name, message);
    public static void Error(Exception ex, string message) => Log(LogLevel.Error, typeof(T).Name, $"{message}\n{ex}");
}
