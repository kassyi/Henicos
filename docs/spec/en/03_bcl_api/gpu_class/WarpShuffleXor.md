---
title: "Warp.ShuffleXorSync Method (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Exchanges a variable between threads by applying an XOR operation to their lane IDs."
---

## Warp.ShuffleXorSync Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Exchanges a variable between threads by applying an XOR operation to their lane IDs.

## Overloads

| Name | Description |
| --- | --- |
| [ShuffleXorSync(uint, int, int, int)](#shufflexorsync-uint-int-int-int) | Exchanges a 32-bit signed integer between threads by applying an XOR operation to their lane IDs. |
| [ShuffleXorSync(uint, uint, int, int)](#shufflexorsync-uint-uint-int-int) | Exchanges a 32-bit unsigned integer between threads by applying an XOR operation to their lane IDs. |
| [ShuffleXorSync(uint, float, int, int)](#shufflexorsync-uint-float-int-int) | Exchanges a single-precision floating-point number between threads by applying an XOR operation to their lane IDs. |

## ShuffleXorSync(uint, int, int, int)

Exchanges a 32-bit signed integer between threads by applying an XOR operation to their lane IDs.

```csharp
[GpuKernel]
public static int ShuffleXorSync(uint mask, int var, int laneMask, int width);
```

#### Parameters

`mask` <i>uint</i><br>
The 32-bit active thread mask. Typically `0xFFFFFFFF` for all 32 threads, or `Gpu.ActiveMask()` if called inside divergent branches. See [Determining the 'mask' Parameter](#determining-the-mask-parameter).

`var` <i>int</i><br>
The variable to exchange.

`laneMask` <i>int</i><br>
The XOR mask applied to the current lane ID.

`width` <i>int</i><br>
The logical warp width: 1, 2, 4, 8, 16 or 32 (default is 32). Any other width is undefined on CUDA: a constant one is reported as [GSH0095](../diagnostics/GSH0095.md), and the CPU paths throw `InvalidOperationException` for one known only at run time.

#### Returns

<i>int</i><br>
The value exchanged from the target thread.

#### Exceptions

[GpuOnlyException](../01_diagnostics_and_exceptions.md#gpuonlyexception)<br>
Thrown when evaluated on the host CPU.

## ShuffleXorSync(uint, uint, int, int)

Exchanges a 32-bit unsigned integer between threads by applying an XOR operation to their lane IDs.

```csharp
public static uint ShuffleXorSync(uint mask, uint var, int laneMask, int width);
```

#### Parameters

`mask` <i>uint</i><br>
The 32-bit active thread mask. Typically `0xFFFFFFFF` for all 32 threads, or `Gpu.ActiveMask()` if called inside divergent branches. See [Determining the 'mask' Parameter](#determining-the-mask-parameter).

`var` <i>uint</i><br>
The variable to exchange.

`laneMask` <i>int</i><br>
The XOR mask applied to the current lane ID.

`width` <i>int</i><br>
The logical warp width: 1, 2, 4, 8, 16 or 32 (default is 32). Any other width is undefined on CUDA: a constant one is reported as [GSH0095](../diagnostics/GSH0095.md), and the CPU paths throw `InvalidOperationException` for one known only at run time.

#### Returns

<i>uint</i><br>
The value exchanged from the target thread.

#### Exceptions

[GpuOnlyException](../01_diagnostics_and_exceptions.md#gpuonlyexception)<br>
Thrown when evaluated on the host CPU.

## ShuffleXorSync(uint, float, int, int)

Exchanges a single-precision floating-point number between threads by applying an XOR operation to their lane IDs.

```csharp
[GpuKernel]
public static float ShuffleXorSync(uint mask, float var, int laneMask, int width);
```

#### Parameters

`mask` <i>uint</i><br>
The 32-bit active thread mask. Typically `0xFFFFFFFF` for all 32 threads, or `Gpu.ActiveMask()` if called inside divergent branches. See [Determining the 'mask' Parameter](#determining-the-mask-parameter).

`var` <i>float</i><br>
The variable to exchange.

`laneMask` <i>int</i><br>
The XOR mask applied to the current lane ID.

`width` <i>int</i><br>
The logical warp width: 1, 2, 4, 8, 16 or 32 (default is 32). Any other width is undefined on CUDA: a constant one is reported as [GSH0095](../diagnostics/GSH0095.md), and the CPU paths throw `InvalidOperationException` for one known only at run time.

#### Returns

<i>float</i><br>
The value exchanged from the target thread.

#### Exceptions

[GpuOnlyException](../01_diagnostics_and_exceptions.md#gpuonlyexception)<br>
Thrown when evaluated on the host CPU.

## Conceptual Behavior

Warp Shuffle operations can be unintuitive. Think of `ShuffleXorSync` as **"Bidirectional swapping of data between paired threads"**.

In real-world GPU algorithms, this is the foundational building block for **Fast Fourier Transforms (FFT)**, **Bitonic Sorting Networks**, and highly optimized **Butterfly Reductions**.

If a warp has 4 active threads holding values `[A, B, C, D]`, calling `ShuffleXorSync(mask, val, 1, 4)` forces threads to pair up based on an XOR mask (Binary `01`) and swap their values:

| Current Lane ID | Initial Value | Target Lane ID (`LaneId ^ 1`) | Action | Final Value |
| :--- | :--- | :--- | :--- | :--- |
| **0** | `A` | `0 ^ 1 = 1` | Fetches value from Lane 1 | `B` |
| **1** | `B` | `1 ^ 1 = 0` | Fetches value from Lane 0 | `A` |
| **2** | `C` | `2 ^ 1 = 3` | Fetches value from Lane 3 | `D` |
| **3** | `D` | `3 ^ 1 = 2` | Fetches value from Lane 2 | `C` |

## Determining the `mask` Parameter

> [!CAUTION]
> **GPU Deadlock Warning**: Never pass `0xFFFFFFFF` if some threads in the warp cannot reach the shuffle instruction (e.g., in an `if (ThreadIndex.X % 2 == 0)` branch without the other threads calling it). The GPU hardware will pause and wait indefinitely for inactive threads to reach the barrier.
> **Solution**: Use `Gpu.ActiveMask()` instead. For comprehensive details on hardware scheduling and deadlock avoidance, see the **[Warp Primitives and Thread Masking Guide](../../04_guides/01_warp_programming_guide.md)**.

> [!NOTE]
> **Difference Between `mask` and `laneMask`**:
> - `mask` (1st parameter): The **synchronization mask** (which threads in the warp participate in the exchange, usually `0xFFFFFFFF`).
> - `laneMask` (3rd parameter): The **XOR offset** (e.g., `1`, `2`, `4`, `8`, `16`) used to compute the target partner thread (`TargetLane = LaneId ^ laneMask`).

The `mask` parameter is a 32-bit unsigned integer (`uint`) where each bit corresponds to a lane in the warp (Lane 0 to Lane 31). A bit set to `1` indicates that the corresponding thread participates in the shuffle operation.

In practice, choosing `mask` follows three common patterns:

| Scenario | Recommended Value | Why / Description |
| :--- | :--- | :--- |
| **All threads participate (95%+ of cases)** | `0xFFFFFFFF` | Used when all 32 threads in the warp execute the shuffle unconditionally (e.g., butterfly reductions, FFTs, bitonic sorts). In binary, this sets all 32 bits to `1`. |
| **Divergent execution (`if` branch)** | `Gpu.ActiveMask()` | When only a subset of threads inside an `if` block call shuffle, passing `0xFFFFFFFF` causes a **GPU deadlock**. Calling `Gpu.ActiveMask()` dynamically computes the bitmask of currently active threads. |
| **Partial warp (Tail boundary)** | `(1u << count) - 1` | When processing data where the final warp only has $K$ valid elements (e.g., $K=10$), use `(1u << 10) - 1` (`0x3FF`) to mark only the first 10 threads as active. |

## Examples

### Butterfly Reduction / Swap

```csharp
// Execution Context: G# Kernel (GPU)
using System;
using Kassyi.GSharp.Core;

try {
    uint mask = 0xFFFFFFFF;
    int myValue = 30;
    
    // Exchanges variable based on a bitwise XOR of the caller's lane ID (useful for butterfly reductions)
    int swappedValue = Gpu.ShuffleXorSync(mask, myValue, 1);
    Console.WriteLine($"Swapped value: {swappedValue}");
} catch (GpuOnlyException) {
    Console.WriteLine("Gpu.ShuffleXorSync can only be executed on the GPU.");
}
```

## Remarks

Warp Shuffle operations allow threads within the same warp (32 threads) to directly exchange data through GPU registers, completely bypassing shared memory (`SharedMemory<T>`). This results in dramatically lower latency and frees up shared memory for other uses.

`ShuffleXorSync` exchanges data between threads using a bitwise XOR on their lane IDs. Specifically, the thread at `LaneId` receives the value of `var` from the thread at `LaneId ^ laneMask`. This bidirectional exchange makes it the standard primitive for **butterfly reductions**, **bitonic sorts**, and **Fast Fourier Transforms (FFT)** within a warp.

Maps to PTX `shfl.bfly.sync` instructions. Thread synchronization across the active mask is implicit, avoiding the need for `Gpu.SyncThreads()`.

### Undefined Results and the CPU Paths

The warp is split into groups of `width` lanes. A lane may read from its own group and from earlier groups; when
`LaneId ^ laneMask` falls in a later group, the caller keeps its own value. For example, with `width` 8 and `laneMask` 8,
lanes 8 to 15 read lanes 0 to 7, while lanes 0 to 7 keep their own values (CUDA Programming Guide, Warp Shuffle
Functions; PTX `shfl.bfly` reads only lanes up to the last lane of the caller's group).
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

