---
title: "1. Prerequisites & Installation"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Prerequisites for Building & Running G#"
---

# 1. Prerequisites & Installation

Before writing code, ensure you have the correct environment configured depending on whether you are targeting true GPU execution or the CPU fallback mode.

## 1. GPU Execution (Default & Recommended)

To leverage actual hardware acceleration:

- **Hardware & Drivers**: A **CUDA-capable NVIDIA GPU** and the **NVIDIA Display Driver**. With the default build settings, kernels run on GPUs of compute capability 8.6 and newer; for 7.5 or 8.0 GPUs, set [`GSharpTargetArchitectures`](../02_architecture/01_aot_transpiler.md#151-target-gpu-architectures-gsharptargetarchitectures).
- **Build Tools**:
    - **[NVIDIA CUDA Toolkit](https://developer.nvidia.com/cuda-downloads)**: Required for `nvcc` to compile the transpiled `.cu` files into native CUDA Fatbins (`.fatbin`). Ensure `nvcc` is available in your system `PATH`.
    - **[C++ Host Compiler](https://visualstudio.microsoft.com/downloads/)**: **Visual Studio C++ Build Tools (MSVC `cl.exe`)** on Windows or `gcc`/`clang` on Linux, required as the host backend compiler for `nvcc`.

## 2. CPU Mode ([StandardCppDialect](../02_architecture/01_aot_transpiler.md#123-transpiler-dialect-architecture-two-tier-design) / Fallback)

For local debugging, unit testing, and CI/CD pipelines without GPU hardware:

- Requires a standard **C++ Host Compiler** (MSVC on Windows, or GCC/Clang on Linux via `GccClangToolchain`). G# transpiles kernels to C++17 and compiles native shared libraries (`.dll` on Windows, `.so` on Linux). When a native C++ compiler is not available, G# falls back to pure C# managed execution via `CpuKernelLoader.ShouldUseManagedExecution`.
- **No NVIDIA GPU, Driver, or CUDA Toolkit is required**. While execution speeds are reduced compared to hardware execution, this allows seamless local debugging and CI/CD testing on GPU-less machines.
- See [Dialect Architecture](../02_architecture/01_aot_transpiler.md#123-transpiler-dialect-architecture-two-tier-design) and [CPU Dialect Limitations](../01_language/05_limitations.md#52-dialect-syntax-support).

Once your environment is set up, proceed to [2. The Mental Model & Safety Net](02_mental_model.md) to understand how G# maps C# code to CUDA.

## See also



### Language Specification

- [1. Language Specification](../01_language/00_index.md)
- [1. Foundation and Domain](../01_language/01_introduction.md)

### Architecture

- [2. Compiler and Runtime Architecture](../02_architecture/00_index.md)
- [2. Runtime Execution and AOT Loader Pipeline (`Kassyi.GSharp.Runtime`)](../02_architecture/02_runtime_loader.md)

