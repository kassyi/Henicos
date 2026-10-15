---
title: "GpuBigIntExpr Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "A value type wrapper that builds a lazy expression tree of GPU signed BigInteger arithmetic."
---

## GpuBigIntExpr Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

A lightweight value type wrapper that constructs and manages a lazy expression tree of signed GPU BigInteger arithmetic. Deferred sign resolution allows for robust evaluation of operations where resultant signs depend on runtime operand magnitudes (e.g. addition of different signs or subtraction of larger numbers).

```csharp
public readonly struct GpuBigIntExpr
```

## Properties

| Name | Description |
| --- | --- |
| [Node](#node) | Gets the root AST node of this signed expression tree. |

## Node

Gets the root AST node of this signed expression tree.

```csharp
public IGpuBigIntExpression Node { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [CompileToGraph(GpuWorkspace, GpuBigUInt?)](#compiletographgpuworkspace-gpubiguint) | Compiles this expression tree into an executable CUDA Graph, eliminating launch overhead. |
| [Evaluate(GpuBigUInt)](#evaluategpubiguint) | Evaluates this signed expression into the provided destination buffer. |
| [Evaluate(GpuWorkspace)](#evaluategpuworkspace) | Evaluates this signed expression into a newly rented temporary GPU buffer. |
| [EvaluateWithCudaGraph(GpuWorkspace, GpuBigUInt?)](#evaluatewithcudagraphgpuworkspace-gpubiguint) | Evaluates this expression tree by dynamically compiling into a CUDA Graph, launching, and destroying it. |
| [EvaluateWithCudaGraphAsync(GpuWorkspace, GpuBigUInt?, CancellationToken)](#evaluatewithcudagraphasyncgpuworkspace-gpubiguint-cancellationtoken) | Like `EvaluateWithCudaGraph`, but waits for the graph without blocking the calling thread. |
| [Max(GpuBigIntExpr, GpuBigIntExpr)](#maxgpubigintexpr-gpubigintexpr) | Constructs a lazy expression that resolves to the maximum of two signed big integer expressions. |
| [Min(GpuBigIntExpr, GpuBigIntExpr)](#mingpubigintexpr-gpubigintexpr) | Constructs a lazy expression that resolves to the minimum of two signed big integer expressions. |

## CompileToGraph(GpuWorkspace, GpuBigUInt?)

Compiles this expression tree into an executable CUDA Graph, eliminating launch overhead for repeated executions. The caller is responsible for disposing the returned graph.

```csharp
public GpuBigIntExecutableGraph CompileToGraph(GpuWorkspace workspace, GpuBigUInt? targetResult = null);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing memory pool and CUDA stream execution.

`targetResult` _GpuBigUInt?_<br>
An optional pre-allocated buffer for the final result magnitude.

#### Returns

_GpuBigIntExecutableGraph_<br>
An executable graph instance that can be launched repeatedly with zero CPU-side scheduling overhead.

#### Exceptions

`InvalidOperationException`<br>
Thrown if warm-up evaluation fails prior to beginning CUDA Graph capture.

## Evaluate(GpuBigUInt)

Evaluates this signed expression into the provided destination buffer.

```csharp
public GpuBigInt Evaluate(GpuBigUInt destination);
```

#### Parameters

`destination` _GpuBigUInt_<br>
The pre-allocated GPU buffer to store the evaluated magnitude.

#### Returns

_GpuBigInt_<br>
A new `GpuBigInt` instance wrapping the destination buffer with the evaluated sign.

## Evaluate(GpuWorkspace)

Evaluates this signed expression into a newly rented temporary GPU buffer.

```csharp
public GpuBigInt Evaluate(GpuWorkspace workspace);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace used for temporary buffer allocation.

#### Returns

_GpuBigInt_<br>
A new `GpuBigInt` instance wrapping the temporary buffer with the evaluated sign.

## EvaluateWithCudaGraph(GpuWorkspace, GpuBigUInt?)

Evaluates this expression tree by dynamically compiling it into a CUDA Graph, launching it, and destroying it immediately.

```csharp
public GpuBigInt EvaluateWithCudaGraph(GpuWorkspace workspace, GpuBigUInt? targetResult = null);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing memory pool and CUDA stream execution.

`targetResult` _GpuBigUInt?_<br>
An optional pre-allocated buffer for the final result magnitude.

#### Returns

_GpuBigInt_<br>
A new `GpuBigInt` instance wrapping the evaluated result.

## EvaluateWithCudaGraphAsync(GpuWorkspace, GpuBigUInt?, CancellationToken)

Evaluates this expression tree through a CUDA Graph like [EvaluateWithCudaGraph](#evaluatewithcudagraphgpuworkspace-gpubiguint), but waits for the graph without blocking the calling thread.

```csharp
public async ValueTask<GpuBigInt> EvaluateWithCudaGraphAsync(GpuWorkspace workspace, GpuBigUInt? targetResult = null, CancellationToken cancellationToken = default);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing memory pool and CUDA stream execution.

`targetResult` _GpuBigUInt?_<br>
An optional pre-allocated buffer for the final result magnitude.

`cancellationToken` _CancellationToken_<br>
A token that stops waiting for the graph. The graph itself still runs to completion.

#### Returns

_ValueTask&lt;GpuBigInt&gt;_<br>
A new `GpuBigInt` instance wrapping the evaluated result.

#### Exceptions

`OperationCanceledException`<br>
`cancellationToken` was canceled. A result buffer allocated by this method is released in stream order behind the graph; a supplied `targetResult` is left to the caller and receives the result once the graph completes.

> [!NOTE]
> Only the final wait is asynchronous. Compilation (a warm-up evaluation, capture and instantiation) runs synchronously on the calling thread and includes host synchronization. To evaluate the same expression repeatedly, call [CompileToGraph](#compiletographgpuworkspace-gpubiguint) once and use [GpuBigIntExecutableGraph.ExecuteAsync](GpuBigIntExecutableGraph.md#executeasynccancellationtoken).

## Max(GpuBigIntExpr, GpuBigIntExpr)

Constructs a lazy expression that resolves to the maximum of two signed big integer expressions.

```csharp
public static GpuBigIntExpr Max(GpuBigIntExpr left, GpuBigIntExpr right);
```

#### Parameters

`left` _GpuBigIntExpr_<br>
The first signed expression operand.

`right` _GpuBigIntExpr_<br>
The second signed expression operand.

#### Returns

_GpuBigIntExpr_<br>
A new `GpuBigIntExpr` representing the Max AST node.

## Min(GpuBigIntExpr, GpuBigIntExpr)

Constructs a lazy expression that resolves to the minimum of two signed big integer expressions.

```csharp
public static GpuBigIntExpr Min(GpuBigIntExpr left, GpuBigIntExpr right);
```

#### Parameters

`left` _GpuBigIntExpr_<br>
The first signed expression operand.

`right` _GpuBigIntExpr_<br>
The second signed expression operand.

#### Returns

_GpuBigIntExpr_<br>
A new `GpuBigIntExpr` representing the Min AST node.

## Operators

| Name | Description |
| --- | --- |
| `implicit operator GpuBigIntExpr(GpuBigInt)` | Implicitly wraps a `GpuBigInt` as a signed expression leaf node. |
| `operator *(GpuBigIntExpr, GpuBigIntExpr)` | Constructs a lazy multiplication expression. |
| `operator /(GpuBigIntExpr, GpuBigIntExpr)` | Constructs a lazy division expression. |
| `operator +(GpuBigIntExpr, GpuBigIntExpr)` | Constructs a lazy addition expression with deferred sign resolution. |
| `operator -(GpuBigIntExpr, GpuBigIntExpr)` | Constructs a lazy subtraction expression with deferred sign resolution. |
| `operator -(GpuBigIntExpr)` | Constructs a lazy negation expression. |
| `operator ~(GpuBigIntExpr)` | Constructs a lazy bitwise NOT expression. |

## Remarks

> [!IMPORTANT]
> **Capture Warm-Up Requirement:** During CUDA Graph capture (`cuStreamBeginCapture`), any device memory allocation (`cuMemAlloc`) will invalidate the capture stream. `CompileToGraph` executes an automated warm-up pass before capture begins to ensure all necessary memory buffers and JIT kernels are primed outside the capture window.

This struct serves as the frontend for operator overloading on GPU BigIntegers, avoiding memory allocations by building a lightweight AST. The expression tree is then evaluated lazily in a single pass on the GPU device or compiled into an executable CUDA Graph for high-frequency kernel dispatch.

## Examples

### Building and Evaluating AST on GPU

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// Execution Context: Host (CPU)
// Allocate a GPU workspace with 1024 words (4KB) of memory pool.
using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

// Upload initial buffers to the GPU device
using var uintA = GpuBigUInt.FromBigInteger(workspace, new BigInteger(100), minWords: 4);
using var uintB = GpuBigUInt.FromBigInteger(workspace, new BigInteger(200), minWords: 4);

var a = new GpuBigInt(uintA, isNegative: false);
var b = new GpuBigInt(uintB, isNegative: true);

// Build AST node lazy expression wrapper
GpuBigIntExpr exprA = a;
GpuBigIntExpr exprB = b;

// Construct AST tree without executing computation
var sumExpr = exprA + exprB;
var prodExpr = sumExpr * exprA;

// Evaluate AST back to a magnitude buffer
using var dest = new GpuBigUInt(workspace, 8);
var finalResult = prodExpr.Evaluate(dest);

Console.WriteLine($"Result={finalResult.Value.ToBigInteger(finalResult.IsNegative)}");
```

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

using var uintA = GpuBigUInt.FromBigInteger(workspace, new BigInteger(150), minWords: 4);
using var uintB = GpuBigUInt.FromBigInteger(workspace, new BigInteger(75), minWords: 4);

var a = new GpuBigInt(uintA, isNegative: false);
var b = new GpuBigInt(uintB, isNegative: false);

GpuBigIntExpr expr = (GpuBigIntExpr)a + b;

// Compile AST into an executable CUDA Graph
using var graph = expr.CompileToGraph(workspace);

// Execute the captured graph repeatedly with minimal driver launch latency
for (int i = 0; i < 5; i++)
{
    graph.Execute();
}

Console.WriteLine($"Graph Result={graph.Result.Value.ToBigInteger(graph.Result.IsNegative)}");
```

## See also

### BCL API > Math Bignum
- [Standard Math Library (Stdlib.Math)](../../../00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
- [GpuBigIntExecutableGraph Struct](GpuBigIntExecutableGraph.md)
