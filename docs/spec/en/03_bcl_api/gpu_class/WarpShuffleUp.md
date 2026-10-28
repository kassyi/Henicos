---
title: "Warp.ShuffleUpSync Method (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Exchanges a variable by shifting data up by a specified delta."
---

## Warp.ShuffleUpSync Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Exchanges a variable by shifting data up by a specified delta.

## Overloads

| Name | Description |
| --- | --- |
| [ShuffleUpSync(uint, int, uint, int)](#shuffleupsync-uint-int-uint-int) | Exchanges a 32-bit signed integer by shifting data up by a specified delta. |
| [ShuffleUpSync(uint, uint, uint, int)](#shuffleupsync-uint-uint-uint-int) | Exchanges a 32-bit unsigned integer by shifting data up by a specified delta. |
| [ShuffleUpSync(uint, float, uint, int)](#shuffleupsync-uint-float-uint-int) | Exchanges a single-precision floating-point number by shifting data up by a specified delta. |

## ShuffleUpSync(uint, int, uint, int)

Exchanges a 32-bit signed integer by shifting data up by a specified delta.

```csharp
[GpuKernel]
public static int ShuffleUpSync(uint mask, int var, uint delta, int width);
```

#### Parameters

`mask` <i>uint</i><br>
The 32-bit active thread mask. Typically `0xFFFFFFFF` for all 32 threads, or `Gpu.ActiveMask()` if called inside divergent branches. See [Determining the 'mask' Parameter](#determining-the-mask-parameter).

`var` <i>int</i><br>
The variable to exchange.

`delta` <i>uint</i><br>
The offset to shift the lane ID up by.

`width` <i>int</i><br>
The logical warp width: 1, 2, 4, 8, 16 or 32 (default is 32). Any other width is undefined on CUDA: a constant one is reported as [GSH0095](../diagnostics/GSH0095.md), and the CPU paths throw `InvalidOperationException` for one known only at run time.

#### Returns

<i>int</i><br>
The value exchanged from the source thread.

#### Exceptions

[GpuOnlyException](../01_diagnostics_and_exceptions.md#gpuonlyexception)<br>
Thrown when evaluated on the host CPU.

## ShuffleUpSync(uint, uint, uint, int)

Exchanges a 32-bit unsigned integer by shifting data up by a specified delta.

```csharp
public static uint ShuffleUpSync(uint mask, uint var, uint delta, int width);
```

#### Parameters

`mask` <i>uint</i><br>
The 32-bit active thread mask. Typically `0xFFFFFFFF` for all 32 threads, or `Gpu.ActiveMask()` if called inside divergent branches. See [Determining the 'mask' Parameter](#determining-the-mask-parameter).

`var` <i>uint</i><br>
The variable to exchange.

`delta` <i>uint</i><br>
The offset to shift the lane ID up by.

`width` <i>int</i><br>
The logical warp width: 1, 2, 4, 8, 16 or 32 (default is 32). Any other width is undefined on CUDA: a constant one is reported as [GSH0095](../diagnostics/GSH0095.md), and the CPU paths throw `InvalidOperationException` for one known only at run time.

#### Returns

<i>uint</i><br>
The value exchanged from the source thread.

#### Exceptions

[GpuOnlyException](../01_diagnostics_and_exceptions.md#gpuonlyexception)<br>
Thrown when evaluated on the host CPU.

## ShuffleUpSync(uint, float, uint, int)

Exchanges a single-precision floating-point number by shifting data up by a specified delta.

```csharp
[GpuKernel]
public static float ShuffleUpSync(uint mask, float var, uint delta, int width);
```

#### Parameters

`mask` <i>uint</i><br>
The 32-bit active thread mask. Typically `0xFFFFFFFF` for all 32 threads, or `Gpu.ActiveMask()` if called inside divergent branches. See [Determining the 'mask' Parameter](#determining-the-mask-parameter).

`var` <i>float</i><br>
The variable to exchange.

`delta` <i>uint</i><br>
The offset to shift the lane ID up by.

`width` <i>int</i><br>
The logical warp width: 1, 2, 4, 8, 16 or 32 (default is 32). Any other width is undefined on CUDA: a constant one is reported as [GSH0095](../diagnostics/GSH0095.md), and the CPU paths throw `InvalidOperationException` for one known only at run time.

#### Returns

<i>float</i><br>
The value exchanged from the source thread.

#### Exceptions

[GpuOnlyException](../01_diagnostics_and_exceptions.md#gpuonlyexception)<br>
Thrown when evaluated on the host CPU.

## Conceptual Behavior

Warp Shuffle operations can be unintuitive. Think of `ShuffleUpSync` as **"Shifting data to the right (to higher thread IDs)"**. 

In real-world GPU algorithms, this is heavily used in **Stencil Computations (e.g., Gaussian Blur, Image Convolution)** where each pixel (thread) needs to cheaply read the values held by its left-adjacent neighbors without hitting memory.

If a warp has 4 active threads holding values `[A, B, C, D]`, calling `ShuffleUpSync(mask, val, 1, 4)` shifts data "up" the lane IDs by 1:

| Current Lane ID | Initial Value | Target Lane ID (`LaneId - 1`) | Action | Final Value |
| :--- | :--- | :--- | :--- | :--- |
| **0** | `A` | `0 - 1 = -1` (Out of bounds) | Keeps its own value | `A` |
| **1** | `B` | `1 - 1 = 0` | Fetches value from Lane 0 | `A` |
| **2** | `C` | `2 - 1 = 1` | Fetches value from Lane 1 | `B` |
| **3** | `D` | `3 - 1 = 2` | Fetches value from Lane 2 | `C` |

## Determining the `mask` Parameter

> [!CAUTION]
> **GPU Deadlock Warning**: Never pass `0xFFFFFFFF` if some threads in the warp cannot reach the shuffle instruction (e.g., in an `if (ThreadIndex.X % 2 == 0)` branch without the other threads calling it). The GPU hardware will pause and wait indefinitely for inactive threads to reach the barrier.
> **Solution**: Use `Gpu.ActiveMask()` instead. For comprehensive details on hardware scheduling and deadlock avoidance, see the **[Warp Primitives and Thread Masking Guide](../../04_guides/01_warp_programming_guide.md)**.

The `mask` parameter is a 32-bit unsigned integer (`uint`) where each bit corresponds to a lane in the warp (Lane 0 to Lane 31). A bit set to `1` indicates that the corresponding thread participates in the shuffle operation.

In practice, choosing `mask` follows three common patterns:

| Scenario | Recommended Value | Why / Description |
| :--- | :--- | :--- |
| **All threads participate (95%+ of cases)** | `0xFFFFFFFF` | Used when all 32 threads in the warp execute the shuffle unconditionally (e.g., prefix sums, convolutions, reductions). In binary, this sets all 32 bits to `1`. |
| **Divergent execution (`if` branch)** | `Gpu.ActiveMask()` | When only a subset of threads inside an `if` block call shuffle, passing `0xFFFFFFFF` causes a **GPU deadlock**. Calling `Gpu.ActiveMask()` dynamically computes the bitmask of currently active threads. |
| **Partial warp (Tail boundary)** | `(1u << count) - 1` | When processing data where the final warp only has $K$ valid elements (e.g., $K=10$), use `(1u << 10) - 1` (`0x3FF`) to mark only the first 10 threads as active. |

## Examples

### Warp-Level Inclusive Scan (Prefix Sum)

```csharp
// Execution Context: G# Kernel (GPU)
using System;
using Kassyi.GSharp.Core;

try {
    uint mask = 0xFFFFFFFF;
    int myValue = 20;
    
    // Exchanges variable from a thread with a lower ID relative to the caller
    int shiftedValue = Gpu.ShuffleUpSync(mask, myValue, 1);
    Console.WriteLine($"Shifted value from previous lane: {shiftedValue}");
} catch (GpuOnlyException) {
    Console.WriteLine("Gpu.ShuffleUpSync can only be executed on the GPU.");
}
```

## Remarks

Warp Shuffle operations allow threads within the same warp (32 threads) to directly exchange data through GPU registers, completely bypassing shared memory (`SharedMemory<T>`). This results in dramatically lower latency and frees up shared memory for other uses.

`ShuffleUpSync` shifts data from a lower lane ID to a higher lane ID. Specifically, the thread at `LaneId` receives the value of `var` from the thread at `LaneId - delta`. This is the standard primitive for implementing **inclusive/exclusive scans (prefix sums)** within a warp.

Maps to PTX `shfl.up.sync` instructions. Thread synchronization across the active mask is implicit, avoiding the need for `Gpu.SyncThreads()`.

### Undefined Results and the CPU Paths

A shuffle that reads a lane outside the caller's segment of `width` lanes is defined: the caller keeps its own value.
Two forms are undefined on CUDA, which returns an unspecified value without an error:

- A width other than 1, 2, 4, 8, 16 or 32.
- A source lane that does not exist in the block: in the last warp of a block whose size is not a multiple of 32 (for
  example lanes 9 to 31 of a block of 9 threads).

The CPU paths (the native CPU build and the managed fallback) check both and throw `InvalidOperationException` with the
place of the shuffle, for example `G#: Warp.ShuffleDownSync at Kernels.cu.cs(6,13) reads lane 16 of warp 0, which does
not exist in a block of 9 threads (CUDA leaves the value undefined. ...)`; the rest of the message says how to avoid the
read. Run a kernel on the CPU dialect to find these shuffles; the release CUDA build does not check them.

To avoid the read, keep the source lane within the block (limit `delta`, `srcLane` or `laneMask` in the last warp), or
launch a multiple of 32 threads. A `mask` that leaves the lane out, such as `Warp.ActiveMask()`, does not help: the mask
does not limit which lane is read, and CUDA still leaves the value undefined.

## See also



### Language Specification

- [1. Language Specification](../../01_language/00_index.md)
- [4. Execution and Concurrency Model](../../01_language/04_execution_model.md)

### BCL API > Gpu Class

- [Gpu Class](00_index.md)
- [Gpu.SyncThreads Method (G#)](SyncThreads.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [Warp Primitives and Thread Masking Guide](../../04_guides/01_warp_programming_guide.md)

