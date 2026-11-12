namespace Kassyi.GSharp.Core;

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
