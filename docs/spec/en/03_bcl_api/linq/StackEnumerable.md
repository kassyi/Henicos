---
title: "StackEnumerable<TEnumerator, T> Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Represents a stack-allocated, zero-allocation sequence wrapping a struct-based enumerator."
---

## StackEnumerable&lt;TEnumerator, T&gt; Struct

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Represents a stack-allocated, zero-allocation sequence wrapping a struct-based enumerator.

```csharp
public readonly ref struct StackEnumerable<TEnumerator, T>
    where TEnumerator : struct, IStackEnumerator<T>, allows ref struct
```

#### Type Parameters

`TEnumerator`<br>
The underlying enumerator type implementing `IStackEnumerator<T>`.

`T`<br>
The type of elements in the sequence.

## Constructors

| Name | Description |
| --- | --- |
| [StackEnumerable(TEnumerator)](#stackenumerabletenumerator) | Initializes a new instance of the `StackEnumerable<TEnumerator, T>` struct wrapping the specified enumerator. |

## StackEnumerable(TEnumerator)

Initializes a new instance of the `StackEnumerable<TEnumerator, T>` struct wrapping the specified enumerator.

```csharp
public StackEnumerable(TEnumerator enumerator);
```

#### Parameters

`enumerator` _TEnumerator_<br>
The struct enumerator to wrap.

## Fields

| Name | Description |
| --- | --- |
| [Enumerator](#enumerator) | The underlying enumerator that drives this sequence. |

## Enumerator

The underlying enumerator that drives this sequence.

```csharp
public readonly TEnumerator Enumerator;
```

## Remarks

`StackEnumerable<TEnumerator, T>` is a `readonly ref struct` wrapper returned by [AsStackEnumerable](AsStackEnumerable.md) and intermediate StackLinq operators (`Where`, `Select`, `Skip`, `Take`, etc.). Because it is a `ref struct`, it cannot be boxed or stored on the managed heap, guaranteeing zero GC pressure.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU) / Device (GPU)
using System;
using Kassyi.ZeroAlloc.Linq;

ReadOnlySpan<int> data = [2, 4, 6, 8];
var enumerable = new StackEnumerable<SpanEnumerator<int>, int>(new SpanEnumerator<int>(data));

Console.WriteLine($"Count: {enumerable.Count()}, Sum: {enumerable.Sum()}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [IStackEnumerator&lt;T&gt; Interface](IStackEnumerator.md)
- [AsStackEnumerable Method](AsStackEnumerable.md)
