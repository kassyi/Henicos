---
title: "TakeWhileEnumerator<TEnumerator, TSource> Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "An enumerator that returns elements from a sequence as long as a specified condition is true with zero heap allocations."
---

## TakeWhileEnumerator&lt;TEnumerator, TSource&gt; Struct

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

An enumerator that returns elements from a sequence as long as a specified condition is true with zero heap allocations.

```csharp
public ref struct TakeWhileEnumerator<TEnumerator, TSource> : IStackEnumerator<TSource>
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
| [TakeWhileEnumerator(TEnumerator, Func&lt;TSource, Boolean&gt;)](#takewhileenumeratortenumerator-functsource-boolean) | Initializes a new instance of the `TakeWhileEnumerator<TEnumerator, TSource>` struct. |

## TakeWhileEnumerator(TEnumerator, Func&lt;TSource, Boolean&gt;)

Initializes a new instance of the `TakeWhileEnumerator<TEnumerator, TSource>` struct.

```csharp
public TakeWhileEnumerator(TEnumerator source, Func<TSource, bool> predicate);
```

#### Parameters

`source` _TEnumerator_<br>
The underlying enumerator providing source elements.

`predicate` _Func&lt;TSource, Boolean&gt;_<br>
A function to test each element for a condition.

## Methods

| Name | Description |
| --- | --- |
| [TryGetNext(out TSource)](#trygetnextout-tsource) | Advances the enumerator to the next element of the sequence as long as the element satisfies the condition. |

## TryGetNext(out TSource)

Advances the enumerator to the next element of the sequence as long as the element satisfies the condition.

```csharp
public bool TryGetNext(out TSource current);
```

#### Parameters

`current` _TSource_<br>
When this method returns `true`, contains the next element in the sequence; otherwise, the default value of `TSource`.

#### Returns

_Boolean_<br>
`true` if the enumerator was successfully advanced to the next element satisfying the predicate; `false` if the end of the sequence was reached or the predicate returned `false`.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU) / Device (GPU)
using System;
using Kassyi.ZeroAlloc.Linq;

ReadOnlySpan<int> numbers = [5, 10, 25, 15];
StackEnumerable<TakeWhileEnumerator<SpanEnumerator<int>, int>, int> prefix =
    numbers.AsStackEnumerable().TakeWhile(x => x < 20);

Console.WriteLine($"Prefix sum: {prefix.Sum()}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [TakeWhile Method](TakeWhile.md)
- [TakeWhileAtEnumerator&lt;TEnumerator, TSource&gt; Struct](TakeWhileAtEnumerator.md)
