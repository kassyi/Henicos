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

Implements [`IGpuGraphProvider`](IGpuExecutionProvider.md) by wrapping CUDA Driver API stream capture and graph execution primitives (`cuStreamBeginCapture`, `cuStreamEndCapture`, `cuGraphInstantiate`, `cuGraphLaunch`, `cuGraphExecDestroy`, `cuGraphDestroy`).

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
| [CalculateGridDim(Int32, UInt32)](#calculategriddimint32-uint32) | Calculates the 1D grid and block dimensions for a given element count. |

## BeginCapture(IntPtr)

Begins thread-local CUDA Graph stream capture (`CU_STREAM_CAPTURE_MODE_THREAD_LOCAL`) on the specified CUDA stream via `cuStreamBeginCapture`.

```csharp
public void BeginCapture(IntPtr stream);
```

#### Parameters

`stream` _IntPtr_<br>
The non-default CUDA stream handle (`CUstream`) on which to begin capturing kernel launches and asynchronous memory operations.

#### Exceptions

`CudaException`<br>
Thrown when `cuStreamBeginCapture` returns a non-zero `CUresult` (for example, if `stream` is the legacy default stream `IntPtr.Zero`).

## EndCapture(IntPtr)

Ends CUDA Graph capture on the specified stream via `cuStreamEndCapture` and returns the captured `CUgraph` handle.

```csharp
public IntPtr EndCapture(IntPtr stream);
```

#### Parameters

`stream` _IntPtr_<br>
The CUDA stream handle (`CUstream`) currently in capture mode.

#### Returns

_IntPtr_<br>
The captured native CUDA graph handle (`CUgraph`).

#### Exceptions

`CudaException`<br>
Thrown when `cuStreamEndCapture` fails (for example, if capture was invalidated by an prohibited synchronous operation).

## InstantiateGraph(IntPtr)

Compiles and instantiates an executable `CUgraphExec` handle from a captured `CUgraph` via `cuGraphInstantiate`.

```csharp
public IntPtr InstantiateGraph(IntPtr graph);
```

#### Parameters

`graph` _IntPtr_<br>
The captured native CUDA graph handle (`CUgraph`).

#### Returns

_IntPtr_<br>
The instantiated executable graph handle (`CUgraphExec`).

#### Exceptions

`CudaException`<br>
Thrown when `cuGraphInstantiate` returns an error.

## LaunchGraph(IntPtr, IntPtr)

Launches an instantiated executable graph (`CUgraphExec`) on the specified CUDA stream via `cuGraphLaunch`.

```csharp
public void LaunchGraph(IntPtr graphExec, IntPtr stream);
```

#### Parameters

`graphExec` _IntPtr_<br>
The executable CUDA graph handle (`CUgraphExec`) returned by [`InstantiateGraph(IntPtr)`](#instantiategraphintptr).

`stream` _IntPtr_<br>
The CUDA stream handle (`CUstream`) on which to enqueue the graph execution.

#### Exceptions

`CudaException`<br>
Thrown when `cuGraphLaunch` returns an error.

## DestroyGraph(IntPtr, IntPtr)

Destroys the executable `CUgraphExec` handle (`cuGraphExecDestroy`) and the underlying `CUgraph` handle (`cuGraphDestroy`) when non-zero.

```csharp
public void DestroyGraph(IntPtr graph, IntPtr graphExec);
```

#### Parameters

`graph` _IntPtr_<br>
The native `CUgraph` handle to destroy, or `IntPtr.Zero` to skip.

`graphExec` _IntPtr_<br>
The native `CUgraphExec` handle to destroy, or `IntPtr.Zero` to skip.

#### Exceptions

`CudaException`<br>
Thrown when `cuGraphExecDestroy` or `cuGraphDestroy` returns an error.

## CalculateGridDim(Int32, UInt32)

Calculates the 1D grid and block dimensions for kernel execution given the element count.

```csharp
public static (uint GridSize, uint BlockSize) CalculateGridDim(int elements, uint blockSize = 256);
```

#### Parameters

`elements` _Int32_<br>
The total number of work items (limbs/words) to process.

`blockSize` _UInt32_<br>
The number of threads per thread block (defaults to `256`).

#### Returns

_(UInt32 GridSize, UInt32 BlockSize)_<br>
A tuple containing the computed grid size (at least `1`) and block size.

## Remarks

`AotGraphHandler` powers [`GpuBigIntExecutableGraph`](../expressions/GpuBigIntExecutableGraph.md) to eliminate CPU kernel launch overhead when replaying repetitive expression trees on the GPU.

## Examples

### Computing 1D Kernel Launch Dimensions

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
// Execution Context: Host (CPU)
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

var (gridSize, blockSize) = AotGraphHandler.CalculateGridDim(elements: 1000, blockSize: 256);
Console.WriteLine($"GridSize={gridSize}, BlockSize={blockSize}");
```

## See also

### BCL API > Math (BigNum)

- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
- [GSharpAotProvider](GSharpAotProvider.md)
- [GpuBigIntExecutableGraph](../expressions/GpuBigIntExecutableGraph.md)
