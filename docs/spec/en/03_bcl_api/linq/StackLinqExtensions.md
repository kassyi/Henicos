---
title: "StackLinqExtensions Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Provides static extension methods for zero-allocation, stack-based LINQ operations over arrays, spans, lists, and struct enumerators."
---

## StackLinqExtensions Class

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Provides a set of `static` extension methods for zero-allocation, stack-based LINQ operations over arrays, spans, lists, and struct enumerators.

```csharp
public static class StackLinqExtensions
```

## Methods

### Entry Points & Parallelization

| Name | Description |
| --- | --- |
| [AsStackEnumerable&lt;T&gt;(T[])](AsStackEnumerable.md) | Wraps a managed array into a zero-allocation `StackEnumerable<ArrayEnumerator<T>, T>`. |
| [AsStackEnumerable&lt;T&gt;(List&lt;T&gt;)](AsStackEnumerable.md) | Wraps a `List<T>` into a zero-allocation `StackEnumerable<SpanEnumerator<T>, T>` (Host CPU only). |
| [AsStackEnumerable&lt;T&gt;(Span&lt;T&gt;)](AsStackEnumerable.md) | Wraps a `Span<T>` into a zero-allocation `StackEnumerable<SpanEnumerator<T>, T>`. |
| [AsStackEnumerable&lt;T&gt;(ReadOnlySpan&lt;T&gt;)](AsStackEnumerable.md) | Wraps a `ReadOnlySpan<T>` into a zero-allocation `StackEnumerable<SpanEnumerator<T>, T>`. |
| `AsParallel<TEnumerator, TSource>(StackEnumerable<TEnumerator, TSource>)` | Wraps a sequence in [ParallelStackEnumerable&lt;TEnumerator, TSource&gt;](ParallelStackEnumerable.md) for parallel reduction. |

### Filtering & Projection

| Name | Description |
| --- | --- |
| [Where&lt;TEnumerator, TSource&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, Func&lt;TSource, Boolean&gt;)](Where.md) | Filters a sequence of values based on a predicate (`WhereEnumerator`). |
| [Where&lt;TEnumerator, TSource&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, Func&lt;TSource, Int32, Boolean&gt;)](Where.md) | Filters a sequence of values based on an index-aware predicate (`WhereAtEnumerator`). |
| [Select&lt;TEnumerator, TSource, TResult&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, Func&lt;TSource, TResult&gt;)](Select.md) | Projects each element of a sequence into a new form (`SelectEnumerator`). |
| [Select&lt;TEnumerator, TSource, TResult&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, Func&lt;TSource, Int32, TResult&gt;)](Select.md) | Projects each element of a sequence into a new form incorporating the element's index (`SelectAtEnumerator`). |

### Partitioning & Conditional Slicing

| Name | Description |
| --- | --- |
| [Take&lt;TEnumerator, TSource&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, Int32)](Take.md) | Returns a specified number of contiguous elements from the start of a sequence (`TakeEnumerator`). |
| [TakeLast&lt;TSource&gt;(StackEnumerable&lt;SpanEnumerator&lt;TSource&gt;, TSource&gt;, Int32)](TakeLast.md) | Returns a specified number of contiguous elements from the end of a span-backed sequence. |
| [TakeLast&lt;TSource&gt;(StackEnumerable&lt;ArrayEnumerator&lt;TSource&gt;, TSource&gt;, Int32)](TakeLast.md) | Returns a specified number of contiguous elements from the end of an array-backed sequence. |
| [TakeWhile&lt;TEnumerator, TSource&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, Func&lt;TSource, Boolean&gt;)](TakeWhile.md) | Returns elements from a sequence as long as a specified condition is true (`TakeWhileEnumerator`). |
| [TakeWhile&lt;TEnumerator, TSource&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, Func&lt;TSource, Int32, Boolean&gt;)](TakeWhile.md) | Returns elements from a sequence as long as an index-aware condition is true (`TakeWhileAtEnumerator`). |
| [Skip&lt;TEnumerator, TSource&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, Int32)](Skip.md) | Bypasses a specified number of elements in a sequence (`SkipEnumerator`). |
| [SkipLast&lt;TSource&gt;(StackEnumerable&lt;SpanEnumerator&lt;TSource&gt;, TSource&gt;, Int32)](SkipLast.md) | Bypasses a specified number of contiguous elements from the end of a span-backed sequence. |
| [SkipLast&lt;TSource&gt;(StackEnumerable&lt;ArrayEnumerator&lt;TSource&gt;, TSource&gt;, Int32)](SkipLast.md) | Bypasses a specified number of contiguous elements from the end of an array-backed sequence. |
| [SkipWhile&lt;TEnumerator, TSource&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, Func&lt;TSource, Boolean&gt;)](SkipWhile.md) | Bypasses elements in a sequence as long as a condition is true (`SkipWhileEnumerator`). |
| [SkipWhile&lt;TEnumerator, TSource&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, Func&lt;TSource, Int32, Boolean&gt;)](SkipWhile.md) | Bypasses elements in a sequence as long as an index-aware condition is true (`SkipWhileAtEnumerator`). |

### Aggregation & Reduction

| Name | Description |
| --- | --- |
| [Aggregate&lt;TEnumerator, TSource&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, Func&lt;TSource, TSource, TSource&gt;)](Aggregate.md) | Applies an accumulator function over a sequence. |
| [Aggregate&lt;TEnumerator, TSource, TAccumulate&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, TAccumulate, Func&lt;TAccumulate, TSource, TAccumulate&gt;)](Aggregate.md) | Applies an accumulator function over a sequence starting from `seed`. |
| [Count&lt;TEnumerator, TSource&gt;(...)](Count.md) | Returns the number of elements in a sequence (unconditional, predicate, or index-aware predicate). |
| [Sum(...)](Sum.md) | Computes the sum of a sequence of `int`, `float`, `double`, or `INumberBase<TSource>` values (including selector overloads and `ParallelStackEnumerable` reduction). |
| `Max<TEnumerator>(StackEnumerable<TEnumerator, int|float|double>)` | Returns the maximum value in a numeric sequence. |
| `Min<TEnumerator>(StackEnumerable<TEnumerator, int|float|double>)` | Returns the minimum value in a numeric sequence. |
| `Average<TEnumerator>(StackEnumerable<TEnumerator, int|float|double>)` | Computes the arithmetic mean of a numeric sequence. |

### Element Access, Quantifiers & Materialization

| Name | Description |
| --- | --- |
| [Any&lt;TEnumerator, TSource&gt;(...)](Any.md) | Determines whether any element of a sequence exists or satisfies a condition. |
| [All&lt;TEnumerator, TSource&gt;(...)](All.md) | Determines whether all elements of a sequence satisfy a condition. |
| [Contains&lt;TEnumerator, TSource&gt;(...)](Contains.md) | Determines whether a sequence contains a specified element. |
| [First&lt;TEnumerator, TSource&gt;(...)](First.md) / `FirstOrDefault<TEnumerator, TSource>(...)` | Returns the first element of a sequence (or default value if empty). |
| `Last<TEnumerator, TSource>(...)` / `LastOrDefault<TEnumerator, TSource>(...)` | Returns the last element of a sequence (or default value if empty). |
| [Single&lt;TEnumerator, TSource&gt;(...)](Single.md) / `SingleOrDefault<TEnumerator, TSource>(...)` | Returns the only element of a sequence (or default value if empty), throwing if more than one element exists. |
| `ElementAt<TEnumerator, TSource>(StackEnumerable<TEnumerator, TSource>, Int32)` / `ElementAtOrDefault(...)` | Returns the element at a specified zero-based index. |
| `CopyTo<TEnumerator, TSource>(StackEnumerable<TEnumerator, TSource>, Span<TSource>)` | Copies the elements of the sequence into a destination `Span<TSource>` with zero heap allocation and returns the number of elements written. |
| `ToArray<TEnumerator, TSource>(StackEnumerable<TEnumerator, TSource>)` | Materializes the sequence into a new managed array on the host CPU. |

## Remarks

All intermediate operators in `StackLinqExtensions` return a [StackEnumerable&lt;TEnumerator, T&gt;](StackEnumerable.md) value-type wrapper around a specialized `ref struct` enumerator. This design avoids boxing, interface virtual dispatch, and GC heap allocations on the host CPU while allowing the G# Source Generator to lower pipelines directly into register-bound CUDA C++ loops inside `[GpuKernel]` methods.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU) / Device (GPU)
using System;
using Kassyi.ZeroAlloc.Linq;

ReadOnlySpan<int> source = [3, 1, 4, 1, 5, 9, 2, 6];

var query = source.AsStackEnumerable()
    .Where(x => x > 2)
    .Skip(1)
    .Take(3)
    .Select(x => x * 10);

Span<int> buffer = stackalloc int[3];
int count = query.CopyTo(buffer);

Console.WriteLine($"Copied {count} items: [{buffer[0]}, {buffer[1]}, {buffer[2]}], Max={source.AsStackEnumerable().Max()}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [StackEnumerable&lt;TEnumerator, T&gt; Struct](StackEnumerable.md)
- [IStackEnumerator&lt;T&gt; Interface](IStackEnumerator.md)
