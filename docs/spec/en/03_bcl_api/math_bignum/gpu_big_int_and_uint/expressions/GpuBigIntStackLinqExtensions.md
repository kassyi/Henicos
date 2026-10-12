---
title: "GpuBigIntStackLinqExtensions Class (G#)"
site: "G# Documentation"
published: "2026-10-12T00:00:00.000Z"
language: "en"
description: "Provides extension methods for zero-allocation stack-based parallel LINQ operations on GPU BigNum types."
---

## GpuBigIntStackLinqExtensions Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Provides extension methods for zero-allocation stack-based LINQ operations (StackLinq) on GPU BigNum types. These extensions enable the construction of parallel evaluation trees (such as balanced binary reductions) over `ParallelStackEnumerable` sequences, yielding deferred `GpuBigIntExpr` expressions.

```csharp
public static class GpuBigIntStackLinqExtensions
```

## Methods

| Name                                                                                                                | Description                                                                                                            |
| ------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------- |
| [Aggregate&lt;TEnumerator&gt;(...)](#aggregatetenumeratorparallelstackenumerabletenumerator-gpubigint-funcgpubigintexpr-gpubigintexpr-gpubigintexpr) | Applies an accumulator function over a parallel sequence by constructing a balanced binary evaluation tree.            |
| [Average&lt;TEnumerator&gt;(...)](#averagetenumeratorparallelstackenumerabletenumerator-gpubigint)                   | Computes the average of a parallel sequence of values.                                                                 |
| [Count&lt;TEnumerator&gt;(...)](#counttenumeratorparallelstackenumerabletenumerator-gpubigint)                       | Computes the number of elements in a parallel sequence.                                                                |
| [Max&lt;TEnumerator&gt;(...)](#maxtenumeratorparallelstackenumerabletenumerator-gpubigint)                           | Computes the maximum of a parallel sequence by constructing a balanced binary evaluation tree.                         |
| [Min&lt;TEnumerator&gt;(...)](#mintenumeratorparallelstackenumerabletenumerator-gpubigint)                           | Computes the minimum of a parallel sequence by constructing a balanced binary evaluation tree.                         |
| [Select&lt;TEnumerator&gt;(...)](#selecttenumeratorparallelstackenumerabletenumerator-gpubigint-funcgpubigint-gpubigintexpr) | Projects each element of a parallel sequence into a new deferred GPU BigInt expression.                                |
| [Sum&lt;TEnumerator&gt;(...)](#sumtenumeratorparallelstackenumerabletenumerator-gpubigint)                           | Computes the sum of a parallel sequence by constructing a balanced binary evaluation tree.                             |
| [Where&lt;TEnumerator&gt;(...)](#wheretenumeratorparallelstackenumerabletenumerator-gpubigint-funcgpubigint-bool)     | Filters a parallel sequence of values based on a predicate.                                                            |

## Aggregate&lt;TEnumerator&gt;(ParallelStackEnumerable&lt;TEnumerator, GpuBigInt&gt;, Func&lt;GpuBigIntExpr, GpuBigIntExpr, GpuBigIntExpr&gt;)

Applies an accumulator function over a parallel sequence of `GpuBigInt` values by constructing a balanced binary evaluation tree.

```csharp
public static GpuBigIntExpr Aggregate<TEnumerator>(
    this ParallelStackEnumerable<TEnumerator, GpuBigInt> source,
    Func<GpuBigIntExpr, GpuBigIntExpr, GpuBigIntExpr> func)
    where TEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct;
```

#### Parameters

`source` _ParallelStackEnumerable&lt;TEnumerator, GpuBigInt&gt;_<br>
A parallel sequence of values to aggregate.

`func` _Func&lt;GpuBigIntExpr, GpuBigIntExpr, GpuBigIntExpr&gt;_<br>
An accumulator function to be invoked on each pair of expressions.

#### Returns

_GpuBigIntExpr_<br>
A deferred expression tree representing the parallel aggregation.

## Average&lt;TEnumerator&gt;(ParallelStackEnumerable&lt;TEnumerator, GpuBigInt&gt;)

Computes the average of a parallel sequence of `GpuBigInt` values by constructing a balanced binary evaluation tree for the sum, and dividing by the counted number of elements.

```csharp
public static GpuBigIntExpr Average<TEnumerator>(
    this ParallelStackEnumerable<TEnumerator, GpuBigInt> source)
    where TEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct;
```

#### Parameters

`source` _ParallelStackEnumerable&lt;TEnumerator, GpuBigInt&gt;_<br>
A parallel sequence of values to calculate the average of.

#### Returns

_GpuBigIntExpr_<br>
A deferred expression tree representing the parallel average.

## Count&lt;TEnumerator&gt;(ParallelStackEnumerable&lt;TEnumerator, GpuBigInt&gt;)

Computes the number of elements in a parallel sequence.

```csharp
public static int Count<TEnumerator>(
    this ParallelStackEnumerable<TEnumerator, GpuBigInt> source)
    where TEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct;
```

#### Parameters

`source` _ParallelStackEnumerable&lt;TEnumerator, GpuBigInt&gt;_<br>
A parallel sequence.

#### Returns

_int_<br>
The number of elements in the sequence.

## Max&lt;TEnumerator&gt;(ParallelStackEnumerable&lt;TEnumerator, GpuBigInt&gt;)

Computes the maximum of a parallel sequence of `GpuBigInt` values by constructing a balanced binary evaluation tree.

```csharp
public static GpuBigIntExpr Max<TEnumerator>(
    this ParallelStackEnumerable<TEnumerator, GpuBigInt> source)
    where TEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct;
```

#### Parameters

`source` _ParallelStackEnumerable&lt;TEnumerator, GpuBigInt&gt;_<br>
A parallel sequence of values that are used to calculate a max.

#### Returns

_GpuBigIntExpr_<br>
A deferred expression tree representing the parallel max.

## Min&lt;TEnumerator&gt;(ParallelStackEnumerable&lt;TEnumerator, GpuBigInt&gt;)

Computes the minimum of a parallel sequence of `GpuBigInt` values by constructing a balanced binary evaluation tree.

```csharp
public static GpuBigIntExpr Min<TEnumerator>(
    this ParallelStackEnumerable<TEnumerator, GpuBigInt> source)
    where TEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct;
```

#### Parameters

`source` _ParallelStackEnumerable&lt;TEnumerator, GpuBigInt&gt;_<br>
A parallel sequence of values that are used to calculate a min.

#### Returns

_GpuBigIntExpr_<br>
A deferred expression tree representing the parallel min.

## Select&lt;TEnumerator&gt;(ParallelStackEnumerable&lt;TEnumerator, GpuBigInt&gt;, Func&lt;GpuBigInt, GpuBigIntExpr&gt;)

Projects each element of a parallel sequence into a new deferred GPU BigInt expression.

```csharp
public static ParallelStackEnumerable<SelectEnumerator<TEnumerator, GpuBigInt, GpuBigIntExpr>, GpuBigIntExpr> Select<TEnumerator>(
    this ParallelStackEnumerable<TEnumerator, GpuBigInt> source,
    Func<GpuBigInt, GpuBigIntExpr> selector)
    where TEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct;
```

#### Parameters

`source` _ParallelStackEnumerable&lt;TEnumerator, GpuBigInt&gt;_<br>
The sequence of elements to invoke a transform function on.

`selector` _Func&lt;GpuBigInt, GpuBigIntExpr&gt;_<br>
A transform function to apply to each source element.

#### Returns

_ParallelStackEnumerable&lt;SelectEnumerator&lt;TEnumerator, GpuBigInt, GpuBigIntExpr&gt;, GpuBigIntExpr&gt;_<br>
A new parallel sequence containing the deferred expressions.

## Sum&lt;TEnumerator&gt;(ParallelStackEnumerable&lt;TEnumerator, GpuBigInt&gt;)

Computes the sum of a parallel sequence of `GpuBigInt` values by constructing a balanced binary evaluation tree.

```csharp
public static GpuBigIntExpr Sum<TEnumerator>(
    this ParallelStackEnumerable<TEnumerator, GpuBigInt> source)
    where TEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct;
```

#### Parameters

`source` _ParallelStackEnumerable&lt;TEnumerator, GpuBigInt&gt;_<br>
A parallel sequence of values that are used to calculate a sum.

#### Returns

_GpuBigIntExpr_<br>
A deferred expression tree representing the parallel sum.

## Where&lt;TEnumerator&gt;(ParallelStackEnumerable&lt;TEnumerator, GpuBigInt&gt;, Func&lt;GpuBigInt, bool&gt;)

Filters a parallel sequence of values based on a predicate.

```csharp
public static ParallelStackEnumerable<WhereEnumerator<TEnumerator, GpuBigInt>, GpuBigInt> Where<TEnumerator>(
    this ParallelStackEnumerable<TEnumerator, GpuBigInt> source,
    Func<GpuBigInt, bool> predicate)
    where TEnumerator : struct, IStackEnumerator<GpuBigInt>, allows ref struct;
```

#### Parameters

`source` _ParallelStackEnumerable&lt;TEnumerator, GpuBigInt&gt;_<br>
An enumerable to filter.

`predicate` _Func&lt;GpuBigInt, bool&gt;_<br>
A function to test each element for a condition.

#### Returns

_ParallelStackEnumerable&lt;WhereEnumerator&lt;TEnumerator, GpuBigInt&gt;, GpuBigInt&gt;_<br>
A new parallel sequence that contains elements from the input sequence that satisfy the condition.

## Remarks

> [!NOTE]
> All reduction operations (`Sum`, `Max`, `Min`, `Aggregate`, `Average`) construct balanced binary evaluation trees out of `GpuBigIntExpr` elements to optimize the reduction path depth. Instead of an $O(N)$ sequential chain, the resulting AST reduces evaluating depth to $O(\log N)$, allowing parallel device kernels to operate effectively over large enumerations.

## Examples

### Building a Balanced Binary Tree Reduction with StackLinq

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath, Kassyi.ZeroAlloc.Linq

```csharp
using System;
using System.Linq;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;
using Kassyi.ZeroAlloc.Linq;

// Execution Context: Host (CPU)
using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);

// Pre-allocate values using System.Linq
var bigInts = Enumerable.Range(1, 4)
    .Select(i => new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, new BigInteger(i), minWords: 2), false))
    .ToArray();

try
{
    // Convert array to ParallelStackEnumerable, build AST, and evaluate on GPU
    using var dest = new GpuBigUInt(workspace, 2);
    
    var result = bigInts
        .AsParallelStackEnumerable()
        .Sum()
        .Evaluate(dest);

    Console.WriteLine($"Reduction result: {result.Value.ToBigInteger(result.IsNegative)}");
}
finally
{
    foreach(var val in bigInts)
    {
        val.Dispose();
    }
}
```

## See also

### BCL API > Math Bignum
- [GpuBigIntExpr Struct](GpuBigIntExpr.md)
- [GpuBigIntVectorLinqExtensions Class](GpuBigIntVectorLinqExtensions.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
