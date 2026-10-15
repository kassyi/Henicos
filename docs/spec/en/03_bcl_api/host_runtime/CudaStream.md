---
title: "CudaStream Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Represents a CUDA stream for asynchronous concurrent execution."
---

## CudaStream Class

## Definition

**Namespace:** `Kassyi.GSharp.Runtime`  
**Assembly:** `Kassyi.GSharp.Runtime.dll`

Represents a CUDA stream for asynchronous concurrent execution.

```csharp
public sealed class CudaStream : IDisposable
```

## Constructors

| Name | Description |
| --- | --- |
| [CudaStream()](#cudastream) | Creates a new non-blocking CUDA stream. |

## CudaStream()

Creates a new non-blocking CUDA stream (`CU_STREAM_NON_BLOCKING`) in the context that is current on the calling thread.

```csharp
public CudaStream();
```

#### Exceptions

`CudaException`<br>
The driver could not create the stream.

## Properties

| Name | Description |
| --- | --- |
| [Handle](#handle) | The underlying CUDA stream handle. |

## Handle

The underlying CUDA stream handle.

```csharp
public IntPtr Handle { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [Dispose()](#dispose) | Destroys the CUDA stream. |
| [Synchronize()](#synchronize) | Blocks the current host thread until all operations in this stream have completed. |
| [SynchronizeAsync(CancellationToken)](#synchronizeasynccancellationtoken) | Asynchronously waits until all operations currently enqueued on this stream have completed. |

## Dispose()

Destroys the CUDA stream. `cuStreamDestroy` does not implicitly synchronize, but the device completes all pending work before the stream is destroyed.

```csharp
public void Dispose();
```

## Synchronize()

Blocks the current host thread until all operations in this stream have completed.

```csharp
public void Synchronize();
```

## SynchronizeAsync(CancellationToken)

Asynchronously waits until all operations currently enqueued on this stream have completed, without blocking the calling thread.

```csharp
public ValueTask SynchronizeAsync(CancellationToken cancellationToken = default);
```

#### Parameters

`cancellationToken` _CancellationToken_<br>
A token that detaches the awaiter; the GPU work itself is not canceled.

#### Returns

_ValueTask_<br>
A task that completes when the stream has drained.

## Remarks

`SynchronizeAsync` is implemented by [CudaStreamCompletion.WaitAsync](CudaStreamCompletion.md#waitasyncintptr-cancellationtoken); see it for cancellation, capture and host-function serialization semantics.

## See also

### BCL API > Host Runtime

- [Host Runtime](00_index.md)
- [CudaStreamCompletion Class](CudaStreamCompletion.md)
- [CudaEventTimer Class](CudaEventTimer.md)
