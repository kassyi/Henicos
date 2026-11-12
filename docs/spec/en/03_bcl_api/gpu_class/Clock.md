---
title: "Gpu.Clock and Gpu.Clock64 Methods (G#)"
site: "G# Documentation"
published: "2026-11-12T00:00:00.000Z"
language: "en"
description: "Read a counter that never decreases during a launch: the cycle counter of the streaming multiprocessor on CUDA, monotonic nanoseconds on the CPU paths."
---

## Gpu.Clock and Gpu.Clock64 Methods

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Read a counter that never decreases while a launch runs. On CUDA it is the cycle counter of the streaming
multiprocessor the thread runs on; on the CPU paths it counts nanoseconds of the monotonic clock.

## Overloads

| Name | Description |
| --- | --- |
| [Clock()](#clock) | The low 32 bits of the counter, which wrap. |
| [Clock64()](#clock64) | The 64-bit counter. |

## Clock()

```csharp
public static uint Clock();
```

#### Returns

_uint_<br>
The low 32 bits of the counter (PTX `%clock` on CUDA). They wrap: compare two readings by their difference,
`(uint)(second - first)`, not by their order.

## Clock64()

```csharp
public static ulong Clock64();
```

#### Returns

_ulong_<br>
The 64-bit counter (PTX `%clock64` on CUDA).

## What the value means on each path

| Path | Counter | Unit |
| :--- | :--- | :--- |
| CUDA | The cycle counter of the streaming multiprocessor (`clock()`, `clock64()`) | Cycles of that multiprocessor |
| Native CPU build | `std::chrono::steady_clock` | Nanoseconds |
| Managed CPU path | `Stopwatch.GetTimestamp()`, converted | Nanoseconds |

The contract is the same on every path: within one thread, a later reading of `Clock64()` is never below an earlier
one (on CUDA, while the thread stays on its multiprocessor, which a running block leaves only when it is preempted), and a later reading of `Clock()` is ahead of an earlier one by less than 2^31 for any interval shorter than that.
The values themselves are not reproducible from run to run on any path, and the CPU unit is not cycles: use the clock
to measure or to wait a while, not to compute a result. Readings of different multiprocessors on CUDA are not
synchronized with each other.

## Examples

### Waiting for a number of ticks

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
using Kassyi.GSharp.Core;

public static partial class SpinKernels
{
    // Execution Context: G# Kernel (GPU)
    [GpuKernel]
    public static void SpinKernel(long ticks, int[] done)
    {
        ulong start = Gpu.Clock64();
        while ((long)(Gpu.Clock64() - start) < ticks)
        {
        }

        done[ThreadIndex.X] = 1;
    }
}
```

## See also

### BCL API > Gpu Class

- [Gpu Class](00_index.md)

### Language Specification

- [5. Limitations and Diagnostics](../../01_language/05_limitations.md)
