---
title: "WhereEnumerator<TEnumerator, TSource> Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Filters a sequence of elements based on a predicate with zero heap allocations."
---

## WhereEnumerator&lt;TEnumerator, TSource&gt; Struct

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Filters a sequence of elements based on a predicate with zero heap allocations.

```csharp
public ref struct WhereEnumerator<TEnumerator, TSource> : IStackEnumerator<TSource>
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
| [WhereEnumerator(TEnumerator, Func&lt;TSource, Boolean&gt;)](#whereenumeratortenumerator-functsource-boolean) | Initializes a new instance of the `WhereEnumerator<TEnumerator, TSource>` struct. |

## WhereEnumerator(TEnumerator, Func&lt;TSource, Boolean&gt;)

Initializes a new instance of the `WhereEnumerator<TEnumerator, TSource>` struct.

```csharp
public WhereEnumerator(TEnumerator source, Func<TSource, bool> predicate);
```

#### Parameters

`source` _TEnumerator_<br>
The underlying enumerator providing source elements.

`predicate` _Func&lt;TSource, Boolean&gt;_<br>
A function to test each element for a condition.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `predicate` is `null`.

## Methods

| Name | Description |
| --- | --- |
| [TryGetNext(out TSource)](#trygetnextout-tsource) | Attempts to advance the enumerator to the next element that satisfies the predicate. |

## TryGetNext(out TSource)

Attempts to advance the enumerator to the next element that satisfies the predicate.

```csharp
public bool TryGetNext(out TSource current);
```

#### Parameters

`current` _TSource_<br>
When this method returns `true`, contains the next element satisfying the predicate; otherwise, the default value of `TSource`.

#### Returns

_Boolean_<br>
`true` if a matching element is found; `false` if the end of the sequence was reached without finding another match.

## Remarks

`WhereEnumerator<TEnumerator, TSource>` evaluates elements lazily with $O(1)$ auxiliary space and zero heap allocations.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU) / Device (GPU)
using System;
using Kassyi.ZeroAlloc.Linq;

ReadOnlySpan<int> numbers = [1, 2, 3, 4, 5, 6];
StackEnumerable<WhereEnumerator<SpanEnumerator<int>, int>, int> evens =
    numbers
        .AsStackEnumerable()
        .Where(x => x % 2 == 0);

Console.WriteLine($"Evens sum: {evens.Sum()}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [Where Method](Where.md)
- [WhereAtEnumerator&lt;TEnumerator, TSource&gt; Struct](WhereAtEnumerator.md)
