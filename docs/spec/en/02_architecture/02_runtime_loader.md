---
title: "2. Runtime Execution and AOT Loader Pipeline (`Kassyi.GSharp.Runtime`)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "This document defines the internal architecture of the runtime loader, context management, and the native hardware execution pipeline for AOT-compi..."
---

# 2. Runtime Execution and AOT Loader Pipeline (`Kassyi.GSharp.Runtime`)

This document defines the internal architecture of the runtime loader, context management, and the native hardware execution pipeline for AOT-compiled kernels.

## 2.1 Context and Thread Management (`GpuKernelLoader`)

_(Note: `GpuKernelLoader` is maintained for legacy API surface compatibility, but now functions strictly as a lightweight AOT loader.)_

The runtime engine utilizes a robust scope-based context management lifecycle to ensure safe multi-threaded execution without relying on global state.

Instead of a singleton context tracked via `[ThreadStatic]`, the AOT generated launcher stub automatically emits a local scope instance (`using var _cudaCtxScope = new CudaContextScope();`) at the beginning of each kernel invocation. This `CudaContextScope` pushes the active CUDA context to the calling thread (`cuCtxPushCurrent`) and safely pops it upon disposal (`cuCtxPopCurrent`), natively supporting parallel kernel dispatches from multiple host threads.

## 2.2 AOT Module Loading and Validation

G# eliminates all runtime compilation overhead by requiring all C# kernels to be compiled into native NVIDIA Fatbins during the MSBuild AOT phase. The runtime operates exclusively as a secure, lightweight module loader, with zero dependencies on local toolchains.

### 2.2.1 GPU AOT Kernel Loading (`GpuKernelLoader.LoadAotKernel`)

The `LoadAotKernel` method is responsible for validating and loading the precompiled AOT binary directly into the CUDA driver. It ensures that the binary matches the expected compilation output and target hardware:

1. **Ed25519 Digital Signature Verification (`AotSignatureValidator`)**: Every valid AOT binary is signed during the build phase with a trailing 64-byte Ed25519 digital signature. The runtime extracts the signature and verifies the payload against pinned trusted public keys (`GpuSecurityPolicy.EmbeddedKeys`) to prevent tampering or unauthorized binary injection.
2. **Compute Capability Verification (`FatbinParser`)**: Before invoking the CUDA driver, the runtime parses the Fatbin container header to verify that it contains at least one CUBIN or PTX image compatible with the active GPU's compute capability (`sm_XX`).

### 2.2.2 CPU AOT Kernel Loading (`CpuKernelLoader.LoadAotKernel`)

When a kernel is compiled using the `CpuDialect` (Standard C++ fallback), it generates standard dynamic libraries (`.dll` or `.so`) rather than CUDA Fatbins.
The `CpuKernelLoader` serves as the runtime equivalent for CPU execution:

- It reads the compiled host library and performs the identical **64-byte Ed25519 Digital Signature Verification** via `AotSignatureValidator.ExtractAndVerify` against `GpuSecurityPolicy.EmbeddedKeys`.
- Upon successful verification, it loads the library into the host process via standard OS APIs (`NativeLibrary.Load`) and uses `NativeLibrary.TryGetExport` to retrieve the exported kernel symbol.
- It then uses `Marshal.GetDelegateForFunctionPointer<TDelegate>` to return a strongly-typed managed delegate, allowing the generated C# stub to directly invoke the native CPU code with the exact same API signature as the GPU launcher, making the CPU fallback execution entirely transparent to the caller.

### 2.2.3 Caching and Memory Management

- **Concurrent Dictionary**: Once validated and loaded (either via `cuModuleLoadDataEx` or `NativeLibrary.Load`), the resulting `CUfunction` or `Delegate` handles are cached in a static `ConcurrentDictionary`.
- **Zero-Allocation Cache Keys**: To avoid string allocations during high-frequency kernel dispatches, `GpuKernelLoader` utilizes a custom `Hash256` value type (a 32-byte struct extracted from the signature) combined with the kernel name as the composite key. Subsequent calls instantly retrieve the cached handle in $O(1)$ time with zero GC pressure.
- **Graceful Shutdown**: Developers can explicitly call `GpuKernelLoader.Shutdown()` or `CpuKernelLoader.Shutdown()` to unload all cached modules (`cuModuleUnload` / `NativeLibrary.Free`), preventing resource leaks when kernels are no longer needed.

## 2.3 Execution and Marshaling (`KernelLauncher`)

Upon successful module loading, the runtime yields a `CUfunction` handle (or a CPU Delegate). The generated source produces two distinct launcher stubs to handle different marshaling requirements:

### 2.3.1 Host Launcher (`LaunchKernelName`)

This stub accepts standard C# managed arrays (e.g., `T[]`, custom structs).

1. **Deep Struct Marshaling**: If a parameter is a custom struct containing nested arrays, it invokes `Marshal_T` to perform deep traversals. It creates temporary shadow structures (`__Native_T`), allocates device memory, and copies host data to the device (unless optimized out by `[Out]`).
2. **Grid and Block Dimensions**: The host logic determines the `gridDim` and `blockDim` parameters based on the workload size.
3. **Dispatch**: Constructs the `IntPtr` argument array and invokes `cuLaunchKernel` (or the CPU delegate). After execution, it invokes `Unmarshal_T` to copy results back to the managed arrays and cleans up handles.

### 2.3.2 Device Launcher (`LaunchKernelName_Device`)

This stub accepts only raw pointers (`T*`) or device abstractions (e.g., `GpuBuffer<T>`).

1. **Zero-Allocation**: It completely bypasses all managed array (`T[]`) pinning, deep struct marshaling, and memory allocation logic. **Passing structs with managed arrays to a `_Device` launcher is explicitly prohibited and will cause a compilation error.**
2. **Grid and Block Dimensions**: Determines `gridDim` and `blockDim`.
3. **Dispatch**: Immediately constructs the `IntPtr` argument array and invokes `cuLaunchKernel`.

## 2.4 Telemetry and Logging Architecture

The diagnostic logging system (`Kassyi.GSharp.Core.Logger`) writes synchronously to standard output (`Console.WriteLine` / `Console.Error.WriteLine`), delegating actual I/O buffering and persistence responsibility to the console host. This design eliminates background queue contention and prevents log truncation or dropped entries under extreme parallel test and kernel execution workloads.

- **Scoped Console Suppression**: `Logger.WriteToConsole` is backed by `AsyncLocal<bool>`, allowing individual asynchronous test flows to suppress console output without affecting concurrent workers.
- **Interval Accumulation**: `Logger.PerformanceTimer` and `Logger.MeasureScope` record elapsed stage timings into a thread-safe `ConcurrentDictionary<string, long>`.

## 2.5 Runtime Utilities

### 2.5.1 `GpuBuffer<T>` (Native Memory Wrapper)

G# introduces the `GpuBuffer<T>` abstraction in the Runtime space to provide a safe, strongly-typed unmanaged memory wrapper over raw CUDA device pointers. It enables zero-allocation device memory management and seamless Host-to-Device/Device-to-Host transfers.

```csharp
// Execution Context: Host (CPU)
// Safely allocate and copy memory without raw P/Invoke overhead
using var buffer = new GpuBuffer<int>(1024);

// Synchronously copies data from Host (ReadOnlySpan<T>) to Device
buffer.CopyFromHost(hostSpan);

// Access the raw CUdeviceptr for kernel invocation
IntPtr devPtr = buffer.DevicePointer;
```

### 2.5.2 CUDA Event Timers (`CudaEventTimer`)

The runtime provides `CudaEventTimer`, an abstraction over native CUDA Driver Events (`cuEventCreate`, `cuEventRecord`, `cuEventElapsedTime`) to allow highly precise, asynchronous GPU throughput measurements without stalling the host pipeline.

```csharp
// Execution Context: Host (CPU)
using var timer = new CudaEventTimer();

timer.Start();
// Launch device kernel asynchronously
LaunchKernelName(args);
timer.Stop();

Console.WriteLine($"Kernel executed in: {timer.ElapsedMilliseconds()} ms");
```

## See also

### Getting Started

- [0. Getting Started with G# (GSharp)](../00_getting_started/00_index.md)
- [1. Prerequisites & Installation](../00_getting_started/01_prerequisites.md)

### Architecture

- [2. Compiler and Runtime Architecture](00_index.md)
- [8. Comprehensive Example](03_comprehensive_example.md)
