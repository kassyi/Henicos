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

Implements [`IGpuMemoryProvider`](IGpuExecutionProvider.md) by bridging low-level CUDA Driver API memory operations (`cuMemAlloc_v2`, `cuMemFree_v2`, `cuMemFreeAsync`, `cuMemsetD8Async`, `cuMemsetD32Async`, `cuMemcpyHtoDAsync_v2`, `cuMemcpyDtoHAsync_v2`, `cuMemcpyDtoDAsync_v2`) and non-blocking async transfers (`CudaHostTransfer`, `CudaStreamCompletion`) into the G# execution pipeline.

```csharp
public sealed class AotMemoryHandler : IGpuMemoryProvider
```

## Methods

| Name | Description |
| --- | --- |
| [AllocateMemory(Int32)](#allocatememoryint32) | Allocates `numWords * 4` bytes of global device memory via `cuMemAlloc_v2`. |
| [FreeMemory(IntPtr, IntPtr)](#freememoryintptr-intptr) | Releases device memory synchronously (`cuMemFree_v2` when `stream == 0`) or stream-ordered (`cuMemFreeAsync` when `stream != 0`). |
| [ClearMemoryAsync(IntPtr, Int32, IntPtr)](#clearmemoryasyncintptr-int32-intptr) | Asynchronously zeroes out `numWords * 4` bytes on `stream` via `cuMemsetD8Async`. |
| [CopyHostToDevice(IntPtr, UInt32[], Int32, IntPtr)](#copyhosttodeviceintptr-uint32-int32-intptr) | Copies `numWords` 32-bit words from a managed `uint[]` array to device memory via `cuMemcpyHtoDAsync_v2`. |
| [CopyDeviceToHost(UInt32[], IntPtr, Int32, IntPtr)](#copydevicetohostuint32-intptr-int32-intptr) | Copies `numWords` 32-bit words from device memory to a managed `uint[]` array and synchronizes `stream`. |
| [CopyDeviceToHostAsync(Memory\<UInt32\>, IntPtr, Int32, IntPtr, CancellationToken)](#copydevicetohostasyncmemoryuint32-intptr-int32-intptr-cancellationtoken) | Asynchronously copies `numWords` 32-bit words from device memory to `Memory<uint>` via `CudaHostTransfer.CopyToHostAsync`. |
| [CopyDeviceToDevice(IntPtr, IntPtr, Int32, IntPtr)](#copydevicetodeviceintptr-intptr-int32-intptr) | Copies `numWords` 32-bit words between device memory pointers on `stream` via `cuMemcpyDtoDAsync_v2`. |
| [ReadWord(IntPtr, Int32, IntPtr)](#readwordintptr-int32-intptr) | Reads a single 32-bit word at `wordOffset` from device memory. |
| [WriteWord(IntPtr, Int32, UInt32, IntPtr)](#writewordintptr-int32-uint32-intptr) | Writes a single 32-bit word at `wordOffset` to device memory (using `cuMemsetD32Async` when `stream != 0` to avoid host buffer pinning). |
| [SynchronizeStream(IntPtr)](#synchronizestreamintptr) | Blocks the calling host thread until all work on `stream` completes via `cuStreamSynchronize`. |
| [SynchronizeStreamAsync(IntPtr, CancellationToken)](#synchronizestreamasyncintptr-cancellationtoken) | Asynchronously awaits stream completion via `CudaStreamCompletion.WaitAsync`. |

## AllocateMemory(Int32)

Allocates `numWords * 4` bytes of global device memory via `cuMemAlloc_v2`.

```csharp
public IntPtr AllocateMemory(int numWords);
```

#### Parameters

`numWords` _Int32_<br>
The number of 32-bit words (`uint` limbs) to allocate.

#### Returns

_IntPtr_<br>
The allocated native CUDA device pointer (`CUdeviceptr`).

#### Exceptions

`CudaException`<br>
Thrown when `cuMemAlloc_v2` returns a non-zero `CUresult` (for example, `CUDA_ERROR_OUT_OF_MEMORY`).

## FreeMemory(IntPtr, IntPtr)

Releases device memory synchronously (`cuMemFree_v2` when `stream == IntPtr.Zero`) or stream-ordered (`cuMemFreeAsync` when `stream != IntPtr.Zero`).

```csharp
public void FreeMemory(IntPtr devicePtr, IntPtr stream = 0);
```

#### Parameters

`devicePtr` _IntPtr_<br>
The native CUDA device pointer to release. If `IntPtr.Zero`, the method returns without action.

`stream` _IntPtr_<br>
The optional CUDA stream handle (`CUstream`) for stream-ordered deallocation. Defaults to `0` (`IntPtr.Zero`).

#### Exceptions

`CudaException`<br>
Thrown when `cuMemFree_v2` or `cuMemFreeAsync` fails.

## ClearMemoryAsync(IntPtr, Int32, IntPtr)

Asynchronously zeroes out `numWords * 4` bytes of device memory on the specified CUDA stream via `cuMemsetD8Async`.

```csharp
public void ClearMemoryAsync(IntPtr devicePtr, int numWords, IntPtr stream);
```

#### Parameters

`devicePtr` _IntPtr_<br>
The target CUDA device pointer (`CUdeviceptr`).

`numWords` _Int32_<br>
The number of 32-bit words (`uint` limbs) to clear to zero. If `<= 0` or `devicePtr == IntPtr.Zero`, the method returns immediately.

`stream` _IntPtr_<br>
The CUDA stream handle (`CUstream`) on which the asynchronous memset is enqueued.

#### Exceptions

`CudaException`<br>
Thrown when `cuMemsetD8Async` returns an error.

## CopyHostToDevice(IntPtr, UInt32[], Int32, IntPtr)

Pins `hostData` and copies `numWords` 32-bit words to device memory via `cuMemcpyHtoDAsync_v2`, synchronizing `stream` when `stream == IntPtr.Zero`.

```csharp
public unsafe void CopyHostToDevice(IntPtr devicePtr, uint[] hostData, int numWords, IntPtr stream = 0);
```

#### Parameters

`devicePtr` _IntPtr_<br>
The destination CUDA device pointer (`CUdeviceptr`).

`hostData` _UInt32[]_<br>
The source managed array of 32-bit unsigned integer limbs.

`numWords` _Int32_<br>
The number of 32-bit words to copy.

`stream` _IntPtr_<br>
The CUDA stream handle (`CUstream`) for the transfer. Defaults to `0` (`IntPtr.Zero`).

#### Exceptions

`CudaException`<br>
Thrown when `cuMemcpyHtoDAsync_v2` or `cuStreamSynchronize` fails.

## CopyDeviceToHost(UInt32[], IntPtr, Int32, IntPtr)

Pins `hostData`, copies `numWords` 32-bit words from device memory via `cuMemcpyDtoHAsync_v2`, and synchronizes `stream` before unpinning to prevent DMA race conditions.

```csharp
public unsafe void CopyDeviceToHost(uint[] hostData, IntPtr devicePtr, int numWords, IntPtr stream = 0);
```

#### Parameters

`hostData` _UInt32[]_<br>
The destination managed array of 32-bit unsigned integer limbs.

`devicePtr` _IntPtr_<br>
The source CUDA device pointer (`CUdeviceptr`).

`numWords` _Int32_<br>
The number of 32-bit words to copy.

`stream` _IntPtr_<br>
The CUDA stream handle (`CUstream`) to synchronize before unpinning `hostData`. Defaults to `0` (`IntPtr.Zero`).

#### Exceptions

`CudaException`<br>
Thrown when `cuMemcpyDtoHAsync_v2` or `cuStreamSynchronize` fails.

## CopyDeviceToHostAsync(Memory\<UInt32\>, IntPtr, Int32, IntPtr, CancellationToken)

Asynchronously copies `numWords` 32-bit words from device memory to `Memory<uint>` via `CudaHostTransfer.CopyToHostAsync` without blocking the calling host thread.

```csharp
public ValueTask CopyDeviceToHostAsync(
    Memory<uint> hostData,
    IntPtr devicePtr,
    int numWords,
    IntPtr stream,
    CancellationToken cancellationToken = default);
```

#### Parameters

`hostData` _Memory\<UInt32\>_<br>
The destination memory buffer to receive the copied 32-bit words.

`devicePtr` _IntPtr_<br>
The source CUDA device pointer (`CUdeviceptr`).

`numWords` _Int32_<br>
The number of 32-bit words to copy.

`stream` _IntPtr_<br>
The CUDA stream handle (`CUstream`) on which the transfer and host callback are enqueued.

`cancellationToken` _CancellationToken_<br>
A token to monitor for cancellation requests. Defaults to `default`.

#### Returns

_ValueTask_<br>
A task that completes when the asynchronous device-to-host DMA transfer finishes on `stream`.

#### Exceptions

`ArgumentOutOfRangeException`<br>
Thrown when `numWords` exceeds `hostData.Length`.

`OperationCanceledException`<br>
Thrown when `cancellationToken` is canceled before or during the transfer.

`CudaException`<br>
Thrown when the underlying CUDA transfer or host callback registration fails.

## CopyDeviceToDevice(IntPtr, IntPtr, Int32, IntPtr)

Copies `numWords` 32-bit words between device memory pointers on `stream` via `cuMemcpyDtoDAsync_v2`.

```csharp
public void CopyDeviceToDevice(IntPtr dstDevicePtr, IntPtr srcDevicePtr, int numWords, IntPtr stream = 0);
```

#### Parameters

`dstDevicePtr` _IntPtr_<br>
The destination CUDA device pointer (`CUdeviceptr`).

`srcDevicePtr` _IntPtr_<br>
The source CUDA device pointer (`CUdeviceptr`).

`numWords` _Int32_<br>
The number of 32-bit words to copy.

`stream` _IntPtr_<br>
The CUDA stream handle (`CUstream`) on which the device-to-device copy is enqueued. Defaults to `0` (`IntPtr.Zero`).

#### Exceptions

`CudaException`<br>
Thrown when `cuMemcpyDtoDAsync_v2` returns an error.

## ReadWord(IntPtr, Int32, IntPtr)

Reads a single 32-bit word at `wordOffset` from device memory via `cuMemcpyDtoHAsync_v2` and synchronizes `stream`.

```csharp
public unsafe uint ReadWord(IntPtr devicePtr, int wordOffset = 0, IntPtr stream = 0);
```

#### Parameters

`devicePtr` _IntPtr_<br>
The base CUDA device pointer (`CUdeviceptr`).

`wordOffset` _Int32_<br>
The zero-based 32-bit word offset from `devicePtr`. Defaults to `0`.

`stream` _IntPtr_<br>
The CUDA stream handle (`CUstream`) to synchronize before reading. Defaults to `0` (`IntPtr.Zero`).

#### Returns

_UInt32_<br>
The 32-bit unsigned integer read from `devicePtr + wordOffset * 4`.

#### Exceptions

`CudaException`<br>
Thrown when `cuMemcpyDtoHAsync_v2` or `cuStreamSynchronize` fails.

## WriteWord(IntPtr, Int32, UInt32, IntPtr)

Writes a single 32-bit word at `wordOffset` to device memory (using `cuMemsetD32Async` when `stream != IntPtr.Zero` to avoid host buffer pinning).

```csharp
public unsafe void WriteWord(IntPtr devicePtr, int wordOffset, uint value, IntPtr stream = 0);
```

#### Parameters

`devicePtr` _IntPtr_<br>
The base CUDA device pointer (`CUdeviceptr`).

`wordOffset` _Int32_<br>
The zero-based 32-bit word offset from `devicePtr`.

`value` _UInt32_<br>
The 32-bit unsigned integer value to write.

`stream` _IntPtr_<br>
The CUDA stream handle (`CUstream`) on which the write is enqueued. Defaults to `0` (`IntPtr.Zero`).

#### Exceptions

`CudaException`<br>
Thrown when `cuMemsetD32Async`, `cuMemcpyHtoDAsync_v2`, or `cuStreamSynchronize` fails.

## SynchronizeStream(IntPtr)

Blocks the calling host thread until all work on `stream` completes via `cuStreamSynchronize`.

```csharp
public void SynchronizeStream(IntPtr stream);
```

#### Parameters

`stream` _IntPtr_<br>
The CUDA stream handle (`CUstream`) to synchronize.

#### Exceptions

`CudaException`<br>
Thrown when `cuStreamSynchronize` returns an error.

## SynchronizeStreamAsync(IntPtr, CancellationToken)

Asynchronously awaits stream completion via `CudaStreamCompletion.WaitAsync`.

```csharp
public ValueTask SynchronizeStreamAsync(IntPtr stream, CancellationToken cancellationToken = default);
```

#### Parameters

`stream` _IntPtr_<br>
The CUDA stream handle (`CUstream`) to await.

`cancellationToken` _CancellationToken_<br>
A token to monitor for cancellation requests. Defaults to `default`.

#### Returns

_ValueTask_<br>
A task that completes when all preceding operations on `stream` have finished.

#### Exceptions

`OperationCanceledException`<br>
Thrown when `cancellationToken` is canceled before stream completion.

`CudaException`<br>
Thrown when registering the CUDA host callback or querying stream status fails.

## Remarks

When a non-zero `stream` handle is supplied to `WriteWord`, `AotMemoryHandler` uses `cuMemsetD32Async` so the 32-bit payload travels inline in the CUDA command buffer without requiring a pinned host buffer or host synchronization.

## Examples

### Instantiating AotMemoryHandler

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
// Execution Context: Host (CPU)
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

IGpuMemoryProvider memoryHandler = new AotMemoryHandler();
Console.WriteLine($"Initialized memory handler: {memoryHandler.GetType().Name}");
```

## See also

### BCL API > Math (BigNum)

- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
- [GSharpAotProvider](GSharpAotProvider.md)
- [IGpuExecutionProvider](IGpuExecutionProvider.md)
