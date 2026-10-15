---
title: "Logger Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Provides logging, Microsoft.Extensions.Logging integration, and performance interval tracking."
---

## Logger Class

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Provides logging and performance interval tracking, implementing `Microsoft.Extensions.Logging.ILogger`.

```csharp
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

public abstract class Logger : Microsoft.Extensions.Logging.ILogger
public class Logger<T> : Logger
```

## Properties

| Name | Description |
| --- | --- |
| [ActiveLogLevel](#activeloglevel) | Gets or sets the minimum active log level for message processing. Defaults to `LogLevel.Trace`. |
| [WriteToConsole](#writetoconsole) | Gets or sets a value indicating whether log entries are written to standard console output. |

## ActiveLogLevel

Gets or sets the minimum active log level for message processing.

```csharp
public static LogLevel ActiveLogLevel { get; set; }
```

## WriteToConsole

Gets or sets a value indicating whether log entries are written to standard console output (stored in an `AsyncLocal<bool>`).

```csharp
public static bool WriteToConsole { get; set; }
```

## Methods

| Name | Description |
| --- | --- |
| [Log(LogLevel, string, string?)](#logloglevel-string-string) | Writes a formatted log message with the specified severity level and component tag. |
| [Trace(string, string)](#tracestring-string) | Logs a trace message with a specified tag. |
| [Verbose(string, string)](#verbosestring-string) | Logs a verbose message with a specified tag. |
| [Debug(string, string)](#debugstring-string) | Logs a debug message with a specified tag. |
| [Info(string, string)](#infostring-string) | Logs an informational message with a specified tag. |
| [Warning(string, string)](#warningstring-string) | Logs a warning message with a specified tag. |
| [Error(string, string)](#errorstring-string) | Logs an error message with a specified tag. |
| [Error(string, Exception, string)](#errorstring-exception-string) | Logs an exception and associated error message with a specified tag. |
| [ClearAccumulated()](#clearaccumulated) | Clears all accumulated performance intervals. |
| [IsEnabled(Microsoft.Extensions.Logging.LogLevel)](#isenabledmicrosoftextensionsloggingloglevel) | Checks if the given `Microsoft.Extensions.Logging.LogLevel` is enabled. |
| [BeginScope\<TState\>(TState)](#beginscopetstatetstate) | Begins a logical operation scope (returns `null`). |
| [Log\<TState\>(...)](#logtstate) | Writes a log entry via the `Microsoft.Extensions.Logging.ILogger` contract. |
| [AddInterval(string, long)](#addintervalstring-long) | Accumulates the elapsed time for a given performance metric key (`protected static`). |
| [PrintAccumulatedInternal(string, string, LogLevel)](#printaccumulatedinternalstring-string-loglevel) | Logs all accumulated performance intervals (`protected static`). |
| [GetTag()](#gettag) | When overridden in a derived class, gets the component tag used to identify the logger source (`protected abstract`). |

## Log(LogLevel, string, string?)

Writes a formatted log message to standard output (or standard error for `LogLevel.Error`) if enabled.

```csharp
public static void Log(LogLevel level, string tag, string? message);
```

#### Parameters

`level` _LogLevel_<br>
The severity level of the log.

`tag` _string_<br>
The component tag identifying the log source.

`message` _string?_<br>
The message to log. If `null`, the operation is ignored.

## Trace(string, string)

Logs a trace message with a specified tag.

```csharp
public static void Trace(string tag, string message);
```

#### Parameters

`tag` _string_<br>
A category or component identifier for the log entry.

`message` _string_<br>
The message text to log.

## Verbose(string, string)

Logs a verbose message with a specified tag.

```csharp
public static void Verbose(string tag, string message);
```

#### Parameters

`tag` _string_<br>
A category or component identifier for the log entry.

`message` _string_<br>
The message text to log.

## Debug(string, string)

Logs a debug message with a specified tag.

```csharp
public static void Debug(string tag, string message);
```

#### Parameters

`tag` _string_<br>
A category or component identifier for the log entry.

`message` _string_<br>
The message text to log.

## Info(string, string)

Logs an informational message with a specified tag.

```csharp
public static void Info(string tag, string message);
```

#### Parameters

`tag` _string_<br>
A category or component identifier for the log entry.

`message` _string_<br>
The message text to log.

## Warning(string, string)

Logs a warning message with a specified tag.

```csharp
public static void Warning(string tag, string message);
```

#### Parameters

`tag` _string_<br>
A category or component identifier for the log entry.

`message` _string_<br>
The message text to log.

## Error(string, string)

Logs an error message with a specified tag.

```csharp
public static void Error(string tag, string message);
```

#### Parameters

`tag` _string_<br>
A category or component identifier for the log entry.

`message` _string_<br>
The message text to log.

## Error(string, Exception, string)

Logs an exception and associated error message with a specified tag.

```csharp
public static void Error(string tag, Exception ex, string message);
```

#### Parameters

`tag` _string_<br>
A category or component identifier for the log entry.

`ex` _Exception_<br>
The exception to format and record.

`message` _string_<br>
An explanatory message accompanying the exception.

## ClearAccumulated()

Clears all accumulated performance intervals.

```csharp
public static void ClearAccumulated();
```

## IsEnabled(Microsoft.Extensions.Logging.LogLevel)

Checks if the given `Microsoft.Extensions.Logging.LogLevel` is enabled against `ActiveLogLevel`.

```csharp
public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel);
```

#### Parameters

`logLevel` _Microsoft.Extensions.Logging.LogLevel_<br>
The level to be checked.

#### Returns

_bool_<br>
`true` if enabled; otherwise, `false`.

## BeginScope\<TState\>(TState)

Begins a logical operation scope.

```csharp
public IDisposable? BeginScope<TState>(TState state) where TState : notnull;
```

#### Parameters

`state` _TState_<br>
The identifier for the scope.

#### Returns

_IDisposable?_<br>
Always returns `null`.

## Log\<TState\>(...)

Writes a log entry via `Microsoft.Extensions.Logging.ILogger`.

```csharp
public void Log<TState>(
    Microsoft.Extensions.Logging.LogLevel logLevel,
    Microsoft.Extensions.Logging.EventId eventId,
    TState state,
    Exception? exception,
    Func<TState, Exception?, string> formatter);
```

## AddInterval(string, long)

Accumulates the elapsed time for a given performance metric key.

```csharp
protected static void AddInterval(string key, long elapsedMilliseconds);
```

## PrintAccumulatedInternal(string, string, LogLevel)

Logs a summary message containing all accumulated performance intervals.

```csharp
protected static void PrintAccumulatedInternal(
    string tag,
    string prefixMessage,
    LogLevel level = LogLevel.Debug);
```

## GetTag()

Gets the component tag used to identify the logger's source.

```csharp
protected abstract string GetTag();
```

## Examples

### Structured Logging and Scope Measurement

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
using System;
using Kassyi.GSharp.Core;

// Execution Context: Host (CPU)

Logger.ActiveLogLevel = LogLevel.Info;
Logger<SampleWorker>.Info("Starting GPU pipeline stage.");

using (new Logger.MeasureScope("SampleWorker", "KernelExecution", LogLevel.Info))
{
    var timer = new Logger.PerformanceTimer();
    timer.Lap("Stage1");
}

Logger.ClearAccumulated();

public sealed class SampleWorker
{
}
```

## Remarks

### Nested Helper Types & `Logger<T>`

- **`Logger<T>`**: Provides strongly-typed static methods (`Trace(string)`, `Verbose(string)`, `Debug(string)`, `Info(string)`, `Warning(string)`, `Error(string)`, `Error(Exception, string)`) that automatically tag entries with `typeof(T).Name`.
- **`Logger.PerformanceTimer`**: Tracks elapsed intervals across pipeline stages via `public long Lap(string key)`.
- **`Logger.MeasureScope`**: A disposable scope (`public MeasureScope(string tag, string scopeName, LogLevel level)`) that logs the start and elapsed milliseconds of an operation upon disposal.

## See also

### BCL API

- [BCL API and Hardware Intrinsics Reference](00_index.md)
- [CudaEnvironment Class (G#)](CudaEnvironment.md)
