---
title: "StreamCompactionOrchestrator Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Orchestrates the multi-block Stream Compaction pipeline using recursive Blelloch exclusive prefix sum scans on the GPU."
---

## StreamCompactionOrchestrator Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Orchestrates the multi-block Stream Compaction pipeline using recursive Blelloch exclusive prefix sum scans on the GPU.

```csharp
public static class StreamCompactionOrchestrator
```

## Methods

| Name | Description |
| --- | --- |
| [ExecuteExclusiveScan(GpuWorkspace, IntPtr, IntPtr, Int32)](#executeexclusivescangpuworkspace-intptr-intptr-int32) | Executes an exclusive prefix sum (Blelloch Scan) across an array of elements, handling multi-block partitioning recursively. |

## ExecuteExclusiveScan(GpuWorkspace, IntPtr, IntPtr, Int32)

Executes an exclusive prefix sum (Blelloch Scan) across a potentially large array of 32-bit unsigned integer elements, handling multi-block partitioning recursively.

```csharp
public static void ExecuteExclusiveScan(
    GpuWorkspace workspace,
    IntPtr outputPtr,
    IntPtr inputPtr,
    int elementCount);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace memory context.

`outputPtr` _IntPtr_<br>
The GPU device pointer to receive the exclusive prefix sum result.

`inputPtr` _IntPtr_<br>
The GPU device pointer holding the input elements.

`elementCount` _Int32_<br>
The number of elements to scan.

## Remarks

Each thread block processes up to `2048` elements (`1024` threads per block, 2 elements per thread) using [GpuStreamCompactionKernels.BlockExclusiveScan](../kernels/GpuStreamCompactionKernels.md). When `elementCount` exceeds `2048`, `StreamCompactionOrchestrator` partitions the input across multiple blocks, recursively scans the per-block sums into a temporary [GpuBigUIntArray](../GpuBigUIntArray.md), and adds the scanned block offsets back into the output buffer via `GpuStreamCompactionKernels.AddBlockSums`.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
// Execution Context: Host (CPU)
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

using var workspace = new GpuWorkspace(maxWords: 64, engine: GpuExecutionEngine.GSharpAot);
using var input = new GpuBigUInt(workspace, new uint[] { 1, 0, 1, 1 });
using var output = new GpuBigUInt(workspace, 4);

StreamCompactionOrchestrator.ExecuteExclusiveScan(
    workspace,
    output.DevicePointer,
    input.DevicePointer,
    elementCount: 4);

uint[] scanned = new uint[4];
workspace.Provider.CopyDeviceToHost(scanned, output.DevicePointer, 4, workspace.Stream);
Console.WriteLine($"Exclusive scan: [{string.Join(", ", scanned)}]");
```

## See also

### BCL API > Math (BigNum)

- [GpuStreamCompactionKernels Class](../kernels/GpuStreamCompactionKernels.md)
- [KernelBasicMathHandler Class](KernelBasicMathHandler.md)
- [GSharpKernelProvider Class](GSharpKernelProvider.md)
