using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using MSLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Kassyi.GSharp.Core;

/// <summary>ログの出力レベルを表します。</summary>
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
public abstract class Logger : ILogger
{
    private const int MaxRetries = 3;

    private static readonly string s_logPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "gsharp_diagnostics.log"
    );

    private static readonly Channel<string> s_logChannel = Channel.CreateUnbounded<string>();

    private static readonly CancellationTokenSource s_cts = new();

    private static readonly ConcurrentDictionary<string, long> s_accumulatedTimes = new();

    static Logger()
    {
        // バックグラウンドでログ書き込みタスクを開始
        Task.Run(ProcessLogQueueAsync);
    }

    public static LogLevel ActiveLogLevel { get; set; } =
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
        try
        {
            var buffer = new List<string>(100);

            await foreach (var logMessage in s_logChannel.Reader.ReadAllAsync(s_cts.Token))
            {
                buffer.Add(logMessage);

                while (s_logChannel.Reader.TryRead(out var msg))
                {
                    buffer.Add(msg);
                    if (buffer.Count >= 100)
                    {
                        break;
                    }
                }

                var retryCount = 0;
                var written = false;
                while (!written && retryCount < MaxRetries)
                {
                    try
                    {
                        await File.AppendAllLinesAsync(s_logPath, buffer);
                        written = true;
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        if (retryCount >= MaxRetries)
                        {
                            foreach (var line in buffer)
                            {
                                var errLine =
                                    $"[LOGGER_FALLBACK_ERROR] Failed to write log: {line} (Reason: {ex.Message})";
                                await Console.Error.WriteLineAsync(errLine);
                                Debug.WriteLine(errLine);
                            }
                        }
                        else
                        {
                            await Task.Delay(50 * retryCount).ConfigureAwait(false);
                        }
                    }
                }

                buffer.Clear();
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown
        }
    }

    public static void Shutdown()
    {
        s_logChannel.Writer.Complete();
        s_cts.Cancel();
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

    public static void Clear()
    {
        try
        {
            if (File.Exists(s_logPath))
            {
                File.Delete(s_logPath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to clear log file: {ex.Message}");
        }
    }

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
