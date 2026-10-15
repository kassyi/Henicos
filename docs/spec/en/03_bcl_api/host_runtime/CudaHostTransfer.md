---
title: "CudaHostTransfer Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Provides device-to-host transfers that do not block the calling thread."
---

## CudaHostTransfer Class

## Definition

**Namespace:** `Kassyi.GSharp.Runtime`  
**Assembly:** `Kassyi.GSharp.Runtime.dll`

Provides device-to-host transfers that do not block the calling thread.

```csharp
public static class CudaHostTransfer
```

A device-to-host copy into pageable memory (any managed array) is synchronous: it waits for the copy and for all work queued ahead of it on the stream, however long that takes. `CudaHostTransfer` stages the transfer through page-locked buffers from a process-wide pool and signals completion through [CudaStreamCompletion](CudaStreamCompletion.md) without parking a thread.

## Methods

| Name                                                                                          | Description                                                                         |
| --------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------- |
| [CopyToHostAsync&lt;T&gt;(IntPtr, Memory&lt;T&gt;, IntPtr, CancellationToken)](#copytohostasynctintptr-memoryt-intptr-cancellationtoken) | Asynchronously copies `destination.Length` elements from device memory into host memory. |

## CopyToHostAsync&lt;T&gt;(IntPtr, Memory&lt;T&gt;, IntPtr, CancellationToken)

Asynchronously copies `destination.Length` elements from device memory into `destination`.

```csharp
public static ValueTask CopyToHostAsync<T>(IntPtr devicePointer, Memory<T> destination, IntPtr stream, CancellationToken cancellationToken = default) where T : unmanaged;
```

#### Type Parameters

`T`<br>
The unmanaged element type; at most 1 MiB in size.

#### Parameters

`devicePointer` _IntPtr_<br>
The device address to copy from. In CPU fallback mode, the host address that stands in for device memory.

`destination` _Memory&lt;T&gt;_<br>
The host memory to fill. Its length determines the number of elements copied.

`stream` _IntPtr_<br>
The stream the copy is ordered on, or `IntPtr.Zero` for the legacy null stream. The copy observes all work enqueued on the stream before this call.

`cancellationToken` _CancellationToken_<br>
A token that detaches the awaiter; the device work itself is not canceled.

#### Returns

_ValueTask_<br>
A task that completes once `destination` holds the data.

#### Exceptions

`NotSupportedException`<br>
`T` is larger than 1 MiB.

`ArgumentException`<br>
`devicePointer` is `IntPtr.Zero` for a non-empty destination.

`InvalidOperationException`<br>
The stream is currently being captured into a CUDA Graph.

`CudaException`<br>
The copy could not be enqueued, or the context faulted while waiting.

`OperationCanceledException`<br>
`cancellationToken` was canceled.

## Remarks

> [!IMPORTANT]
> **Cancellation only detaches the awaiter.** Queued device work is not canceled. Once cancellation is observed, no further data is written to the destination, which may already hold the chunks that completed before. A staging buffer returns to the pool only once the device has finished writing to it.

**Chunking.** Transfers are split into chunks of whole elements of at most 1 MiB, one staging buffer and one completion wait each, so arbitrarily large transfers (including those beyond 2 GiB) need only a bounded amount of page-locked memory.

**Allocation.** Staging buffers and completion sources are pooled. Page-locked memory is allocated only while the pool grows (at most 16 idle buffers are kept), and it is never freed on the caller's path, because freeing page-locked memory waits for all work on every stream of the device.

**Streams of isolated contexts.** The staging memory is allocated as portable in the global context, so copies on streams of isolated `GpuWorkspace` contexts stay asynchronous.

**CPU fallback.** Without a CUDA device the copy completes synchronously from the host address.

## Examples

### Reading Device Memory Back Without Blocking a Thread

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Runtime

```csharp
using System;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;

// Execution Context: Host (CPU)
const int Count = 1024;
var source = new uint[Count];
for (var i = 0; i < Count; i++)
{
    source[i] = (uint)(i * 3);
}

IntPtr device;
CudaStream stream;

// A context scope is bound to the OS thread, so it covers only synchronous sections and never an await.
using (new GpuKernelLoader.CudaContextScope())
{
    CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out device, (IntPtr)(Count * sizeof(uint))));
    var pinned = GCHandle.Alloc(source, GCHandleType.Pinned);
    try
    {
        CudaException.ThrowIfError(
            CudaDriverApi.cuMemcpyHtoD_v2(device, pinned.AddrOfPinnedObject(), (IntPtr)(Count * sizeof(uint))));
    }
    finally
    {
        pinned.Free();
    }

    CudaException.ThrowIfError(CudaDriverApi.cuCtxSynchronize());
    stream = new CudaStream();
}

var destination = new uint[Count];
await CudaHostTransfer.CopyToHostAsync(device, destination.AsMemory(), stream.Handle);

Console.WriteLine($"Last={destination[Count - 1]}");

using (new GpuKernelLoader.CudaContextScope())
{
    stream.Dispose();
    CudaDriverApi.cuMemFree_v2(device);
}
```

## See also

### BCL API > Host Runtime

- [Host Runtime](00_index.md)
- [CudaStreamCompletion Class](CudaStreamCompletion.md)
- [CudaStream Class](CudaStream.md)
