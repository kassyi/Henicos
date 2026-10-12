---
title: "GpuBigIntExecutableGraph Struct (G#)"
site: "G# Documentation"
published: "2026-10-12T00:00:00.000Z"
language: "en"
description: "Represents a compiled, executable CUDA Graph for repeated execution of a GpuBigInt expression tree."
---

## GpuBigIntExecutableGraph Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Represents a compiled, executable CUDA Graph instance (`CUgraphExec`) for a `GpuBigInt` expression tree. Eliminates kernel launch overhead and CPU driver API scheduling latency for repeated mathematical evaluations.

```csharp
public readonly struct GpuBigIntExecutableGraph : IDisposable
```

## Properties

| Name              | Description                                                    |
| ----------------- | -------------------------------------------------------------- |
| [Result](#result) | Gets the buffer holding the result of the CUDA Graph execution.|

## Result

Gets the buffer holding the result of the CUDA Graph execution. This buffer is populated and updated each time [Execute()](#execute) is invoked.

```csharp
public GpuBigInt Result { get; }
```

## Methods

| Name                   | Description                                                               |
| ---------------------- | ------------------------------------------------------------------------- |
| [Dispose()](#dispose) | Destroys the underlying CUDA Graph and executable graph driver resources. |
| [Execute()](#execute) | Launches the executable graph on the GPU and waits synchronously.         |

## Dispose()

Destroys the underlying native CUDA Graph (`CUgraph`) and executable graph (`CUgraphExec`) driver instances via `cuGraphDestroy` and `cuGraphExecDestroy`.

```csharp
public void Dispose();
```

## Execute()

Launches the executable graph on the GPU stream (`cuGraphLaunch`) and synchronously waits for completion (`cuStreamSynchronize`).

```csharp
public void Execute();
```

## Remarks

> [!NOTE]
> `GpuBigIntExecutableGraph` is a `readonly struct`. To safely manage unmanaged driver resources, use the standard C# `using` statement (`using var graph = expr.CompileToGraph(workspace);`). Avoid using null-conditional access (`?.`) as it is invalid on value types (`CS0023`).

> [!WARNING]
> The current execution model is synchronous and blocks the caller CPU thread until the GPU finishes all operations. For asynchronous non-blocking dispatch, track issue [#317](https://github.com/kassyi/Henicos/issues/317).

## Examples

### Compiling and Launching Repeatedly via CUDA Graph

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// Execution Context: Host (CPU)
using var workspace = new GpuWorkspace(2048, GpuExecutionEngine.GSharpAot);

using var uintA = GpuBigUInt.FromBigInteger(workspace, new BigInteger(500), minWords: 4);
using var uintB = GpuBigUInt.FromBigInteger(workspace, new BigInteger(250), minWords: 4);

var a = new GpuBigInt(uintA, isNegative: false);
var b = new GpuBigInt(uintB, isNegative: false);

GpuBigIntExpr expr = (GpuBigIntExpr)a + b;

// Compile to an executable graph
using var graph = expr.CompileToGraph(workspace);

// Execute on GPU
graph.Execute();

Console.WriteLine($"Result={graph.Result.Value.ToBigInteger(graph.Result.IsNegative)}");
```

## See also

### BCL API > Math Bignum
- [GpuBigIntExpr Struct](GpuBigIntExpr.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
