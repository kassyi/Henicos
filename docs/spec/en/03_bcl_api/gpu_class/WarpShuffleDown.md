---
title: "Warp.ShuffleDownSync Method (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Exchanges a variable by shifting data down by a specified delta."
---

## Warp.ShuffleDownSync Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Exchanges a variable by shifting data down by a specified delta.

## Overloads

| Name | Description |
| --- | --- |
| [ShuffleDownSync(uint, int, uint, int)](#shuffledownsync-uint-int-uint-int) | Exchanges a 32-bit signed integer by shifting data down by a specified delta. |
| [ShuffleDownSync(uint, uint, uint, int)](#shuffledownsync-uint-uint-uint-int) | Exchanges a 32-bit unsigned integer by shifting data down by a specified delta. |
| [ShuffleDownSync(uint, float, uint, int)](#shuffledownsync-uint-float-uint-int) | Exchanges a single-precision floating-point number by shifting data down by a specified delta. |

## ShuffleDownSync(uint, int, uint, int)

Exchanges a 32-bit signed integer by shifting data down by a specified delta.

```csharp
[GpuKernel]
public static int ShuffleDownSync(uint mask, int var, uint delta, int width);
```

#### Parameters

`mask` <i>uint</i><br>
The 32-bit active thread mask. Typically `0xFFFFFFFF` for all 32 threads, or `Gpu.ActiveMask()` if called inside divergent branches. See [Determining the 'mask' Parameter](#determining-the-mask-parameter).

`var` <i>int</i><br>
The variable to exchange.

`delta` <i>uint</i><br>
The offset to shift the lane ID down by.

`width` <i>int</i><br>
The logical warp width (default is 32).

#### Returns

<i>int</i><br>
The value exchanged from the source thread.

## ShuffleDownSync(uint, uint, uint, int)

Exchanges a 32-bit unsigned integer by shifting data down by a specified delta.

```csharp
public static uint ShuffleDownSync(uint mask, uint var, uint delta, int width);
```

#### Parameters

`mask` <i>uint</i><br>
The 32-bit active thread mask. Typically `0xFFFFFFFF` for all 32 threads, or `Gpu.ActiveMask()` if called inside divergent branches. See [Determining the 'mask' Parameter](#determining-the-mask-parameter).

`var` <i>uint</i><br>
The variable to exchange.

`delta` <i>uint</i><br>
The offset to shift the lane ID down by.

`width` <i>int</i><br>
The logical warp width (default is 32).

#### Returns

<i>uint</i><br>
The value exchanged from the source thread.

## ShuffleDownSync(uint, float, uint, int)

Exchanges a single-precision floating-point number by shifting data down by a specified delta.

```csharp
[GpuKernel]
public static float ShuffleDownSync(uint mask, float var, uint delta, int width);
```

#### Parameters

`mask` <i>uint</i><br>
The 32-bit active thread mask. Typically `0xFFFFFFFF` for all 32 threads, or `Gpu.ActiveMask()` if called inside divergent branches. See [Determining the 'mask' Parameter](#determining-the-mask-parameter).

`var` <i>float</i><br>
The variable to exchange.

`delta` <i>uint</i><br>
The offset to shift the lane ID down by.

`width` <i>int</i><br>
The logical warp width (default is 32).

#### Returns

<i>float</i><br>
The value exchanged from the source thread.

## Conceptual Behavior

Warp Shuffle operations can be unintuitive. Think of `ShuffleDownSync` as **"Shifting data to the left (to lower thread IDs)"**.

In real-world GPU algorithms, this is heavily used in **Stencil Computations (e.g., Gaussian Blur, Image Convolution)** where each pixel (thread) needs to cheaply read the values held by its right-adjacent neighbors without hitting memory. It is also the backbone of **Tree Reductions**.

If a warp has 4 active threads holding values `[A, B, C, D]`, calling `ShuffleDownSync(mask, val, 1, 4)` shifts data "down" the lane IDs by 1:

| Current Lane ID | Initial Value | Target Lane ID (`LaneId + 1`) | Action | Final Value |
| :--- | :--- | :--- | :--- | :--- |
| **0** | `A` | `0 + 1 = 1` | Fetches value from Lane 1 | `B` |
| **1** | `B` | `1 + 1 = 2` | Fetches value from Lane 2 | `C` |
| **2** | `C` | `2 + 1 = 3` | Fetches value from Lane 3 | `D` |
| **3** | `D` | `3 + 1 = 4` (Out of bounds) | Keeps its own value | `D` |

## Determining the `mask` Parameter

The `mask` parameter is a 32-bit unsigned integer (`uint`) where each bit corresponds to a lane in the warp (Lane 0 to Lane 31). A bit set to `1` indicates that the corresponding thread participates in the shuffle operation.

In practice, choosing `mask` follows three common patterns:

| Scenario | Recommended Value | Why / Description |
| :--- | :--- | :--- |
| **All threads participate (95%+ of cases)** | `0xFFFFFFFF` | Used when all 32 threads in the warp execute the shuffle unconditionally (e.g., prefix sums, convolutions, reductions). In binary, this sets all 32 bits to `1`. |
| **Divergent execution (`if` branch)** | `Gpu.ActiveMask()` | When only a subset of threads inside an `if` block call shuffle, passing `0xFFFFFFFF` causes a **GPU deadlock**. Calling `Gpu.ActiveMask()` dynamically computes the bitmask of currently active threads. |
| **Partial warp (Tail boundary)** | `(1u << count) - 1` | When processing data where the final warp only has $K$ valid elements (e.g., $K=10$), use `(1u << 10) - 1` (`0x3FF`) to mark only the first 10 threads as active. |

> [!CAUTION]
> **GPU Deadlock Warning**: Never pass `0xFFFFFFFF` if some threads in the warp cannot reach the shuffle instruction (e.g., in an `if (ThreadIndex.X % 2 == 0)` branch without the other threads calling it). The GPU hardware will pause and wait indefinitely for inactive threads to reach the barrier.

For comprehensive details on active masks, hardware scheduling, and deadlock avoidance, see the [Warp Primitives and Thread Masking Guide](../../04_guides/01_warp_programming_guide.md).

## Examples

### Warp-Level Reduction (Cascading Sum)

```csharp
// Execution Context: G# Kernel (GPU)
[GpuKernel]
public static unsafe void WarpReduceKernel(int* data)
{
    int val = data[ThreadIndex.X];
    
    // Reduce values across the warp. Each thread pulls data from the thread 'offset' lanes above it.
    // Example: Thread 0 pulls from Thread 16, then Thread 8, then 4, 2, 1.
    for (uint offset = 16; offset > 0; offset /= 2)
    {
        val += Warp.ShuffleDownSync(0xFFFFFFFF, val, offset, 32);
    }
    
    // The final reduced sum is guaranteed to be in Lane 0 (ThreadIndex.X % 32 == 0)
    if (ThreadIndex.X % 32 == 0)
    {
        data[ThreadIndex.X] = val; // Store result
    }
}
```

## Remarks

Warp Shuffle operations allow threads within the same warp (32 threads) to directly exchange data through GPU registers, completely bypassing shared memory (`SharedMemory<T>`). This results in dramatically lower latency and frees up shared memory for other uses.

`ShuffleDownSync` shifts data from a higher lane ID to a lower lane ID. Specifically, the thread at `LaneId` receives the value of `var` from the thread at `LaneId + delta`. This is the standard primitive for implementing **tree reductions** (e.g., finding the sum or max of all values in a warp).

Maps to PTX `shfl.down.sync` instructions. Thread synchronization across the active mask is implicit, avoiding the need for `Gpu.SyncThreads()`.

## See also

- [Warp Primitives and Thread Masking Guide](../../04_guides/01_warp_programming_guide.md)
- [SyncThreads](SyncThreads.md)
- [Gpu BCL API Overview](../01_api_overview.md)
