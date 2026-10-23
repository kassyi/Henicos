---
title: "TakeEnumerator<TEnumerator, TSource> Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "An enumerator that returns a specified number of contiguous elements from the start of a sequence with zero heap allocations."
---

## TakeEnumerator&lt;TEnumerator, TSource&gt; Struct

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

An enumerator that returns a specified number of contiguous elements from the start of a sequence with zero heap allocations.

```csharp
public ref struct TakeEnumerator<TEnumerator, TSource> : IStackEnumerator<TSource>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
```

#### Type Parameters

`TEnumerator`<br>
The type of the underlying source enumerator.

`TSource`<br>
The type of the elements in the sequence.

## Constructors

| Name | Description |
| --- | --- |
| [TakeEnumerator(TEnumerator, Int32)](#takeenumeratortenumerator-int32) | Initializes a new instance of the `TakeEnumerator<TEnumerator, TSource>` struct. |

## TakeEnumerator(TEnumerator, Int32)

Initializes a new instance of the `TakeEnumerator<TEnumerator, TSource>` struct.

```csharp
public TakeEnumerator(TEnumerator source, int count);
```

#### Parameters

`source` _TEnumerator_<br>
The underlying enumerator providing source elements.

`count` _Int32_<br>
The number of elements to take from the start of the sequence.

## Methods

| Name | Description |
| --- | --- |
| [TryGetNext(out TSource)](#trygetnextout-tsource) | Advances the enumerator to the next element of the sequence until the specified count is reached. |

## TryGetNext(out TSource)

Advances the enumerator to the next element of the sequence until the specified count is reached.

```csharp
public bool TryGetNext(out TSource value);
```

#### Parameters

`value` _TSource_<br>
When this method returns `true`, contains the next element in the sequence; otherwise, the default value of `TSource`.

#### Returns

_Boolean_<br>
`true` if the enumerator was successfully advanced to the next element; `false` if the end of the sequence or the maximum count was reached.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU) / Device (GPU)
using System;
using Kassyi.ZeroAlloc.Linq;

ReadOnlySpan<int> numbers = [10, 20, 30, 40, 50];
StackEnumerable<TakeEnumerator<SpanEnumerator<int>, int>, int> firstThree =
    numbers
        .AsStackEnumerable()
        .Take(3);

Console.WriteLine($"Sum of first 3: {firstThree.Sum()}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [Take Method](Take.md)
- [TakeLast Method](TakeLast.md)
