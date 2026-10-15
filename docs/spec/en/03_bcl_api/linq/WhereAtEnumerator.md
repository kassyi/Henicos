---
title: "WhereAtEnumerator<TEnumerator, TSource> Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Filters a sequence of elements based on a predicate that incorporates the element's zero-based index."
---

## WhereAtEnumerator&lt;TEnumerator, TSource&gt; Struct

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Filters a sequence of elements based on a predicate that incorporates the element's zero-based index.

```csharp
public ref struct WhereAtEnumerator<TEnumerator, TSource> : IStackEnumerator<TSource>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
```

#### Type Parameters

`TEnumerator`<br>
The type of the underlying enumerator.

`TSource`<br>
The type of the elements to enumerate.

## Constructors

| Name | Description |
| --- | --- |
| [WhereAtEnumerator(TEnumerator, Func&lt;TSource, Int32, Boolean&gt;)](#whereatenumeratortenumerator-functsource-int32-boolean) | Initializes a new instance of the `WhereAtEnumerator<TEnumerator, TSource>` struct. |

## WhereAtEnumerator(TEnumerator, Func&lt;TSource, Int32, Boolean&gt;)

Initializes a new instance of the `WhereAtEnumerator<TEnumerator, TSource>` struct.

```csharp
public WhereAtEnumerator(TEnumerator source, Func<TSource, int, bool> predicate);
```

#### Parameters

`source` _TEnumerator_<br>
The source enumerator.

`predicate` _Func&lt;TSource, Int32, Boolean&gt;_<br>
A function to test each element for a condition; the second parameter of the function represents the zero-based index of the source element.

## Methods

| Name | Description |
| --- | --- |
| [TryGetNext(out TSource)](#trygetnextout-tsource) | Advances the enumerator to the next element that satisfies the index-aware predicate. |

## TryGetNext(out TSource)

Advances the enumerator to the next element that satisfies the index-aware predicate.

```csharp
public bool TryGetNext(out TSource current);
```

#### Parameters

`current` _TSource_<br>
When this method returns `true`, contains the next element satisfying the predicate; otherwise, the default value of `TSource`.

#### Returns

_Boolean_<br>
`true` if a matching element is found; `false` if the end of the sequence was reached.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU) / Device (GPU)
using System;
using Kassyi.ZeroAlloc.Linq;

ReadOnlySpan<int> numbers = [10, 20, 30, 40];
StackEnumerable<WhereAtEnumerator<SpanEnumerator<int>, int>, int> evenIndexed =
    numbers.AsStackEnumerable().Where((x, index) => index % 2 == 0);

Console.WriteLine($"Even-indexed sum: {evenIndexed.Sum()}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [Where Method](Where.md)
- [WhereEnumerator&lt;TEnumerator, TSource&gt; Struct](WhereEnumerator.md)
