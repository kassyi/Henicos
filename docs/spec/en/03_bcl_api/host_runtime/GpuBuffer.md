---
title: "GpuBuffer<T> Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Represents a contiguous region of device (GPU) memory strongly typed to T."
---

## GpuBuffer\<T\> Class

## Definition

**Namespace:** `Kassyi.GSharp.Runtime`  
**Assembly:** `Kassyi.GSharp.Runtime.dll`

Represents a contiguous region of device (GPU) memory strongly typed to `T`. Manages allocation and deallocation natively.

```csharp
public sealed class GpuBuffer<T> : IDisposable where T : unmanaged
```

#### Type Parameters

`T`<br>
The unmanaged type of the elements in the buffer.

## Constructors

| Name | Description |
| --- | --- |
| [GpuBuffer(Int32)](#gpubufferint32) | Allocates a new device buffer capable of holding `length` elements. |
| [GpuBuffer(IntPtr)](#gpubufferintptr) | Initializes a new instance of the `GpuBuffer<T>` class with the specified byte size. |

## GpuBuffer(Int32)

Allocates a new device buffer capable of holding `length` elements.

```csharp
public GpuBuffer(int length);
```

#### Parameters

`length` _Int32_<br>
Number of elements.

#### Exceptions

`ArgumentOutOfRangeException`<br>
`length` is negative.

`OverflowException`<br>
`length * sizeof(T)` overflows `nint`.

`CudaException`<br>
The CUDA driver failed to query the current context or allocate device memory.

## GpuBuffer(IntPtr)

Initializes a new instance of the `GpuBuffer<T>` class with the specified byte size.

```csharp
public GpuBuffer(IntPtr byteSize);
```

#### Parameters

`byteSize` _IntPtr_<br>
The raw byte size of the buffer.

## Properties

| Name | Description |
| --- | --- |
| [DevicePointer](#devicepointer) | Gets the raw CUDA device pointer to the allocated memory. |
| [Length](#length) | Gets the number of elements this buffer can hold. |

## DevicePointer

Gets the raw CUDA device pointer to the allocated memory.

```csharp
public IntPtr DevicePointer { get; }
```

## Length

Gets the number of elements this buffer can hold.

```csharp
public int Length { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [CopyToDevice(ReadOnlySpan\<T\>)](#copytodevicereadonlyspant) | Synchronously copies data from host memory to this device buffer. |
| [CopyToHost(Span\<T\>)](#copytohostspant) | Synchronously copies data from this device buffer to host memory. |
| [Dispose()](#dispose) | Releases the allocated device (or fallback unmanaged host) memory. |

## CopyToDevice(ReadOnlySpan\<T\>)

Synchronously copies data from host memory to this device buffer.

```csharp
public void CopyToDevice(ReadOnlySpan<T> source);
```

#### Parameters

`source` _ReadOnlySpan\<T\>_<br>
The host memory span to copy from.

#### Exceptions

`ObjectDisposedException`<br>
The buffer has been disposed.

`ArgumentException`<br>
`source.Length` is greater than `Length`.

`CudaException`<br>
The host-to-device memory transfer or stream synchronization failed.

> [!IMPORTANT]
> **Ordering guarantee:** `CopyToDevice` stages pageable host memory through a page-locked buffer on a non-blocking stream and synchronizes that stream before returning. Once `CopyToDevice` returns, the uploaded data is guaranteed to be resident in device memory and visible to any subsequent work on the same CUDA context, including kernels and transfers enqueued on non-blocking streams (`CU_STREAM_NON_BLOCKING`).

## CopyToHost(Span\<T\>)

Synchronously copies data from this device buffer to host memory.

```csharp
public void CopyToHost(Span<T> destination);
```

#### Parameters

`destination` _Span\<T\>_<br>
The host memory span to copy to.

#### Exceptions

`ObjectDisposedException`<br>
The buffer has been disposed.

`ArgumentException`<br>
`destination.Length` is greater than `Length`.

`CudaException`<br>
The synchronous device-to-host memory transfer (`cuMemcpyDtoH_v2`) failed.

## Dispose()

Releases the allocated device (or fallback unmanaged host) memory.

```csharp
public void Dispose();
```

#### Exceptions

`CudaException`<br>
The CUDA driver failed to free the device allocation (`cuMemFree_v2`).

## Remarks

`GpuBuffer<T>` captures the current CUDA context (`cuCtxGetCurrent`) at allocation time and automatically pushes/pops that owning context during `CopyToDevice`, `CopyToHost`, and `Dispose`. When [GpuKernelLoader.IsCpuFallbackMode](GpuKernelLoader.md#iscpufallbackmode) is active, `GpuBuffer<T>` transparently allocates unmanaged host memory via `Marshal.AllocHGlobal` and performs copies via `Buffer.MemoryCopy`.

## Examples

### Allocating and Transferring Device Memory

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Runtime

```csharp
using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;

// Execution Context: Host (CPU)
using (new GpuKernelLoader.CudaContextScope())
{
    using var buffer = new GpuBuffer<int>(4);
    ReadOnlySpan<int> input = [10, 20, 30, 40];
    buffer.CopyToDevice(input);

    Span<int> output = stackalloc int[4];
    buffer.CopyToHost(output);
    Console.WriteLine($"Buffer[2]: {output[2]}");
}
```

## See also

### BCL API > Host Runtime

- [Host Runtime](00_index.md)
- [CudaHostTransfer Class](CudaHostTransfer.md)
- [GpuKernelLoader Class](GpuKernelLoader.md)
