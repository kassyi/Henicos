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
The logical warp width (default is 32).

#### Returns

<i>int</i><br>
The value exchanged from the target thread.

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
The logical warp width (default is 32).

#### Returns

<i>uint</i><br>
The value exchanged from the target thread.

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
The logical warp width (default is 32).

#### Returns

<i>float</i><br>
The value exchanged from the target thread.

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

The `mask` parameter is a 32-bit unsigned integer (`uint`) where each bit corresponds to a lane in the warp (Lane 0 to Lane 31). A bit set to `1` indicates that the corresponding thread participates in the shuffle operation.

> [!NOTE]
> **Difference Between `mask` and `laneMask`**:
> - `mask` (1st parameter): The **synchronization mask** (which threads in the warp participate in the exchange, usually `0xFFFFFFFF`).
> - `laneMask` (3rd parameter): The **XOR offset** (e.g., `1`, `2`, `4`, `8`, `16`) used to compute the target partner thread (`TargetLane = LaneId ^ laneMask`).

In practice, choosing `mask` follows three common patterns:

| Scenario | Recommended Value | Why / Description |
| :--- | :--- | :--- |
| **All threads participate (95%+ of cases)** | `0xFFFFFFFF` | Used when all 32 threads in the warp execute the shuffle unconditionally (e.g., butterfly reductions, FFTs, bitonic sorts). In binary, this sets all 32 bits to `1`. |
| **Divergent execution (`if` branch)** | `Gpu.ActiveMask()` | When only a subset of threads inside an `if` block call shuffle, passing `0xFFFFFFFF` causes a **GPU deadlock**. Calling `Gpu.ActiveMask()` dynamically computes the bitmask of currently active threads. |
| **Partial warp (Tail boundary)** | `(1u << count) - 1` | When processing data where the final warp only has $K$ valid elements (e.g., $K=10$), use `(1u << 10) - 1` (`0x3FF`) to mark only the first 10 threads as active. |

> [!CAUTION]
> **GPU Deadlock Warning**: Never pass `0xFFFFFFFF` if some threads in the warp cannot reach the shuffle instruction (e.g., in an `if (ThreadIndex.X % 2 == 0)` branch without the other threads calling it). The GPU hardware will pause and wait indefinitely for inactive threads to reach the barrier.

For comprehensive details on active masks, hardware scheduling, and deadlock avoidance, see the [Warp Primitives and Thread Masking Guide](../../04_guides/01_warp_programming_guide.md).

## Examples

### Butterfly Reduction / Swap

```csharp
// Execution Context: G# Kernel (GPU)
[GpuKernel]
public static unsafe void ButterflyReduceKernel(int* data)
{
    int val = data[ThreadIndex.X];
    
    // Reduce values across the warp using a butterfly pattern (XOR swap)
    // Both threads in the XOR pair exchange their values simultaneously.
    for (int offset = 16; offset > 0; offset /= 2)
    {
        val += Warp.ShuffleXorSync(0xFFFFFFFF, val, offset, 32);
    }
    
    // The final reduced sum is distributed to ALL threads in the warp
    data[ThreadIndex.X] = val;
}
```

## Remarks

Warp Shuffle operations allow threads within the same warp (32 threads) to directly exchange data through GPU registers, completely bypassing shared memory (`SharedMemory<T>`). This results in dramatically lower latency and frees up shared memory for other uses.

`ShuffleXorSync` exchanges data between threads using a bitwise XOR on their lane IDs. Specifically, the thread at `LaneId` receives the value of `var` from the thread at `LaneId ^ laneMask`. This bidirectional exchange makes it the standard primitive for **butterfly reductions**, **bitonic sorts**, and **Fast Fourier Transforms (FFT)** within a warp.

Maps to PTX `shfl.bfly.sync` instructions. Thread synchronization across the active mask is implicit, avoiding the need for `Gpu.SyncThreads()`.

## See also

- [Warp Primitives and Thread Masking Guide](../../04_guides/01_warp_programming_guide.md)
- [SyncThreads](SyncThreads.md)
- [Gpu BCL API Overview](../01_api_overview.md)
