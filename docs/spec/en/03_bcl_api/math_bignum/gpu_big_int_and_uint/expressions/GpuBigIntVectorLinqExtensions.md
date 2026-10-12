---
title: "GpuBigIntVectorLinqExtensions Class (G#)"
site: "G# Documentation"
published: "2026-10-12T00:00:00.000Z"
language: "en"
description: "Provides batched and vectorized LINQ operations for contiguous GpuBigIntArray pipelines."
---

## GpuBigIntVectorLinqExtensions Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Provides batched and vectorized LINQ extension methods for [GpuBigIntArray](../GpuBigIntArray.md), compiling deferred query expressions into fused multi-block GPU kernels and parallel reduction trees.

```csharp
public static class GpuBigIntVectorLinqExtensions
```

## Methods

| Name                                                                                              | Description                                                                   |
| ------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------- |
| [AsVectorizedEnumerable(GpuBigIntArray)](#asvectorizedenumerablegpubigintarray)                   | Converts a `GpuBigIntArray` into a vectorized deferred expression.            |
| [SelectMultiply(IGpuBigIntVectorExpression, GpuBigInt)](#selectmultiplyigpubigintvectorexpression-gpubigint) | Multiplies each element of a vectorized expression by a scalar on GPU device. |
| [Sum(IGpuBigIntVectorExpression)](#sumigpubigintvectorexpression)                                 | Computes the parallel sum reduction of a vectorized expression.               |

## AsVectorizedEnumerable(GpuBigIntArray)

Converts a `GpuBigIntArray` into a vectorized deferred expression leaf node.

```csharp
public static IGpuBigIntVectorExpression AsVectorizedEnumerable(this GpuBigIntArray source);
```

#### Parameters

`source` _GpuBigIntArray_<br>
The source `GpuBigIntArray` stored in GPU global device memory.

#### Returns

_IGpuBigIntVectorExpression_<br>
A vectorized expression wrapping the array.

## SelectMultiply(IGpuBigIntVectorExpression, GpuBigInt)

Multiplies each element of a vectorized expression by a scalar value using a grid-stride parallel kernel.

```csharp
public static IGpuBigIntVectorExpression SelectMultiply(
    this IGpuBigIntVectorExpression source,
    GpuBigInt scalar);
```

#### Parameters

`source` _IGpuBigIntVectorExpression_<br>
The input vectorized expression pipeline.

`scalar` _GpuBigInt_<br>
The scalar value to multiply each vector element by.

#### Returns

_IGpuBigIntVectorExpression_<br>
A new vectorized expression representing the element-wise scalar multiplication.

## Sum(IGpuBigIntVectorExpression)

Computes the sum of all elements in a vectorized expression using a multi-pass tree reduction kernel.

```csharp
public static GpuBigIntExpr Sum(this IGpuBigIntVectorExpression source);
```

#### Parameters

`source` _IGpuBigIntVectorExpression_<br>
The input vectorized expression.

#### Returns

_GpuBigIntExpr_<br>
A deferred expression tree representing the parallel sum reduction, yielding a scalar `GpuBigInt` when evaluated.

## Remarks

Unlike standard LINQ or scalar expression trees that evaluate element-by-element, `GpuBigIntVectorLinqExtensions` constructs execution plans that operate directly over contiguous arrays. Reduction operations like [Sum](#sumigpubigintvectorexpression) automatically partition the workload into multi-pass chunk kernels, scaling efficiently up to millions of elements.

Because [Sum](#sumigpubigintvectorexpression) returns a [GpuBigIntExpr](GpuBigIntExpr.md), the entire vector reduction pipeline can be compiled directly into a native CUDA Graph:
- **One-Liner Evaluation:** Chain `.EvaluateWithCudaGraph(workspace)` to capture, instantiate, execute, and destroy a CUDA Graph in a single statement.
- **Precompiled Reusable Execution:** Chain `.CompileToGraph(workspace)` to produce a reusable [GpuBigIntExecutableGraph](GpuBigIntExecutableGraph.md) that can be dispatched repeatedly via `.Execute()` with zero CPU launch latency.

## Examples

### Vectorized Inner Product and Parallel Sum Reduction

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// Execution Context: Host (CPU)
using var workspace = new GpuWorkspace(65536, GpuExecutionEngine.GSharpAot);

// Allocate contiguous array of 256 elements
using var array = new GpuBigIntArray(workspace, length: 256, wordsPerElement: 4);

// Scalar multiplier
using var scalarUint = GpuBigUInt.FromBigInteger(workspace, new BigInteger(7), minWords: 4);
var scalar = new GpuBigInt(scalarUint, isNegative: false);

// Evaluate the entire vector pipeline
using var dest = new GpuBigUInt(workspace, 8);

var result = array.AsVectorizedEnumerable()
    .SelectMultiply(scalar)
    .Sum()
    .Evaluate(dest);

Console.WriteLine($"Reduction result: {result.Value.ToBigInteger(result.IsNegative)}");
```

### One-Liner CUDA Graph Execution

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
// Evaluate vector pipeline using a one-liner CUDA Graph execution
using var graphResult = array.AsVectorizedEnumerable()
    .SelectMultiply(scalar)
    .Sum()
    .EvaluateWithCudaGraph(workspace);

Console.WriteLine($"Graph result: {graphResult.Value.ToBigInteger(graphResult.IsNegative)}");
```

### Precompiled CUDA Graph for High-Frequency Repetition

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
// Compile the vector pipeline AST into a reusable CUDA Graph
using var precompiledGraph = array.AsVectorizedEnumerable()
    .SelectMultiply(scalar)
    .Sum()
    .CompileToGraph(workspace);

// Execute the graph repeatedly with minimal driver launch latency
for (int i = 0; i < 10; i++)
{
    precompiledGraph.Execute();
}

Console.WriteLine($"Precompiled result: {precompiledGraph.Result.Value.ToBigInteger(precompiledGraph.Result.IsNegative)}");
```

## See also

### BCL API > Math Bignum
- [GpuBigIntArray Struct](../GpuBigIntArray.md)
- [GpuBigIntExpr Struct](GpuBigIntExpr.md)
- [GpuBigIntExecutableGraph Struct](GpuBigIntExecutableGraph.md)
- [IGpuBigIntVectorExpression Interface](IGpuBigIntVectorExpression.md)

