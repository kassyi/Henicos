# 6. Runtime and JIT Pipeline (`Kassyi.GSharp.Runtime`)

This document defines the internal architecture of the Just-In-Time (JIT) compilation pipeline, caching mechanisms, and hardware interaction layer.


## 6.1 Context and Thread Management (`JitEngine`)
The JIT engine utilizes a robust scope-based context management lifecycle to ensure safe multi-threaded execution without relying on global state. 

Instead of a singleton context tracked via `[ThreadStatic]`, the AOT generated launcher stub automatically emits a local scope instance (`using var _cudaCtxScope = new CudaContextScope();`) at the beginning of each kernel invocation. This `CudaContextScope` pushes the active CUDA context to the calling thread (`cuCtxPushCurrent`) and safely pops it upon disposal (`cuCtxPopCurrent`), natively supporting parallel kernel dispatches from multiple host threads.

The engine also dynamically queries the physical hardware's compute capability (e.g., `sm_89` or `sm_86`) using `cuDeviceGetAttribute` during initialization to perfectly match the JIT compilation target to the host GPU.

## 6.2 JIT Compilation and Caching

To eliminate redundant compilation overhead across loops or repeated kernel invocations, G# implements a robust caching mechanism within `JitEngine`:

1. **SHA256 Fingerprinting**: When the launcher stub calls `GetOrCompileKernel(string cppSource, string kernelName)`, the runtime computes a SHA256 hash of the complete C++ string.
2. **Cache Key Construction**: The cache key combines the hash, the hardware compute capability, and the kernel name: `"{sourceHash}_{s_smVersion}_{kernelName}"`.
3. **Concurrent Dictionary**: A static `ConcurrentDictionary` stores the compiled `GpuModule` handles. If a cache hit occurs, the cached `IntPtr` to the kernel function is instantly returned.
4. **Cache Directory**: Compiled `.cu` and `.fatbin` files are persisted in `%TEMP%\Kassyi.GSharp.Runtime.JitCache`.

### 6.2.1 Graceful Shutdown
Developers can explicitly call `JitEngine.Shutdown()` to unload all cached CUDA modules (`cuModuleUnload`) and clear the dictionary, preventing device memory leaks when kernels are no longer needed.

### 6.2.2 Cache Hit vs. Miss
1. **Hit**: If the hash exists in the concurrent dictionary `s_compiledKernels`, the cached `IntPtr` (representing a `CUfunction` handle) is returned immediately in $O(1)$ time.
2. **Miss**: The engine writes the `cuCode` to a temporary directory and invokes `NvccCompiler.CompileToFatbin()`.

## 6.3 Backend Compilation (`BackendCompiler`)

The `NvccCompiler` orchestrates the external `nvcc` process to compile the CUDA C++ file into a machine-specific executable format (Fatbin).

### 6.3.1 Environmental Dependencies
The JIT compiler dynamically invokes external toolchains and expects the following host environment:
1. **Visual Studio (MSVC)**: Uses `vswhere.exe` to locate the latest VC++ build tools and invokes `vcvars64.bat` to configure the C++ compilation environment.
2. **CUDA Toolkit**: Resolves the CUDA compiler via the `CUDA_PATH` environment variable (`bin\nvcc.exe`).
If these tools are missing, the JIT engine will throw an `InvalidOperationException` or `Exception` prior to compilation.

### 6.3.2 Options File Injection Security
To mitigate the risk of OS Command Injection and to bypass the Windows command prompt's 8191-character limit, `BackendCompiler` avoids passing arguments directly via the command line.
Instead, all source paths, headers (`-I`), and NVCC optimization flags are written to a temporary `--options-file` (`.txt`). This file is then passed safely to the `nvcc.exe` process and automatically deleted upon compilation completion.

### 6.3.3 Static Analysis (`ptxas`)
The compiler specifies the `--ptxas-options=-v` flag to extract static resource allocation data from the NVIDIA PTX assembler.
* The standard error output is captured and parsed using regex.
* Metrics such as `RegistersUsed`, `SharedMemoryBytes`, and `ConstantMemoryBytes` are populated into a `CompilationResult` object. This enables static verification of optimization strategies (e.g., proving that `#pragma unroll` reduced register pressure) without necessitating physical hardware execution.

## 6.4 Execution and Marshaling (`KernelLauncher`)

Upon successful compilation and module loading (`cuModuleLoadData`), `JitEngine` yields a `CUfunction` handle.

1. **Grid and Block Dimensions**: The host logic determines the `gridDim` and `blockDim` parameters based on the workload size.
2. **Argument Array**: The launcher constructs an array of `IntPtr` where each element points to the memory address of a kernel parameter.
3. **Dispatch**: The runtime invokes `cuLaunchKernel`, passing the function handle, dimensions, and the marshaled argument array directly to the GPU driver.

## 6.5 Telemetry and Logging Architecture

The internal telemetry and diagnostic logging system (`Kassyi.GSharp.Core.Logger`) is designed to operate asynchronously without blocking the host application's critical execution paths.

### 6.5.1 High-Throughput Batch I/O

To eliminate the $O(N)$ context-switch penalties associated with sequential asynchronous `await` writes, the logger utilizes a lock-free message queue (`System.Threading.Channels`). 
The background logging task continuously drains the entire queue into an internal buffer ($O(1)$) and writes all pending messages to disk in a single batch operation. This ensures that rapid bursts of log entries do not degrade host application performance.

### 6.5.2 Graceful Logging Shutdown

Because log flushes are deferred to a background task, abrupt application termination can result in truncated or missing logs.
To prevent this, developers should call `Logger.Shutdown()` during application exit. This method signals the channel writer to complete and employs a synchronized `Task.Wait()` to guarantee that the background task flushes all remaining buffered messages to disk before returning. 
Additionally, file deletion commands (e.g., `Logger.Clear()`) are synchronized through the same `__CLEAR__` message queue to prevent disk access contention.
