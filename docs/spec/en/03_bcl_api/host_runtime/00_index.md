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

## Types Without a Page

These public types have no page of their own yet; their XML documentation is shown by IntelliSense.

| Type | Namespace | Purpose |
| :--- | :--- | :--- |
| `CudaStreamExtensions` | `Kassyi.GSharp.Runtime` | `WaitFor(this CudaStream consumer, CudaStream producer)`: makes one stream wait on the device for the work queued on another, without blocking a host thread. |
| `CudaStreamGate` | `Kassyi.GSharp.Runtime` | A reusable, host-signaled gate that holds CUDA streams on the device (`cuStreamWaitValue32_v2`) without occupying a host thread. |
| `DeviceArrayRegions` | `Kassyi.GSharp.Runtime.Interop` | The device regions that one launch allocates for the arrays it copies; used by the generated launchers. |
| `LayoutGuard` | `Kassyi.GSharp.Runtime.Interop` | Throws when a struct layout differs from the one G# predicted; used by the generated launchers. |
| `GSharpErrorCode`, `GSharpErrorSite`, `GSharpDeviceErrorSlot`, `GSharpErrorMapper` | `Kassyi.GSharp.Runtime.Aot` | The device and native CPU error channel: the recorded error, its source site, and the mapping to .NET exceptions. |
| `DeviceErrorSlot` | `Kassyi.GSharp.Runtime.Aot` | Manages the pinned host-mapped error records shared between the host and the device. |
| `DynamicSharedMemoryOptIn` | `Kassyi.GSharp.Runtime.Aot` | Opts a function in to dynamic shared memory beyond 48 KB per block when a launch requests it, and rejects a request the device cannot hold. |

## See also

### BCL API

- [BCL API and Hardware Intrinsics Reference](../00_index.md)
- [GPU Execution Contexts](../math_bignum/gpu_workspace/GpuWorkspace.md)
