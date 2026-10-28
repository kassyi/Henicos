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

The number of selected elements is computed on the device, and the host does not wait for it: `Count`, `Sum`, `Max`, `Min`, `Any`, `All`, `Skip`, `Take` and the element-wise operations after a `Where` use only the selected elements, as `System.Linq` does. `Length` of the result throws `InvalidOperationException`; `Capacity` is the length of `source`, and `Evaluate` reads the count (one synchronization) and returns exactly the selected elements. `predicate` must have the length of `source` (`ArgumentException`).

Two `Where` calls on different inputs cannot be combined element-wise, because their counts are known only on the device (`NotSupportedException`): write `v.Where(p.And(q))`. `v.Where(p).Where(m)` works when `m` has the capacity's length.

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

`Sum`, `Max`, and `Min` return a value with the same number of words as the elements (`WordsPerElement`):

- The reduction accumulates in one word more than the elements, so values whose top bit is set and intermediate sums that leave the element width are exact.
- `Sum` throws `OverflowException` on evaluation when the final sum does not fit in `WordsPerElement` words. Only the final sum counts: unlike the checked sum of `System.Linq`, a running sum that leaves the element width and comes back (`[2^(32N) - 1, 1, -1]`) does not throw. `Max` and `Min` always fit.
- `Evaluate(destination)` writes the magnitude of the result into `destination` and returns a `GpuBigInt` that wraps `destination` with the sign of the result. `destination` must have `WordsPerElement` words (`ArgumentException`).
- `Evaluate(workspace)` allocates a new buffer for the magnitude; the caller owns it and disposes it. If evaluation throws, that buffer is released.
- When `Evaluate(destination)` throws, `destination` is left as follows: for a destination of the wrong width (`ArgumentException`) it is not touched; for `OverflowException` its contents are unspecified (currently the low `WordsPerElement` words of the magnitude).
- Evaluation reads two words back from the device: a word with the sign bit and the overflow bit, and the number of reduced elements.
- An empty vector behaves as in `System.Linq`: `Max` and `Min` throw `InvalidOperationException` ("Sequence contains no elements") on evaluation without evaluating the vector or launching anything, so a destination is not touched, `Sum` is +0 (a destination is cleared), and `Count` is 0.

## Element-Wise Comparisons (IsEqualTo, IsNotEqualTo, IsGreaterThan, IsGreaterThanOrEqualTo, IsLessThan, IsLessThanOrEqualTo)

Compares each element of a vectorized expression against either a `GpuBigInt` scalar or another `IGpuBigIntVectorExpression`, returning an `IGpuBoolVectorExpression`.

The comparison is signed and agrees with `BigInteger.CompareTo`: `-100 < 50`, `-3 != 3`, and `-100 < -3`. A negative zero (sign set, magnitude 0) equals zero. The two sides may have different word counts: each side is read with its own word count, and the missing upper words of the narrower side are 0, so a 4-word `3` equals an 8-word `3`. A scalar is read with its own word count, which may be smaller or larger than the elements'. Two vectors must have the same length (`ArgumentException`).

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

`Count` returns a [GpuBigIntExpr](GpuBigIntExpr.md) that can be compiled into a native CUDA Graph; after a `Where`, every launch counts the selected elements again on the device:
- **One-Liner Evaluation:** Chain `.EvaluateWithCudaGraph(workspace)` to capture, instantiate, execute, and destroy a CUDA Graph in a single statement.
- **Precompiled Reusable Execution:** Chain `.CompileToGraph(workspace)` to produce a reusable [GpuBigIntExecutableGraph](GpuBigIntExecutableGraph.md) that can be dispatched repeatedly via `.Execute()` or `.ExecuteAsync()` with zero CPU launch latency.

`Sum`, `Max` and `Min` read their sign, emptiness and overflow back from the device, which a capture cannot record: evaluating them while the stream is capturing throws `NotSupportedException`. Evaluate them directly (`Evaluate`).

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

// Evaluate the entire vector pipeline. SelectMultiply widens each element to 4 + 4 = 8 words,
// and the sum has the same width as the elements it adds.
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

// Count the elements above the scalar with a one-liner CUDA Graph execution
using var graphResult = array.AsVectorizedEnumerable()
    .Where(array.AsVectorizedEnumerable().IsGreaterThan(scalar))
    .Count()
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
    .Where(array.AsVectorizedEnumerable().IsGreaterThan(scalar))
    .Count()
    .CompileToGraph(workspace);

// Execute the graph repeatedly with minimal driver launch latency; each launch counts again
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
