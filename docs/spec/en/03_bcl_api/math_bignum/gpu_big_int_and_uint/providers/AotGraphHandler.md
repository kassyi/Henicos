---
title: "AotGraphHandler Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Provides CUDA Graph capture, instantiation, launch, and 1D grid dimension calculation for GSharp BigNum kernel pipelines."
---

## AotGraphHandler Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Implements `IGpuGraphProvider` by wrapping CUDA Driver API stream capture and graph execution primitives (`cuStreamBeginCapture`, `cuStreamEndCapture`, `cuGraphInstantiate`, `cuGraphLaunch`, `cuGraphExecDestroy`, `cuGraphDestroy`).

```csharp
public sealed class AotGraphHandler : IGpuGraphProvider
```

## Methods

| Name | Description |
| --- | --- |
| [BeginCapture(IntPtr)](#begincaptureintptr) | Begins thread-local CUDA Graph capture (`CU_STREAM_CAPTURE_MODE_THREAD_LOCAL`) on the specified stream. |
| [EndCapture(IntPtr)](#endcaptureintptr) | Ends CUDA Graph capture on the specified stream and returns the `CUgraph` handle. |
| [InstantiateGraph(IntPtr)](#instantiategraphintptr) | Compiles and instantiates an executable `CUgraphExec` handle from a captured `CUgraph`. |
| [LaunchGraph(IntPtr, IntPtr)](#launchgraphintptr-intptr) | Launches an executable `CUgraphExec` on the specified CUDA stream. |
| [DestroyGraph(IntPtr, IntPtr)](#destroygraphintptr-intptr) | Destroys the `CUgraphExec` and `CUgraph` handles. |
| [CalculateGridDim(int, uint)](#calculategriddimint-uint) | Calculates the 1D grid and block dimensions for a given element count. |

## BeginCapture(IntPtr)

```csharp
public void BeginCapture(IntPtr stream);
```

## EndCapture(IntPtr)

```csharp
public IntPtr EndCapture(IntPtr stream);
```

## InstantiateGraph(IntPtr)

```csharp
public IntPtr InstantiateGraph(IntPtr graph);
```

## LaunchGraph(IntPtr, IntPtr)

```csharp
public void LaunchGraph(IntPtr graphExec, IntPtr stream);
```

## DestroyGraph(IntPtr, IntPtr)

```csharp
public void DestroyGraph(IntPtr graph, IntPtr graphExec);
```

## CalculateGridDim(int, uint)

Calculates the optimal grid and block dimensions for 1D kernel execution given the element count.

```csharp
public static (uint GridSize, uint BlockSize) CalculateGridDim(int elements, uint blockSize = 256);
```

#### Parameters

`elements` _int_<br>
The total number of work items (limbs/words) to process.

`blockSize` _uint_<br>
The number of threads per thread block (defaults to `256`).

#### Returns

_(uint GridSize, uint BlockSize)_<br>
A tuple containing the computed grid size (at least `1`) and block size.

## Examples

### Computing 1D Kernel Launch Dimensions

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

var (gridSize, blockSize) = AotGraphHandler.CalculateGridDim(elements: 1000, blockSize: 256);
Console.WriteLine($"GridSize={gridSize}, BlockSize={blockSize}");
```

## Remarks

`AotGraphHandler` powers [`GpuBigIntExecutableGraph`](../expressions/GpuBigIntExecutableGraph.md) to eliminate CPU kernel launch overhead when replaying repetitive expression trees on the GPU.

## See also

### BCL API > Math (BigNum)

- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
- [GSharpAotProvider](GSharpAotProvider.md)
- [GpuBigIntExecutableGraph](../expressions/GpuBigIntExecutableGraph.md)
