---
title: "CudaStreamCompletion Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Provides non-blocking completion notification for work enqueued on CUDA streams."
---

## CudaStreamCompletion Class

## Definition

**Namespace:** `Kassyi.GSharp.Runtime`  
**Assembly:** `Kassyi.GSharp.Runtime.dll`

Provides non-blocking completion notification for work enqueued on CUDA streams.

```csharp
public static class CudaStreamCompletion
```

`cuStreamSynchronize` parks the calling OS thread until the GPU finishes. In high-concurrency hosts (ASP.NET Core, gRPC, background workers) this starves the thread pool. `WaitAsync` releases the calling thread and resumes the continuation on the thread pool once the stream has drained.

## Methods

| Name                                                                   | Description                                                                 |
| ---------------------------------------------------------------------- | --------------------------------------------------------------------------- |
| [WaitAsync(IntPtr, CancellationToken)](#waitasyncintptr-cancellationtoken) | Asynchronously waits until all work currently enqueued on a stream has completed. |

## WaitAsync(IntPtr, CancellationToken)

Asynchronously waits until all work currently enqueued on `stream` has completed.

```csharp
public static ValueTask WaitAsync(IntPtr stream, CancellationToken cancellationToken = default);
```

#### Parameters

`stream` _IntPtr_<br>
The CUDA stream handle to observe. `IntPtr.Zero` denotes the legacy default stream.

`cancellationToken` _CancellationToken_<br>
A token that detaches the awaiter. Cancellation never cancels or invalidates the GPU work itself; the stream keeps executing and remains usable.

#### Returns

_ValueTask_<br>
A task that completes when the stream has drained.

#### Exceptions

`InvalidOperationException`<br>
The stream is currently being captured into a CUDA Graph.

`CudaException`<br>
The completion signal could not be enqueued, or the context faulted while waiting.

`OperationCanceledException`<br>
`cancellationToken` was canceled.

## Remarks

Only work enqueued **before** the call is observed. In CPU fallback mode the returned task is already completed. The stream must not be destroyed while the returned task is pending, because fault detection queries it.

The completion signal is armed in the context that **owns** the stream (resolved with `cuStreamGetCtx`), not in the context that happens to be current on the calling thread. A stream of an isolated `GpuWorkspace` can therefore be awaited from ordinary code, and the calling thread's current context is left unchanged.

**Allocation.** Completion sources are pooled, so awaiting with a non-cancelable token does not allocate in steady state.

> [!IMPORTANT]
> **Host-function serialization.** The completion signal is a host function (`cuLaunchHostFunc`). The CUDA driver executes host functions of a context on a single internal thread, so a long-running or blocking host function enqueued by other code on *any* stream delays completion signals of all streams in that context.

## Examples

### Awaiting a Stream

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Runtime

```csharp
using System;
using System.Threading;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;

// Execution Context: Host (CPU)
CudaStream stream;
using (new GpuKernelLoader.CudaContextScope())
{
    stream = new CudaStream();
}

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
await CudaStreamCompletion.WaitAsync(stream.Handle, cts.Token);

Console.WriteLine("Stream drained.");

using (new GpuKernelLoader.CudaContextScope())
{
    stream.Dispose();
}
```

## See also

### BCL API > Host Runtime

- [Host Runtime](00_index.md)
- [CudaStream Class](CudaStream.md)
- [CudaHostTransfer Class](CudaHostTransfer.md)
