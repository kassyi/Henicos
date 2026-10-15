---
title: "Host Runtime"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Host-side types for CUDA streams, non-blocking completion and device-to-host transfers."
---

# Host Runtime

Host-side types (`Kassyi.GSharp.Runtime`) for driving CUDA streams without blocking threads. They run on the CPU and cannot be used inside a `[GpuKernel]`.

## In This Section

- [CudaStream](CudaStream.md): A non-blocking CUDA stream with an asynchronous `SynchronizeAsync`.
- [CudaStreamCompletion](CudaStreamCompletion.md): Awaits all work queued on a stream without parking a thread.
- [CudaHostTransfer](CudaHostTransfer.md): Copies device memory to the host through pooled page-locked buffers without blocking.
- [CudaEventTimer](CudaEventTimer.md): Measures GPU execution time with CUDA events.
- [GpuKernelLoader](GpuKernelLoader.md): Manages CUDA context scopes, CPU fallback detection, and verified AOT Fatbin module loading.
- [CpuKernelLoader](CpuKernelLoader.md): Loads signed AOT native CPU kernel libraries and controls managed execution fallbacks.
- [ConstantArgument](ConstantArgument.md): Represents an unboxed constant argument for AOT/JIT kernel specialization.
- [FatbinParser](FatbinParser.md): Defensively parses and inspects NVIDIA FATBIN and `.gfatbin` binary payloads.
- [FatbinImage](FatbinImage.md): Represents a parsed FATBIN binary image, its embedded modules, and its optional Ed25519 signature.
- [FatbinItemInfo](FatbinItemInfo.md): Metadata describing an individual PTX or cuBIN module inside a FATBIN image.
- [GpuSecurityPolicy](GpuSecurityPolicy.md): Manages trusted Ed25519 public keys and signature verification policies for GPU binaries.
- [GpuBuffer\<T\>](GpuBuffer.md): Strongly-typed contiguous device memory buffer with automatic context scoping and CPU fallback.
- [LaunchDimension](LaunchDimension.md): Calculates 1D grid and block dimensions for kernel launches.
- [CudaDriverApi](CudaDriverApi.md): P/Invoke bindings, `CUresult`, `CudaException`, and descriptors for the NVIDIA CUDA Driver API.
- [CudaLibraryTypes](CudaLibraryTypes.md): Status and operation enumerations (`CuBlasStatus`, `CuBlasOperation`, `CuRandStatus`, `CuRandRngType`) for cuBLAS and cuRAND interop.
- [GSharpArrayProxy](GSharpArrayProxy.md): Managed 16-byte explicit-layout proxy struct matching native `GSharpArray<T>`.
- [NativeLibraryProbe](NativeLibraryProbe.md): Probes and resolves native library paths for generated `DllImportResolver` bindings.

## See also

### BCL API

- [BCL API and Hardware Intrinsics Reference](../00_index.md)
- [GPU Execution Contexts](../math_bignum/gpu_workspace/GpuWorkspace.md)
