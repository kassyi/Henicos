---
title: "ParallelStackEnumerable<TEnumerator, TSource> Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "A zero-allocation marker struct indicating that subsequent operations on the stack enumerator should be executed in parallel."
---

## ParallelStackEnumerable&lt;TEnumerator, TSource&gt; Struct

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

A zero-allocation marker struct indicating that the subsequent operations on the enumerator should be executed in parallel (e.g., via CUDA cooperative thread groups or GPU vector reductions).

```csharp
public readonly ref struct ParallelStackEnumerable<TEnumerator, TSource>(TEnumerator enumerator)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
```

#### Type Parameters

`TEnumerator`<br>
The type of the underlying stack enumerator.

`TSource`<br>
The type of elements in the sequence.

## Constructors

| Name | Description |
| --- | --- |
| [ParallelStackEnumerable(TEnumerator)](#parallelstackenumerabletenumerator) | Initializes a new `ParallelStackEnumerable<TEnumerator, TSource>` wrapping the specified stack enumerator. |

## ParallelStackEnumerable(TEnumerator)

Initializes a new `ParallelStackEnumerable<TEnumerator, TSource>` wrapping the specified stack enumerator.

```csharp
public ParallelStackEnumerable(TEnumerator enumerator);
```

#### Parameters

`enumerator` _TEnumerator_<br>
The underlying stack enumerator.

## Fields

| Name | Description |
| --- | --- |
| [Enumerator](#enumerator) | Gets the underlying stack enumerator. |

## Enumerator

Gets the underlying stack enumerator.

```csharp
public readonly TEnumerator Enumerator;
```

## Remarks

`ParallelStackEnumerable<TEnumerator, TSource>` is returned by `AsParallel()` on a stack enumerator and enables parallel terminal reductions (such as `Sum()`).

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU) / Device (GPU)
using System;
using Kassyi.ZeroAlloc.Linq;

ReadOnlySpan<int> values = [10, 20, 30, 40];
ParallelStackEnumerable<SpanEnumerator<int>, int> parallelSeq = values.AsStackEnumerable().AsParallel();

int sum = parallelSeq.Sum();
Console.WriteLine($"Parallel sum: {sum}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [IStackEnumerator&lt;T&gt; Interface](IStackEnumerator.md)
- [Sum Method](Sum.md)
