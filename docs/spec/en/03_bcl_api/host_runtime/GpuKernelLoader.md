---
title: "GpuKernelLoader Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Manages CUDA driver initialization, thread-scoped CUDA contexts, and verified AOT Fatbin module loading."
---

## GpuKernelLoader Class

## Definition

**Namespace:** `Kassyi.GSharp.Runtime.Aot`  
**Assembly:** `Kassyi.GSharp.Runtime.dll`

Runtime AOT loader engine that manages thread contexts, CPU fallback detection, and verified GPU Fatbin module loading.

```csharp
public sealed class GpuKernelLoader
```

## Properties

| Name | Description |
| --- | --- |
| [GlobalContext](#globalcontext) | Gets the global CUDA context handle (`CUcontext`). |
| [ForceCpuFallback](#forcecpufallback) | Gets or sets a value indicating whether to forcibly emulate the GPU runtime on CPU host memory. |
| [IsCpuFallbackMode](#iscpufallbackmode) | Gets a value indicating whether the runtime is operating in CPU host fallback mode. |
| [IsDriverAvailable](#isdriveravailable) | Gets a value indicating whether the CUDA driver is available and initialized. |

## GlobalContext

Gets the global CUDA context handle, initializing the driver if necessary.

```csharp
public static IntPtr GlobalContext { get; }
```

## ForceCpuFallback

Gets or sets a value indicating whether to forcibly emulate the GPU runtime on CPU host memory (stored in an `AsyncLocal<bool>`).

```csharp
public static bool ForceCpuFallback { get; set; }
```

## IsCpuFallbackMode

Gets a value indicating whether the runtime is operating in CPU host fallback mode (`ForceCpuFallback` is `true` or no usable CUDA driver/device was found).

```csharp
public static bool IsCpuFallbackMode { get; }
```

## IsDriverAvailable

Gets a value indicating whether the CUDA driver is available and initialized.

```csharp
public static bool IsDriverAvailable { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [EnsureInitialized()](#ensureinitialized) | Ensures that the global CUDA driver and primary context are initialized in a thread-safe manner. |
| [LoadAotKernel(ReadOnlySpan\<byte\>, string)](#loadaotkernelreadonlyspanbyte-string) | Validates and loads an AOT-compiled Fatbin into the CUDA driver and returns the kernel function handle. |
| [LoadModule(byte[])](#loadmodulebyte) | Validates and loads a Fatbin module into the CUDA driver and returns the module handle (`CUmodule`). |
| [GetFunction(IntPtr, string)](#getfunctionintptr-string) | Retrieves a function handle (`CUfunction`) from a loaded CUDA module. |
| [GetKernelAttributes(IntPtr)](#getkernelattributesintptr) | Queries the hardware resource attributes (`KernelResourceAttributes`) of a compiled kernel function. |
| [SynchronizeDevice()](#synchronizedevice) | Blocks until the device has completed all preceding requested tasks in the current context. |
| [Shutdown()](#shutdown) | Unloads all cached GPU modules and clears the module cache. |

## EnsureInitialized()

Ensures that the global CUDA driver and primary context are initialized in a thread-safe manner. Falls back to CPU emulation mode if the CUDA driver or hardware is unavailable.

```csharp
public static void EnsureInitialized();
```

## LoadAotKernel(ReadOnlySpan\<byte\>, string)

Loads an Ahead-Of-Time (AOT) compiled Fatbin directly into the CUDA driver after verifying its Ed25519 digital signature and target compute capability.

```csharp
public static IntPtr LoadAotKernel(ReadOnlySpan<byte> aotFatbin, string kernelName);
```

#### Parameters

`aotFatbin` _ReadOnlySpan\<byte\>_<br>
The raw byte span of the signed AOT Fatbin binary.

`kernelName` _string_<br>
The name of the kernel entry point to load.

#### Returns

_IntPtr_<br>
A native handle (`IntPtr`) to the loaded CUDA kernel function (`CUfunction`).

#### Exceptions

`InvalidOperationException`<br>
Thrown if the binary signature is invalid or the Fatbin does not contain any PTX or CUBIN compatible with the current GPU's compute capability.

`CudaException`<br>
Thrown if `cuModuleLoadDataEx` or `cuModuleGetFunction` fails.

## LoadModule(byte[])

Loads a signed Fatbin module directly into the CUDA driver and returns the module handle.

```csharp
public static IntPtr LoadModule(byte[] binaryData);
```

#### Parameters

`binaryData` _byte[]_<br>
The raw byte array containing the signed Fatbin module binary.

#### Returns

_IntPtr_<br>
A native handle (`IntPtr`) to the loaded CUDA module (`CUmodule`).

#### Exceptions

`InvalidOperationException`<br>
Thrown if signature verification fails or the Fatbin is incompatible with the current GPU.

`CudaException`<br>
Thrown if `cuModuleLoadDataEx` fails.

## GetFunction(IntPtr, string)

Retrieves a function pointer from a loaded CUDA module.

```csharp
public static IntPtr GetFunction(IntPtr hModule, string kernelName);
```

#### Parameters

`hModule` _IntPtr_<br>
The handle to the loaded CUDA module (`CUmodule`).

`kernelName` _string_<br>
The entry point function name to retrieve.

#### Returns

_IntPtr_<br>
A native handle (`IntPtr`) to the CUDA kernel function (`CUfunction`).

## GetKernelAttributes(IntPtr)

Queries the hardware resource attributes of a compiled kernel function from the CUDA driver.

```csharp
public readonly record struct KernelResourceAttributes(
    int NumRegs,
    int LocalSizeBytes,
    int SharedSizeBytes,
    int ConstSizeBytes);

public static KernelResourceAttributes GetKernelAttributes(IntPtr hfunc);
```

#### Parameters

`hfunc` _IntPtr_<br>
The native handle to the CUDA kernel function (`CUfunction`).

#### Returns

_GpuKernelLoader.KernelResourceAttributes_<br>
A `KernelResourceAttributes` struct containing hardware register and memory resource metrics.

#### Exceptions

`ArgumentException`<br>
Thrown if `hfunc` is `IntPtr.Zero`.

## SynchronizeDevice()

Blocks until the device has completed all preceding requested tasks.

```csharp
public static void SynchronizeDevice();
```

## Shutdown()

Unloads all cached GPU modules and clears the module cache.

```csharp
public static void Shutdown();
```

## Examples

### Managing Context Scopes and Fallback Mode

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Runtime

```csharp
using System;
using Kassyi.GSharp.Runtime.Aot;

// Execution Context: Host (CPU)

GpuKernelLoader.ForceCpuFallback = true;
using (new GpuKernelLoader.CudaContextScope())
{
    Console.WriteLine($"IsCpuFallbackMode: {GpuKernelLoader.IsCpuFallbackMode}");
}
GpuKernelLoader.ForceCpuFallback = false;
```

## Remarks

### `GpuKernelLoader.CudaContextScope`

```csharp
public readonly struct CudaContextScope : IDisposable
{
    public CudaContextScope();
    public CudaContextScope(IntPtr customContext);
    public void Dispose();
}
```

`CudaContextScope` is a scoped RAII structure that activates a CUDA context on the current thread (`cuCtxSetCurrent`) and restores the previous context upon disposal. When `IsCpuFallbackMode` is active, it operates as a zero-overhead no-op.

## See also

### BCL API > Host Runtime

- [Host Runtime](00_index.md)
- [CpuKernelLoader Class (G#)](CpuKernelLoader.md)
- [ConstantArgument Struct (G#)](ConstantArgument.md)
- [CudaStream Class (G#)](CudaStream.md)
