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
public static class GpuKernelLoader
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

The global context is the primary context of device 0 (`cuDevicePrimaryCtxRetain`), retained until the process ends. It is the context the CUDA Runtime and the libraries built on it (cuBLAS, cuFFT) use, so their streams and events can be passed to G# directly (device memory could already be shared across contexts of the same device). A library that resets the device (`cudaDeviceReset`, `cuDevicePrimaryCtxReset`) destroys it as well, with the device memory, modules and page-locked memory of G#. G# records the context ID (`cuCtxGetId`) when it initializes the context and checks it whenever it makes the global context current; after a reset every such call throws `InvalidOperationException`, also once the CUDA Runtime has initialized the primary context again under the same handle.

A potentially unsafe driver call (an allocation, a free, an event query) made while a stream captures in `CUstreamCaptureMode.Global` or `CUstreamCaptureMode.ThreadLocal` fails in this context with `CudaException` (`CudaErrorStreamCaptureUnsupported`) and invalidates the capture. In a context created with `cuCtxCreate`, such as the one of an isolated `GpuWorkspace`, the driver currently ends the process with an access violation instead (NVIDIA bug 7081224, driver 621.14 on Windows).

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
| [Shutdown()](#shutdown) | Unloads all cached GPU modules and clears the module cache. A later launch loads its module again. |
| [DestroyContext(IntPtr)](#destroycontextintptr) | Faults every pending stream wait on a context, then destroys the context with `cuCtxDestroy`. |
| [RegisterErrorSites(ReadOnlySpan\<byte\>, ReadOnlySpan\<GSharpErrorSite\>)](#registererrorsitesreadonlyspanbyte-readonlyspangsharperrorsite) | Registers the error site table of an AOT Fatbin. |
| [ThrowIfDeviceError(Exception)](#throwifdeviceerrorexception) | Consumes any recorded device error and throws the corresponding .NET exception. |
| [CreateExceptionForDriverError(CUresult, string)](#createexceptionfordrivererrorcuresult-string) | Creates the exception for a failed driver call, taking a recorded device error into account. |

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

Unloads all cached GPU modules and clears the module cache. A later launch loads its module again.

```csharp
public static void Shutdown();
```

Do not call it while another thread launches a kernel or loads a module: such a launch can still use a function of a
module that is being unloaded.

## DestroyContext(IntPtr)

Faults every pending stream wait on `context`, then destroys the context with `cuCtxDestroy`. Destroy a context created with `cuCtxCreate` through this method rather than calling `cuCtxDestroy` directly: a wait of `CudaStreamCompletion.WaitAsync` that is still pending on the context would otherwise use a destroyed handle.

```csharp
public static void DestroyContext(IntPtr context);
```

#### Parameters

`context` _IntPtr_<br>
A context created with `cuCtxCreate`. It must not be used after this call.

#### Exceptions

[CudaException](../01_diagnostics_and_exceptions.md#cudaexception)<br>
`cuCtxDestroy` failed.

`TimeoutException`<br>
A watchdog tick did not leave the driver in time; the context was not destroyed.

The awaiters of the faulted waits observe an `ObjectDisposedException`. The GPU work they were waiting for is not canceled; it is drained or torn down together with the context.

<a id="registererrorsitesreadonlyspanbyte-readonlyspangsharperrorsite"></a>

## RegisterErrorSites(ReadOnlySpan\<byte\>, ReadOnlySpan\<GSharpErrorSite\>)

Registers the error site table of an AOT Fatbin, so that a device error recorded by its kernels is reported with its source location. The generated launchers call it; user code does not need to.

```csharp
public static void RegisterErrorSites(ReadOnlySpan<byte> aotFatbin, ReadOnlySpan<GSharpErrorSite> sites);
```

#### Parameters

`aotFatbin` _ReadOnlySpan\<byte\>_<br>
The raw bytes of the AOT Fatbin binary.

`sites` _ReadOnlySpan\<GSharpErrorSite\>_<br>
The 1-based error site table.

## ThrowIfDeviceError(Exception)

Consumes any recorded device error and throws the corresponding .NET exception (for example `IndexOutOfRangeException` or `DivideByZeroException`).

```csharp
public static void ThrowIfDeviceError(Exception? innerException = null);
```

#### Parameters

`innerException` _Exception?_<br>
An optional inner exception, such as the `CudaException` of the driver call that reported the failure.

It consumes the error of any launch whose launcher is not about to read it itself, also of a launch on another stream that has not been synchronized yet; a generated launcher consumes only the error of its own launch.

## CreateExceptionForDriverError(CUresult, string)

Creates the exception for a driver call that returned a non-success `CUresult`. When the result reports a fault of a running kernel and a device error was recorded, it returns the corresponding .NET exception with a `CudaException` as its inner exception; otherwise it returns a `CudaException`.

```csharp
public static Exception CreateExceptionForDriverError(CUresult result, string? fallbackMessage = null);
```

#### Parameters

`result` _CUresult_<br>
The non-success result of the driver call.

`fallbackMessage` _string?_<br>
An optional message for the `CudaException`.

#### Returns

_Exception_<br>
The exception to throw.

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
- [CudaStream Class (G#)](CudaStream.md)
