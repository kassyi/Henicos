---
title: "CudaEventTimer Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Measures precise GPU kernel execution time using CUDA events."
---

## CudaEventTimer Class

## Definition

**Namespace:** `Kassyi.GSharp.Runtime.Interop`  
**Assembly:** `Kassyi.GSharp.Runtime.dll`

A helper class to measure precise GPU kernel execution time using CUDA events. In CPU fallback mode it measures wall-clock time instead.

```csharp
public sealed class CudaEventTimer : IDisposable
```

## Constructors

| Name | Description |
| --- | --- |
| [CudaEventTimer()](#cudaeventtimer) | Initializes a new instance of the `CudaEventTimer` class. |

## CudaEventTimer()

Creates the start and end events in the context that is current on the calling thread.

```csharp
public CudaEventTimer();
```

## Methods

| Name | Description |
| --- | --- |
| [Dispose()](#dispose) | Destroys the CUDA events. |
| [GetElapsedTimeMilliseconds()](#getelapsedtimemilliseconds) | Computes the elapsed time between Start and Stop events in milliseconds. |
| [Start(IntPtr)](#startintptr) | Records the start event in the specified stream. |
| [Stop(IntPtr)](#stopintptr) | Records the end event in the specified stream. |
| [Synchronize()](#synchronize) | Synchronizes the host thread until the end event has been recorded. |
| [SynchronizeAsync(CancellationToken)](#synchronizeasynccancellationtoken) | Asynchronously waits until the end event has been recorded. |

## Dispose()

Destroys the CUDA events.

```csharp
public void Dispose();
```

## GetElapsedTimeMilliseconds()

Computes the elapsed time between Start and Stop events in milliseconds. Both events must have been recorded and synchronized first.

```csharp
public float GetElapsedTimeMilliseconds();
```

#### Returns

_float_<br>
The elapsed execution time in milliseconds.

## Start(IntPtr)

Records the start event in the specified stream.

```csharp
public void Start(IntPtr hStream = 0);
```

#### Parameters

`hStream` _IntPtr_<br>
The stream to record the event in.

## Stop(IntPtr)

Records the end event in the specified stream.

```csharp
public void Stop(IntPtr hStream = 0);
```

#### Parameters

`hStream` _IntPtr_<br>
The stream to record the event in.

## Synchronize()

Blocks the host thread until the end event has been recorded.

```csharp
public void Synchronize();
```

## SynchronizeAsync(CancellationToken)

Asynchronously waits until the end event has been recorded, without blocking the calling thread.

```csharp
public ValueTask SynchronizeAsync(CancellationToken cancellationToken = default);
```

#### Parameters

`cancellationToken` _CancellationToken_<br>
A token that detaches the awaiter; the GPU work itself is not canceled.

#### Returns

_ValueTask_<br>
A task that completes once `GetElapsedTimeMilliseconds()` can be called.

#### Exceptions

`ObjectDisposedException`<br>
The timer has been disposed.

## Remarks

`SynchronizeAsync` waits for the stream passed to `Stop` to drain. This is a superset of the end event, because the event is the last recorded item on that stream at the time of `Stop`.

## See also

### BCL API > Host Runtime

- [Host Runtime](00_index.md)
- [CudaStream Class](CudaStream.md)
- [CudaStreamCompletion Class](CudaStreamCompletion.md)
