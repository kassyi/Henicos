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

## See also

### BCL API

- [BCL API and Hardware Intrinsics Reference](../00_index.md)
- [GPU Execution Contexts](../math_bignum/gpu_workspace/GpuWorkspace.md)
