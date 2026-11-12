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
- **Minimum Driver**: a driver for **CUDA 13.0 or newer**, that is the R580 driver branch or newer (580.65.06 or newer on Linux; on Windows any R580 or newer driver, as the CUDA 13 release notes list no separate Windows version). Source: the CUDA Driver section of the [CUDA Toolkit release notes](https://docs.nvidia.com/cuda/cuda-toolkit-release-notes/index.html#cuda-driver) and of [CUDA 13.0](https://docs.nvidia.com/cuda/archive/13.0.0/cuda-toolkit-release-notes/index.html#cuda-driver). When the runtime initializes CUDA, it asks the driver for the CUDA version it supports (`cuDriverGetVersion`); on an older driver, GPU use throws a `NotSupportedException` that names the version found and the version required, instead of falling back to the CPU; `GpuKernelLoader.IsDriverAvailable` is then `false` and `GpuKernelLoader.DriverUnavailableReason` holds the message. The CPU mode below and `ForceCpuFallback` do not check the driver.
    - The PTX image in the Fatbin (used on GPUs without matching native code, see the table of [`GSharpTargetArchitectures`](../02_architecture/01_aot_transpiler.md#151-target-gpu-architectures-gsharptargetarchitectures)) is JIT-compiled only by a driver at least as new as the CUDA Toolkit that built it (for example R615 for CUDA 13.4). On such a GPU, either keep the driver at the toolkit's version or add the GPU's architecture to `GSharpTargetArchitectures`.
- **Build Tools**:
    - **[NVIDIA CUDA Toolkit](https://developer.nvidia.com/cuda-downloads)** 13.0 or newer: Required for `nvcc` to compile the transpiled `.cu` files into native CUDA Fatbins (`.fatbin`). Ensure `nvcc` is available in your system `PATH`.
    - **[C++ Host Compiler](https://visualstudio.microsoft.com/downloads/)**: **Visual Studio C++ Build Tools (MSVC `cl.exe`)** on Windows or `gcc`/`clang` on Linux, required as the host backend compiler for `nvcc`.

## 2. CPU Mode ([StandardCppDialect](../02_architecture/01_aot_transpiler.md#123-transpiler-dialect-architecture-two-tier-design) / Fallback)

For local debugging, unit testing, and CI/CD pipelines without GPU hardware:

- Requires a standard **C++ Host Compiler** (MSVC on Windows, or GCC/Clang on Linux via `GccClangToolchain`). G#
  transpiles kernels to C++17 and compiles native shared libraries (`.dll` on Windows, `.so` on Linux). When the native
  library of the kernels does not exist (for example, no C++ compiler built it), or inside
  `GpuExecution.Use(KernelExecution.Managed)`, G# runs the kernels as managed C# instead
  (`CpuKernelLoader.ShouldUseManagedExecution`, [GpuExecution](../03_bcl_api/execution_context/GpuExecution.md)).
- **No NVIDIA GPU, Driver, or CUDA Toolkit is required**. While execution speeds are reduced compared to hardware
  execution, this allows seamless local debugging and CI/CD testing on GPU-less machines.
- See [Dialect Architecture](../02_architecture/01_aot_transpiler.md#123-transpiler-dialect-architecture-two-tier-design)
  and [CPU Dialect Limitations](../01_language/05_limitations.md#52-dialect-syntax-support).

Once your environment is set up, proceed to [2. The Mental Model & Safety Net](02_mental_model.md) to understand how G# maps C# code to CUDA.

## See also



### Language Specification

- [1. Language Specification](../01_language/00_index.md)
- [1. Foundation and Domain](../01_language/01_introduction.md)

### Architecture

- [2. Compiler and Runtime Architecture](../02_architecture/00_index.md)
- [2. Runtime Execution and AOT Loader Pipeline (`Kassyi.GSharp.Runtime`)](../02_architecture/02_runtime_loader.md)

