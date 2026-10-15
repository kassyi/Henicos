---
title: "AotMemoryHandler Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Manages unmanaged CUDA device memory allocations, clearing, stream synchronization, and host-device/device-device copies."
---

## AotMemoryHandler Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Implements `IGpuMemoryProvider` by bridging low-level CUDA Driver API memory operations (`cuMemAlloc_v2`, `cuMemFree_v2`, `cuMemFreeAsync`, `cuMemsetD8Async`, `cuMemsetD32Async`, `cuMemcpyHtoDAsync_v2`, `cuMemcpyDtoHAsync_v2`, `cuMemcpyDtoDAsync_v2`) and non-blocking async transfers (`CudaHostTransfer`, `CudaStreamCompletion`) into the G# execution pipeline.

```csharp
public sealed class AotMemoryHandler : IGpuMemoryProvider
```

## Methods

| Name | Description |
| --- | --- |
| `AllocateMemory(int)` | Allocates `numWords * 4` bytes of global device memory via `cuMemAlloc_v2`. |
| `FreeMemory(IntPtr, IntPtr)` | Releases device memory synchronously (`cuMemFree_v2` when `stream == 0`) or stream-ordered (`cuMemFreeAsync` when `stream != 0`). |
| `ClearMemoryAsync(IntPtr, int, IntPtr)` | Asynchronously zeroes out `numWords * 4` bytes on `stream` via `cuMemsetD8Async`. |
| `CopyHostToDevice(IntPtr, uint[], int, IntPtr)` | Copies `numWords` 32-bit words from a managed `uint[]` array to device memory via `cuMemcpyHtoDAsync_v2`. |
| `CopyDeviceToHost(uint[], IntPtr, int, IntPtr)` | Copies `numWords` 32-bit words from device memory to a managed `uint[]` array and synchronizes `stream`. |
| `CopyDeviceToHostAsync(Memory<uint>, IntPtr, int, IntPtr, CancellationToken)` | Asynchronously copies `numWords` 32-bit words from device memory to `Memory<uint>` via `CudaHostTransfer.CopyToHostAsync`. |
| `CopyDeviceToDevice(IntPtr, IntPtr, int, IntPtr)` | Copies `numWords` 32-bit words between device memory pointers on `stream` via `cuMemcpyDtoDAsync_v2`. |
| `ReadWord(IntPtr, int, IntPtr)` | Reads a single 32-bit word at `wordOffset` from device memory. |
| `WriteWord(IntPtr, int, uint, IntPtr)` | Writes a single 32-bit word at `wordOffset` to device memory (using `cuMemsetD32Async` when `stream != 0` to avoid host buffer pinning). |
| `SynchronizeStream(IntPtr)` | Blocks the calling host thread until all work on `stream` completes via `cuStreamSynchronize`. |
| `SynchronizeStreamAsync(IntPtr, CancellationToken)` | Asynchronously awaits stream completion via `CudaStreamCompletion.WaitAsync`. |

## Examples

### Instantiating AotMemoryHandler

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

IGpuMemoryProvider memoryHandler = new AotMemoryHandler();
Console.WriteLine($"Initialized memory handler: {memoryHandler.GetType().Name}");
```

## Remarks

When a non-zero `stream` handle is supplied to `WriteWord`, `AotMemoryHandler` uses `cuMemsetD32Async` so the 32-bit payload travels inline in the CUDA command buffer without requiring a pinned host buffer or host synchronization.

## See also

### BCL API > Math (BigNum)

- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
- [GSharpAotProvider](GSharpAotProvider.md)
- [IGpuExecutionProvider](IGpuExecutionProvider.md)
