# 2. Runtime Execution and AOT Loader Pipeline (`Kassyi.GSharp.Runtime`)

This document defines the internal architecture of the runtime loader, context management, and the native hardware execution pipeline for AOT-compiled kernels.

## 2.1 Context and Thread Management (`GpuKernelLoader`)

*(Note: `GpuKernelLoader` is maintained for legacy API surface compatibility, but now functions strictly as a lightweight AOT loader.)*

The runtime engine utilizes a robust scope-based context management lifecycle to ensure safe multi-threaded execution without relying on global state. 

Instead of a singleton context tracked via `[ThreadStatic]`, the AOT generated launcher stub automatically emits a local scope instance (`using var _cudaCtxScope = new CudaContextScope();`) at the beginning of each kernel invocation. This `CudaContextScope` pushes the active CUDA context to the calling thread (`cuCtxPushCurrent`) and safely pops it upon disposal (`cuCtxPopCurrent`), natively supporting parallel kernel dispatches from multiple host threads.

## 2.2 AOT Module Loading and Validation

G# eliminates all runtime compilation overhead by requiring all C# kernels to be compiled into native NVIDIA Fatbins during the MSBuild AOT phase. The runtime operates exclusively as a secure, lightweight module loader, with zero dependencies on local toolchains.

### 2.2.1 AOT Kernel Loading (`LoadAotKernel`)

The `LoadAotKernel` method is responsible for validating and loading the precompiled AOT binary directly into the CUDA driver. It ensures that the binary matches the expected compilation output.

1. **Trailing Hash Validation**: Every valid AOT binary is embedded with a 32-byte trailing hash during the build phase. The runtime extracts and validates this hash against the expected value to prevent tampering or corruption.
2. **Decryption (`GSHPENC1`)**: If the AOT binary is protected via Virtualization-Based Obfuscation (VBO) or static encryption, the binary will contain the magic header `GSHPENC1`. The runtime automatically extracts the 16-byte Initialization Vector (IV) from the payload header and invokes the provided `EncryptionKeyProvider` (configured via `[GpuKernel]` attribute, or derived by the VBO VM on the host) to derive the AES-256-CBC key and decrypt the binary in-memory before passing it to the NVIDIA driver. (Note: Authenticated encryption like GCM is currently not used; integrity relies on the trailing hash).

### 2.2.2 Caching and Memory Management

* **Concurrent Dictionary**: Once validated and loaded (`cuModuleLoadData`), the resulting `CUfunction` handles are cached in a static `ConcurrentDictionary` using a composite key derived from the embedded hash and kernel name. Subsequent calls instantly retrieve the cached handle in $O(1)$ time.
* **Graceful Shutdown**: Developers can explicitly call `GpuKernelLoader.Shutdown()` to unload all cached CUDA modules (`cuModuleUnload`), preventing device memory leaks when kernels are no longer needed.

## 2.3 Execution and Marshaling (`KernelLauncher`)

Upon successful module loading, the runtime yields a `CUfunction` handle.

1. **Grid and Block Dimensions**: The host logic determines the `gridDim` and `blockDim` parameters based on the workload size.
2. **Argument Array**: The launcher constructs an array of `IntPtr` where each element points to the memory address of a kernel parameter.
3. **Dispatch**: The runtime invokes `cuLaunchKernel`, passing the function handle, dimensions, and the marshaled argument array directly to the GPU driver.

## 2.4 Telemetry and Logging Architecture

The internal telemetry and diagnostic logging system (`Kassyi.GSharp.Core.Logger`) is designed to operate asynchronously without blocking the host application's critical execution paths.

### 2.4.1 High-Throughput Batch I/O

To eliminate the $O(N)$ context-switch penalties associated with sequential asynchronous `await` writes, the logger utilizes a lock-free message queue (`System.Threading.Channels`). 
The background logging task continuously drains the entire queue into an internal buffer ($O(1)$) and writes all pending messages to disk in a single batch operation. This ensures that rapid bursts of log entries do not degrade host application performance.

### 2.4.2 Graceful Logging Shutdown

Because log flushes are deferred to a background task, abrupt application termination can result in truncated or missing logs.
To prevent this, developers should call `Logger.Shutdown()` during application exit. This method signals the channel writer to complete and employs a synchronized `Task.Wait()` to guarantee that the background task flushes all remaining buffered messages to disk before returning. 
Additionally, file deletion commands (e.g., `Logger.Clear()`) are synchronized through the same `__CLEAR__` message queue to prevent disk access contention.

