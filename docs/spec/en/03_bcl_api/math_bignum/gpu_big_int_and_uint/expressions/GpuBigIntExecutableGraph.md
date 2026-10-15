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

| Name | Description |
| --- | --- |
| [Result](#result) | Gets the buffer holding the result of the CUDA Graph execution. |

## Result

Gets the buffer holding the result of the CUDA Graph execution. This buffer is populated and updated each time [Execute()](#execute) is invoked.

```csharp
public GpuBigInt Result { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [Dispose()](#dispose) | Destroys the underlying CUDA Graph and executable graph driver resources. |
| [Execute()](#execute) | Launches the executable graph on the GPU and waits synchronously. |
| [ExecuteAsync(CancellationToken)](#executeasynccancellationtoken) | Launches the executable graph and waits without blocking the calling thread. |

## Dispose()

Destroys the underlying native CUDA Graph (`CUgraph`) and executable graph (`CUgraphExec`) driver instances via `cuGraphDestroy` and `cuGraphExecDestroy`.

```csharp
public void Dispose();
```

Copies of the struct share one set of graph handles, so `Dispose` may be called on any copy and more than once. Afterwards every copy rejects execution with `ObjectDisposedException`. Disposing while a launch is still running is safe and does not wait: the driver releases the executable graph once the launch completes. The [Result](#result) buffer is not released by `Dispose`.

## Execute()

Launches the executable graph on the GPU stream (`cuGraphLaunch`) and synchronously waits for completion (`cuStreamSynchronize`).

```csharp
public void Execute();
```

#### Exceptions

`ObjectDisposedException`<br>
The graph has been disposed.

## ExecuteAsync(CancellationToken)

Launches the executable graph on the GPU stream (`cuGraphLaunch`) and waits for completion without blocking the calling thread.

```csharp
public ValueTask ExecuteAsync(CancellationToken cancellationToken = default);
```

#### Parameters

`cancellationToken` _CancellationToken_<br>
A token that stops waiting. An already canceled token prevents the launch; a later cancellation leaves the graph running to completion.

#### Returns

_ValueTask_<br>
A task that completes when the graph has finished. Repeated launches do not allocate in steady state.

#### Exceptions

`ObjectDisposedException`<br>
The graph has been disposed.

`OperationCanceledException`<br>
`cancellationToken` was canceled.

> [!IMPORTANT]
> Cancellation only detaches the awaiter. The launched graph keeps running, so after a canceled `ExecuteAsync` the contents of [Result](#result) are only final once the workspace stream has drained. Reads ordered on that stream, such as `ToBigInteger()`, wait for the graph and observe the completed result.

## Remarks

> [!NOTE]
> `GpuBigIntExecutableGraph` is a `readonly struct`. To safely manage unmanaged driver resources, use the standard C# `using` statement (`using var graph = expr.CompileToGraph(workspace);`). Avoid using null-conditional access (`?.`) as it is invalid on value types (`CS0023`).

> [!NOTE]
> [Execute()](#execute) blocks the caller CPU thread until the GPU finishes; prefer [ExecuteAsync](#executeasynccancellationtoken) in thread-pool-sensitive hosts such as ASP.NET Core. Each launch is ordered on the workspace stream, so launches of the same graph never overlap.

> [!NOTE]
> The workspace context is activated when the handles are destroyed, so `Dispose` is also safe after an `await` that resumed on a different thread.

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

### Launching Without Blocking the Calling Thread

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using System.Numerics;
using System.Threading;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// Execution Context: Host (CPU)
using var workspace = new GpuWorkspace(2048, GpuExecutionEngine.GSharpAot);

using var uintA = GpuBigUInt.FromBigInteger(workspace, new BigInteger(500), minWords: 4);
using var uintB = GpuBigUInt.FromBigInteger(workspace, new BigInteger(250), minWords: 4);

var a = new GpuBigInt(uintA, isNegative: false);
var b = new GpuBigInt(uintB, isNegative: false);

using var graph = ((GpuBigIntExpr)a + b).CompileToGraph(workspace);

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
await graph.ExecuteAsync(cts.Token);

Console.WriteLine($"Result={await graph.Result.Value.ToBigIntegerAsync(graph.Result.IsNegative)}");
```

## See also

### BCL API > Math Bignum
- [GpuBigIntExpr Struct](GpuBigIntExpr.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
