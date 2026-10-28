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

### In kernels

- **The meaning is the one of C#.** Every thread that calls `data.AsStackEnumerable().AsParallel().Sum()` receives the sum of the whole sequence, never a partial sum. An empty sequence sums to 0, and the sum wraps on overflow as StackLinq's `Sum` does.
- **`AsParallel()` lets G# choose how.** On CUDA, the threads of a block share the elements and combine their partial sums (every block sums the whole sequence) when this gives the same value: an `int`, `uint`, `long` or `ulong` sum whose source is an array or span parameter of the kernel, whose stages are `Select`, `Where` and `Skip` / `Take` / `SkipLast` / `TakeLast` before any `Where`, whose lambdas are expressions without side effects, whose inputs are the same in every thread of the block, and which every thread of the block reaches. Otherwise each thread sums on its own, and [GSH0099](../diagnostics/GSH0099.md) says why. The CPU dialects always sum in each thread; integer sums give the same bits either way.
- **Shared memory**: the shared form of a block uses static shared memory, 33 elements of the type of the sum (32 warp partials and the total), once per type in a kernel, whatever the number of calls. Count it against the shared memory of the kernel.
- **In a cooperative kernel** (`[GpuKernel(Cooperative = true)]`), when the inputs are also the same in every block, the threads of the whole grid share the elements: each block combines its part, the blocks meet at two grid syncs, and block 0 adds the block results in order. The launcher allocates the small scratch this needs (16 bytes per block) for each launch.
- **`float` and `double` sums run in each thread**, in order, on every path (the shared form, which changes the order of the additions, is not used for them yet).
- **Writes of other threads are not awaited.** The call does not synchronize before it reads; data that other threads write must be published first with `Gpu.SyncThreads()` or `Gpu.SyncGrid()`.
- **The number of lambda calls is not part of the promise**: the shared form calls a `Where` / `Select` lambda once per element instead of once per element per thread, which is why lambdas with side effects run in each thread.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU) / Device (GPU)
using System;
using Kassyi.ZeroAlloc.Linq;

ReadOnlySpan<int> values = [10, 20, 30, 40];
ParallelStackEnumerable<SpanEnumerator<int>, int> parallelSeq = values
    .AsStackEnumerable()
    .AsParallel();

int sum = parallelSeq.Sum();
Console.WriteLine($"Parallel sum: {sum}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [IStackEnumerator&lt;T&gt; Interface](IStackEnumerator.md)
- [Sum Method](Sum.md)
