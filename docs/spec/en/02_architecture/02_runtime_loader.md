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

The runtime initializes the driver once per process, on the first use or query (`cuInit`, `cuDriverGetVersion`, then the global context on device 0), and keeps the result. Without a driver or a device, or when a driver call fails, it switches to the CPU fallback mode. A driver that supports a CUDA version older than 13.0 (`cuDriverGetVersion` below 13000) is not a fallback case; see [Prerequisites](../00_getting_started/01_prerequisites.md#1-gpu-execution-default--recommended):

- The queries do not throw: `IsDriverAvailable` and `IsCpuFallbackMode` are `false`, and `DriverUnavailableReason` holds the message (the CUDA version the driver supports and the required one; for the fallback cases, the failed call or the missing library).
- GPU use throws a `NotSupportedException` with that message: `EnsureInitialized`, `GlobalContext`, a `CudaContextScope` and so every launch, transfer, stream completion and event timer.
- `ForceCpuFallback` still runs on the CPU and does not touch the driver. The bindings of `cuda_driver_api.yml` therefore bind the versioned symbols of the CUDA 13 `cuda.h` (`cuEventElapsedTime_v2`, `cuMemAllocHost_v2`, ...) without a fallback for older drivers.

## 2.2 AOT Module Loading and Validation

G# eliminates all runtime compilation overhead by requiring all C# kernels to be compiled into native NVIDIA Fatbins during the MSBuild AOT phase. The runtime operates exclusively as a secure, lightweight module loader, with zero dependencies on local toolchains.

### 2.2.1 GPU AOT Kernel Loading (`GpuKernelLoader.LoadAotKernel`)

The `LoadAotKernel` method is responsible for validating and loading the precompiled AOT binary directly into the CUDA driver. It ensures that the binary matches the expected compilation output and target hardware:

1. **Ed25519 Digital Signature Verification (`AotSignatureValidator`)**: Every valid AOT binary is signed during the build phase with a trailing 64-byte Ed25519 digital signature. The runtime extracts the signature and verifies the payload against pinned trusted public keys (`GpuSecurityPolicy.EmbeddedKeys`) to prevent tampering or unauthorized binary injection.
2. **Compute Capability Verification (`FatbinParser`)**: Before invoking the CUDA driver, the runtime parses the Fatbin container header to verify that it contains at least one CUBIN or PTX image compatible with the active GPU's compute capability (`sm_XX`). A CUBIN is compatible with devices of the same major version and an equal or newer minor version; a PTX image with any device of an equal or newer compute capability (the driver JIT-compiles it). The check accepts everything the driver may accept; when the driver still refuses the module (`CUDA_ERROR_NO_BINARY_FOR_GPU`, for example an architecture-specific `sm_90a` CUBIN, or a desktop CUBIN on a Tegra 8.7 GPU), the loader throws the same `InvalidOperationException`, which names the device and lists the images the Fatbin holds. When the driver is older than the CUDA Toolkit that wrote the PTX and cannot JIT-compile it (`CUDA_ERROR_UNSUPPORTED_PTX_VERSION`), the loader throws an `InvalidOperationException` that names this cause and the two fixes: update the driver, or add the GPU's architecture to `GSharpTargetArchitectures` so that the Fatbin carries native code for it.

### 2.2.2 CPU AOT Kernel Loading (`CpuKernelLoader.LoadAotKernel`)

When a kernel is compiled using the `CpuDialect` (Standard C++ fallback), it generates standard dynamic libraries (`.dll` or `.so`) rather than CUDA Fatbins.
The `CpuKernelLoader` serves as the runtime equivalent for CPU execution:

- It reads the compiled host library and performs the identical **64-byte Ed25519 Digital Signature Verification** via `AotSignatureValidator.ExtractAndVerify` against `GpuSecurityPolicy.EmbeddedKeys`.
- Upon successful verification, it loads the library into the host process via standard OS APIs (`NativeLibrary.Load`) and uses `NativeLibrary.TryGetExport` to retrieve the exported kernel symbol.
- It then uses `Marshal.GetDelegateForFunctionPointer<TDelegate>` to return a strongly-typed managed delegate, allowing the generated C# stub to directly invoke the native CPU code with the exact same API signature as the GPU launcher, making the CPU fallback execution entirely transparent to the caller.

### 2.2.3 Caching and Memory Management

- **Concurrent Dictionary**: Once validated and loaded (either via `cuModuleLoadDataEx` or `NativeLibrary.Load`), the resulting `CUfunction` or `Delegate` handles are cached in a static `ConcurrentDictionary`.
- **Zero-Allocation Cache Keys**: To avoid string allocations during high-frequency kernel dispatches, `GpuKernelLoader` utilizes a custom `Hash256` value type (a 32-byte struct extracted from the signature) combined with the kernel name as the composite key. Subsequent calls instantly retrieve the cached handle in $O(1)$ time with zero GC pressure.
- **One Module per Context**: A CUDA module and its functions belong to the context they were loaded in, so the GPU caches are keyed by the context as well. A launch loads the module into the context that is current for it (the context of the innermost `CudaContextScope`, such as the one of an isolated `GpuWorkspace`, otherwise the global context) the first time it runs there. Each generated launcher keeps the last function it resolved together with a number that identifies its context; a launch in the same context reuses it without a driver call, and a launch in another context resolves the function of that context. `GpuKernelLoader.DestroyContext` drops the modules of the destroyed context; a context created later gets a new number even when the driver reuses the handle, so nothing of the destroyed context is launched again.
- **Graceful Shutdown**: Developers can explicitly call `GpuKernelLoader.Shutdown()` or `CpuKernelLoader.Shutdown()` to unload all cached modules (`cuModuleUnload` / `NativeLibrary.Free`), preventing resource leaks when kernels are no longer needed. A kernel launched after `GpuKernelLoader.Shutdown()` loads its module again: the generated launcher keeps its function together with the loader's module generation, which `Shutdown` advances. `Shutdown` must not run while another thread launches a kernel.

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

## 2.6 Device Exception Channel (`DeviceErrorSlot`)

G# surfaces GPU device traps, hardware assertions, and sequence errors as standard .NET exceptions (such as `ArgumentOutOfRangeException`, `IndexOutOfRangeException`, and `InvalidOperationException`) carrying exact source locations and 3D thread/block coordinates (#729).

### 2.6.1 Process-Wide Single Error Slot Architecture

A single pinned, host-mapped memory slot (`GSharpDeviceErrorSlot`, 24 bytes) is shared process-wide across all CUDA modules, streams, and execution contexts.

- **Warp Leader Election**: When a device trap occurs on any thread, the warp elects a single leader via active mask ballot. The leader executes an atomic CAS on the slot's error code to claim first-writer status.
- **Coordinate and Module Recording**: The winning leader records the error site ID (a 31-bit hash of the file name, the start and end line and column, and the trap kind), the module ID, the packed 3D thread coordinates, and the direct 3D block coordinates (`blockIdx.x` and packed `blockIdx.y | (blockIdx.z << 16)`), executing double system memory fences before and after setting the ready bit.
- **Host Consumption and Context Invalidation**: When the host synchronizes or driver error handling detects a failure (`CudaDriverApi.ThrowIfError`, stream completion, or explicit `ThrowIfDeviceError`), the slot is atomically consumed and reset. Because a device trap fundamentally invalidates the underlying CUDA context, all concurrent and subsequent operations on that context will fail; whichever host check runs first consumes and reports the rich error details.
- **Module Loading Decoupling**: Loading a module (`GetOrLoadModule`) intentionally does not check or reset the error slot, preventing an unrelated module load from consuming an in-flight kernel's trap details.

### 2.6.2 Known Limits of the CUDA Error Records

A launch through a generated launcher records its error under its own launch epoch (`LaunchErrorRecords`), and its launcher throws it. A launch captured into a CUDA graph keeps its arguments in the graph and runs again at every replay, so it cannot own a record: it records into the shared slot above, which the synchronization after `cuGraphLaunch` reads.

A captured launch of a kernel that waits at a barrier counting a fixed number of arrivals (a cooperative kernel, or one that reaches an mbarrier) takes a capture epoch (bit 31 set) and, before the kernel, launches on the capturing stream a one-thread kernel of the module (`__gsharp_clear_capture_abort`) that clears its abort word in the module's `__gsharp_capture_abort` array. The graph runs that node before the kernel at every replay, so the waiters of a replay whose thread failed leave the barrier as for a launch that is not captured, and a later replay starts with a clear word. The node costs about 0.8 us per replay (a 4-byte memset node cost 7 to 15 us, RTX 3070 Ti, driver 621.14). A captured launch of any other kernel has epoch 0 and adds no node.

A block that leaves a grid barrier after a failure takes its arrival back while the barrier has not completed. On driver 621.14 a graph keeps the grid workspace of a cooperative kernel node from one replay to the next, so an arrival left in a barrier that never completed would let blocks of the next replay pass their barriers early. NVIDIA does not document either behavior; the take-back is also correct when a driver clears the workspace for every replay.

Several captured launches that fail before a synchronization (two failing nodes of one graph, or replays of different graphs) all write the shared slot: the first error stays, the later failing launches only leave, and the synchronization throws the first error. The same holds for a launch that is not captured but records into the shared slot (below).

Known limits of captured launches:

- Their error can be thrown by the synchronization of any stream, not only the one the graph was launched on.
- When several captured launches fail before a synchronization, only the first error is thrown; the others are lost.
- Capture epochs are never released (the runtime does not see when a graph is destroyed). Two live graphs of the same module whose capture epochs are a multiple of 4096 apart share a word: a replay of one that runs at the same time as a failed replay of the other can clear the other's word, and the other's waiters keep waiting until the driver resets the GPU (TDR). Two executable graphs instantiated from one captured graph share their epochs: when they run at the same time, the failure of one makes the other leave its barriers, and as only one synchronization throws the error, the other instance may finish with partial results without an exception. G# itself instantiates every graph it captures once.

Further limits:

- When the per-launch records could not be allocated, or 256 launches whose launchers read their own record are in flight at once, the next launch takes an epoch of the shared range (bit 31 set) and records into the shared slot like a captured launch: it keeps its own abort word, its launcher throws any recorded error, and when several such launches fail before a synchronization, only the first error is thrown.
- A launch whose own record is still held by an unconsumed error of the launch 256 epochs before it records into the shared slot; when that is held by another launch as well, the thread traps (a GPU fault that ends the context) rather than let its launcher return partial results with no exception.
- Once every 2^31 launches the epoch counter wraps around and the host clears the abort words of every loaded module. The clear is not ordered with launches on non-blocking streams: a launch that failed and is still running at that moment can lose its abort word, and its grid barrier or mbarrier waiters keep waiting until the driver resets the GPU (TDR).

### 2.6.3 Native CPU Consistency

The native CPU execution path (`CpuKernelLoader`) mirrors this architecture using thread-local `GSharpErrorSlot` instances exporting `__gsharp_consume_last_error_details`. The runtime unpacks thread and block coordinates with identical layout decoding, producing uniform exception types, messages, and `Exception.Data` key-value pairs across CUDA GPU and native CPU targets.

### 2.6.4 Kernel Pool Growth After GPU Faults on Windows

A failing G# thread records its error and exits, so an ordinary device error (an index out of range, a failed switch expression) does not fault the GPU and costs nothing beyond the exception. A real GPU fault is different: a `trap` that ends the CUDA context (the cases listed in 2.6.2, a launch without error records) or an illegal memory access (a kernel whose checks a release build leaves out). On Windows each such fault logs `nvlddmkm` Event 153 entries in the System event log (one for a plain CUDA program, two for a G# test process), resets the GPU engine and writes a live kernel dump.

Measured on one Windows 11 machine with the NVIDIA driver 610.88 (WDDM):

- A plain CUDA program whose kernel ends in `trap;`, run 200 times right after a reboot, grew the nonpaged pool by 474 MB and the paged pool by 1,098 MB. The growth was uneven from one group of runs to the next, and the pools were not freed when the processes exited, only by a reboot.
- Over nine days of test runs (about 10,600 Event 153 entries), the pools reached 8.3 GB nonpaged and 11.1 GB paged; a reboot returned them to 1.8 GB and 2.1 GB.

Whether the driver or the live kernel dump that Windows writes for each fault holds the memory is not known yet, and faults on an illegal address were not measured separately. The question is reported to NVIDIA as bug 7077284 (https://developer.nvidia.com/bugs/7077284 , which needs an NVIDIA developer login).

- Watch `\Memory\Pool Nonpaged Bytes` and `\Memory\Pool Paged Bytes` (Performance Monitor, or `typeperf`) when a workload may fault repeatedly, and reboot when they have grown.
- Do not retry a faulting kernel in a loop; fix the fault, or run the kernel through a launcher whose error records are available so that a failing thread exits instead of faulting.
- Only Windows was measured; Linux has not been measured.

## See also

### Getting Started

- [0. Getting Started with G# (GSharp)](../00_getting_started/00_index.md)
- [1. Prerequisites & Installation](../00_getting_started/01_prerequisites.md)

### Architecture

- [2. Compiler and Runtime Architecture](00_index.md)
- [8. Comprehensive Example](03_comprehensive_example.md)
