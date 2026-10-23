---
title: "SkipEnumerator<TEnumerator, TSource> Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "An enumerator that bypasses a specified number of elements in a sequence and then returns the remaining elements with zero heap allocations."
---

## SkipEnumerator&lt;TEnumerator, TSource&gt; Struct

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

An enumerator that bypasses a specified number of elements in a sequence and then returns the remaining elements with zero heap allocations.

```csharp
public ref struct SkipEnumerator<TEnumerator, TSource> : IStackEnumerator<TSource>
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
| [SkipEnumerator(TEnumerator, Int32)](#skipenumeratortenumerator-int32) | Initializes a new instance of the `SkipEnumerator<TEnumerator, TSource>` struct. |

## SkipEnumerator(TEnumerator, Int32)

Initializes a new instance of the `SkipEnumerator<TEnumerator, TSource>` struct.

```csharp
public SkipEnumerator(TEnumerator source, int count);
```

#### Parameters

`source` _TEnumerator_<br>
The underlying enumerator providing source elements.

`count` _Int32_<br>
The number of elements to skip before yielding elements.

## Methods

| Name | Description |
| --- | --- |
| [TryGetNext(out TSource)](#trygetnextout-tsource) | Advances the enumerator to the next element of the sequence after skipping. |

## TryGetNext(out TSource)

Advances the enumerator to the next element of the sequence after skipping.

```csharp
public bool TryGetNext(out TSource value);
```

#### Parameters

`value` _TSource_<br>
When this method returns `true`, contains the next element in the sequence; otherwise, the default value of `TSource`.

#### Returns

_Boolean_<br>
`true` if the enumerator was successfully advanced to the next element; `false` if the end of the sequence was reached.

## Remarks

`SkipEnumerator<TEnumerator, TSource>` operates lazily with $O(1)$ auxiliary space and zero heap allocations. Negative `count` values are clamped to `0`.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU) / Device (GPU)
using System;
using Kassyi.ZeroAlloc.Linq;

ReadOnlySpan<int> numbers = [10, 20, 30, 40, 50];
StackEnumerable<SkipEnumerator<SpanEnumerator<int>, int>, int> skipped =
    numbers
        .AsStackEnumerable()
        .Skip(2);

Console.WriteLine($"Sum after skipping 2: {skipped.Sum()}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [Skip Method](Skip.md)
- [SkipLast Method](SkipLast.md)
