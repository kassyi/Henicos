---
title: "GpuBigIntVectorLinqExtensions Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
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

| Name | Description |
| --- | --- |
| [All(IGpuBoolVectorExpression)](#alligpuboolvectorexpression) | Determines whether all elements of a boolean vector expression evaluate to `true`. |
| [And(IGpuBoolVectorExpression, IGpuBoolVectorExpression)](#andigpuboolvectorexpression-igpuboolvectorexpression) | Performs an element-wise logical AND on two boolean vector expressions. |
| [Any(IGpuBoolVectorExpression)](#anyigpuboolvectorexpression) | Determines whether any element of a boolean vector expression evaluates to `true`. |
| [AsVectorizedEnumerable(GpuBigIntArray)](#asvectorizedenumerablegpubigintarray) | Converts a `GpuBigIntArray` into a vectorized deferred expression. |
| [Count(IGpuBigIntVectorExpression)](#countigpubigintvectorexpression) | Computes the number of elements in the vectorized expression. |
| [IsEqualTo(IGpuBigIntVectorExpression, GpuBigInt)](#isequaltoigpubigintvectorexpression-gpubigint) | Evaluates if each element is equal to a scalar. |
| [IsEqualTo(IGpuBigIntVectorExpression, IGpuBigIntVectorExpression)](#isequaltoigpubigintvectorexpression-igpubigintvectorexpression) | Evaluates if elements in the left vector are equal to the right vector element-wise. |
| [IsGreaterThan(IGpuBigIntVectorExpression, GpuBigInt)](#isgreaterthanigpubigintvectorexpression-gpubigint) | Evaluates if each element is greater than a scalar. |
| [IsGreaterThan(IGpuBigIntVectorExpression, IGpuBigIntVectorExpression)](#isgreaterthanigpubigintvectorexpression-igpubigintvectorexpression) | Evaluates if elements in the left vector are greater than the right vector element-wise. |
| [IsGreaterThanOrEqualTo(IGpuBigIntVectorExpression, GpuBigInt)](#isgreaterthanorequaltoigpubigintvectorexpression-gpubigint) | Evaluates if each element is greater than or equal to a scalar. |
| [IsGreaterThanOrEqualTo(IGpuBigIntVectorExpression, IGpuBigIntVectorExpression)](#isgreaterthanorequaltoigpubigintvectorexpression-igpubigintvectorexpression) | Evaluates if elements in the left vector are greater than or equal to the right vector element-wise. |
| [IsLessThan(IGpuBigIntVectorExpression, GpuBigInt)](#islessthanigpubigintvectorexpression-gpubigint) | Evaluates if each element is less than a scalar. |
| [IsLessThan(IGpuBigIntVectorExpression, IGpuBigIntVectorExpression)](#islessthanigpubigintvectorexpression-igpubigintvectorexpression) | Evaluates if elements in the left vector are less than the right vector element-wise. |
| [IsLessThanOrEqualTo(IGpuBigIntVectorExpression, GpuBigInt)](#islessthanorequaltoigpubigintvectorexpression-gpubigint) | Evaluates if each element is less than or equal to a scalar. |
| [IsLessThanOrEqualTo(IGpuBigIntVectorExpression, IGpuBigIntVectorExpression)](#islessthanorequaltoigpubigintvectorexpression-igpubigintvectorexpression) | Evaluates if elements in the left vector are less than or equal to the right vector element-wise. |
| [IsNotEqualTo(IGpuBigIntVectorExpression, GpuBigInt)](#isnotequaltoigpubigintvectorexpression-gpubigint) | Evaluates if each element is not equal to a scalar. |
| [IsNotEqualTo(IGpuBigIntVectorExpression, IGpuBigIntVectorExpression)](#isnotequaltoigpubigintvectorexpression-igpubigintvectorexpression) | Evaluates if elements in the left vector are not equal to the right vector element-wise. |
| [Max(IGpuBigIntVectorExpression)](#maxigpubigintvectorexpression) | Computes the maximum value of a vectorized expression. |
| [Min(IGpuBigIntVectorExpression)](#minigpubigintvectorexpression) | Computes the minimum value of a vectorized expression. |
| [Not(IGpuBoolVectorExpression)](#notigpuboolvectorexpression) | Performs an element-wise logical NOT on a boolean vector expression. |
| [Or(IGpuBoolVectorExpression, IGpuBoolVectorExpression)](#origpuboolvectorexpression-igpuboolvectorexpression) | Performs an element-wise logical OR on two boolean vector expressions. |
| [SelectAdd(IGpuBigIntVectorExpression, GpuBigInt)](#selectaddigpubigintvectorexpression-gpubigint) | Adds a scalar to each element of a vectorized expression. |
| [SelectMultiply(IGpuBigIntVectorExpression, GpuBigInt)](#selectmultiplyigpubigintvectorexpression-gpubigint) | Multiplies each element of a vectorized expression by a scalar on GPU device. |
| [SelectSubtract(IGpuBigIntVectorExpression, GpuBigInt)](#selectsubtractigpubigintvectorexpression-gpubigint) | Subtracts a scalar from each element of a vectorized expression. |
| [Skip(IGpuBigIntVectorExpression, Int32)](#skipigpubigintvectorexpression-int32) | Bypasses a specified number of elements in a sequence and returns the remaining elements. |
| [Sum(IGpuBigIntVectorExpression)](#sumigpubigintvectorexpression) | Computes the parallel sum reduction of a vectorized expression. |
| [Take(IGpuBigIntVectorExpression, Int32)](#takeigpubigintvectorexpression-int32) | Returns a specified number of contiguous elements from the start of a sequence. |
| [Where(IGpuBigIntVectorExpression, IGpuBoolVectorExpression)](#whereigpubigintvectorexpression-igpuboolvectorexpression) | Filters a vectorized sequence of values based on a predicate (GPU stream compaction). |
| [ZipAdd(IGpuBigIntVectorExpression, IGpuBigIntVectorExpression)](#zipaddigpubigintvectorexpression-igpubigintvectorexpression) | Adds two vectorized expressions element-wise. |
| [ZipMultiply(IGpuBigIntVectorExpression, IGpuBigIntVectorExpression)](#zipmultiplyigpubigintvectorexpression-igpubigintvectorexpression) | Multiplies the first vectorized expression by the second element-wise. |
| [ZipSubtract(IGpuBigIntVectorExpression, IGpuBigIntVectorExpression)](#zipsubtractigpubigintvectorexpression-igpubigintvectorexpression) | Subtracts the second vectorized expression from the first element-wise. |

## AsVectorizedEnumerable(GpuBigIntArray)

Converts a `GpuBigIntArray` into a vectorized deferred expression leaf node.

```csharp
public static IGpuBigIntVectorExpression AsVectorizedEnumerable(this GpuBigIntArray source);
```

## Where(IGpuBigIntVectorExpression, IGpuBoolVectorExpression)

Filters a vectorized sequence of values based on a predicate. Executes a Stream Compaction (`Where`) operation on the GPU.

```csharp
public static IGpuBigIntVectorExpression Where(
    this IGpuBigIntVectorExpression source,
    IGpuBoolVectorExpression predicate);
```

## SelectAdd / SelectSubtract / SelectMultiply

Applies a scalar addition, subtraction, or multiplication to each element of a vectorized expression using a grid-stride parallel kernel.

```csharp
public static IGpuBigIntVectorExpression SelectAdd(this IGpuBigIntVectorExpression source, GpuBigInt scalar);
public static IGpuBigIntVectorExpression SelectSubtract(this IGpuBigIntVectorExpression source, GpuBigInt scalar);
public static IGpuBigIntVectorExpression SelectMultiply(this IGpuBigIntVectorExpression source, GpuBigInt scalar);
```

## ZipAdd / ZipSubtract / ZipMultiply

Performs element-wise addition, subtraction, or multiplication across two vectorized expressions of equal length.

```csharp
public static IGpuBigIntVectorExpression ZipAdd(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right);
public static IGpuBigIntVectorExpression ZipSubtract(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right);
public static IGpuBigIntVectorExpression ZipMultiply(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right);
```

## Skip / Take

Slices a vectorized sequence by bypassing `count` leading elements (`Skip`) or taking `count` contiguous elements from the start (`Take`). Throws `ArgumentNullException` if `source` is `null`.

```csharp
public static IGpuBigIntVectorExpression Skip(this IGpuBigIntVectorExpression source, int count);
public static IGpuBigIntVectorExpression Take(this IGpuBigIntVectorExpression source, int count);
```

## Sum / Max / Min / Count

Computes parallel reductions over all elements in a vectorized expression, returning a deferred scalar [GpuBigIntExpr](GpuBigIntExpr.md).

```csharp
public static GpuBigIntExpr Sum(this IGpuBigIntVectorExpression source);
public static GpuBigIntExpr Max(this IGpuBigIntVectorExpression source);
public static GpuBigIntExpr Min(this IGpuBigIntVectorExpression source);
public static GpuBigIntExpr Count(this IGpuBigIntVectorExpression source);
```

## Element-Wise Comparisons (IsEqualTo, IsNotEqualTo, IsGreaterThan, IsGreaterThanOrEqualTo, IsLessThan, IsLessThanOrEqualTo)

Compares each element of a vectorized expression against either a `GpuBigInt` scalar or another `IGpuBigIntVectorExpression`, returning an `IGpuBoolVectorExpression`.

```csharp
public static IGpuBoolVectorExpression IsEqualTo(this IGpuBigIntVectorExpression source, GpuBigInt scalar);
public static IGpuBoolVectorExpression IsNotEqualTo(this IGpuBigIntVectorExpression source, GpuBigInt scalar);
public static IGpuBoolVectorExpression IsGreaterThan(this IGpuBigIntVectorExpression source, GpuBigInt scalar);
public static IGpuBoolVectorExpression IsGreaterThanOrEqualTo(this IGpuBigIntVectorExpression source, GpuBigInt scalar);
public static IGpuBoolVectorExpression IsLessThan(this IGpuBigIntVectorExpression source, GpuBigInt scalar);
public static IGpuBoolVectorExpression IsLessThanOrEqualTo(this IGpuBigIntVectorExpression source, GpuBigInt scalar);

public static IGpuBoolVectorExpression IsEqualTo(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right);
public static IGpuBoolVectorExpression IsNotEqualTo(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right);
public static IGpuBoolVectorExpression IsGreaterThan(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right);
public static IGpuBoolVectorExpression IsGreaterThanOrEqualTo(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right);
public static IGpuBoolVectorExpression IsLessThan(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right);
public static IGpuBoolVectorExpression IsLessThanOrEqualTo(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right);
```

## Boolean Combinators and Quantifiers (And, Or, Not, Any, All)

Combines boolean vector expressions element-wise (`And`, `Or`, `Not`) or reduces a boolean vector expression to a deferred scalar [IGpuBoolExpr](BoolVectorExpressions.md) (`Any`, `All`).

```csharp
public static IGpuBoolVectorExpression And(this IGpuBoolVectorExpression left, IGpuBoolVectorExpression right);
public static IGpuBoolVectorExpression Or(this IGpuBoolVectorExpression left, IGpuBoolVectorExpression right);
public static IGpuBoolVectorExpression Not(this IGpuBoolVectorExpression source);
public static IGpuBoolExpr Any(this IGpuBoolVectorExpression source);
public static IGpuBoolExpr All(this IGpuBoolVectorExpression source);
```

## Remarks

Unlike standard LINQ or scalar expression trees that evaluate element-by-element, `GpuBigIntVectorLinqExtensions` constructs execution plans that operate directly over contiguous arrays. Reduction operations (`Sum`, `Max`, `Min`, `Count`) automatically partition the workload into multi-pass chunk kernels, scaling efficiently up to millions of elements.

Because `Sum`, `Max`, `Min`, and `Count` return a [GpuBigIntExpr](GpuBigIntExpr.md), the entire vector reduction pipeline can be compiled directly into a native CUDA Graph:
- **One-Liner Evaluation:** Chain `.EvaluateWithCudaGraph(workspace)` to capture, instantiate, execute, and destroy a CUDA Graph in a single statement.
- **Precompiled Reusable Execution:** Chain `.CompileToGraph(workspace)` to produce a reusable [GpuBigIntExecutableGraph](GpuBigIntExecutableGraph.md) that can be dispatched repeatedly via `.Execute()` or `.ExecuteAsync()` with zero CPU launch latency.

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
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

using var workspace = new GpuWorkspace(65536, GpuExecutionEngine.GSharpAot);
using var array = new GpuBigIntArray(workspace, length: 256, wordsPerElement: 4);
using var scalarUint = GpuBigUInt.FromBigInteger(workspace, new BigInteger(7), minWords: 4);
var scalar = new GpuBigInt(scalarUint, isNegative: false);

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
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

using var workspace = new GpuWorkspace(65536, GpuExecutionEngine.GSharpAot);
using var array = new GpuBigIntArray(workspace, length: 256, wordsPerElement: 4);
using var scalarUint = GpuBigUInt.FromBigInteger(workspace, new BigInteger(7), minWords: 4);
var scalar = new GpuBigInt(scalarUint, isNegative: false);

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
- [IGpuBoolExpr and VectorCompareOperator](BoolVectorExpressions.md)
