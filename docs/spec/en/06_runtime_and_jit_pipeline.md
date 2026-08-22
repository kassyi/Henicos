# 6. Runtime and JIT Pipeline (`Kassyi.GSharp.Runtime`)

This document defines the internal architecture of the Just-In-Time (JIT) compilation pipeline, caching mechanisms, and hardware interaction layer.


## 6.1 Context and Thread Management (`JitEngine`)
The JIT engine utilizes a singleton CUDA context (`s_globalContext`) shared across the application. When a kernel is invoked, `EnsureInitialized()` ensures that the CUDA context is bound to the calling thread using `CudaDriverApi.cuCtxSetCurrent` and tracked via a `[ThreadStatic]` flag.

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

### 6.3.2 Static Analysis (`ptxas`)
The compiler specifies the `--ptxas-options=-v` flag to extract static resource allocation data from the NVIDIA PTX assembler.
* The standard error output is captured and parsed using regex.
* Metrics such as `RegistersUsed`, `SharedMemoryBytes`, and `ConstantMemoryBytes` are populated into a `CompilationResult` object. This enables static verification of optimization strategies (e.g., proving that `#pragma unroll` reduced register pressure) without necessitating physical hardware execution.

## 6.4 Execution and Marshaling (`KernelLauncher`)

Upon successful compilation and module loading (`cuModuleLoadData`), `JitEngine` yields a `CUfunction` handle.

1. **Grid and Block Dimensions**: The host logic determines the `gridDim` and `blockDim` parameters based on the workload size.
2. **Argument Array**: The launcher constructs an array of `IntPtr` where each element points to the memory address of a kernel parameter.
3. **Dispatch**: The runtime invokes `cuLaunchKernel`, passing the function handle, dimensions, and the marshaled argument array directly to the GPU driver.
