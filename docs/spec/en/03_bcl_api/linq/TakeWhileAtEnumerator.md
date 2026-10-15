---
title: "TakeWhileAtEnumerator<TEnumerator, TSource> Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "An enumerator that returns elements from a sequence as long as a specified condition involving element index is true with zero heap allocations."
---

## TakeWhileAtEnumerator&lt;TEnumerator, TSource&gt; Struct

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

An enumerator that returns elements from a sequence as long as a specified condition involving element index is true with zero heap allocations.

```csharp
public ref struct TakeWhileAtEnumerator<TEnumerator, TSource> : IStackEnumerator<TSource>
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
| [TakeWhileAtEnumerator(TEnumerator, Func&lt;TSource, Int32, Boolean&gt;)](#takewhileatenumeratortenumerator-functsource-int32-boolean) | Initializes a new instance of the `TakeWhileAtEnumerator<TEnumerator, TSource>` struct. |

## TakeWhileAtEnumerator(TEnumerator, Func&lt;TSource, Int32, Boolean&gt;)

Initializes a new instance of the `TakeWhileAtEnumerator<TEnumerator, TSource>` struct.

```csharp
public TakeWhileAtEnumerator(TEnumerator source, Func<TSource, int, bool> predicate);
```

#### Parameters

`source` _TEnumerator_<br>
The underlying enumerator providing source elements.

`predicate` _Func&lt;TSource, Int32, Boolean&gt;_<br>
A function to test each element and its zero-based index for a condition.

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

ReadOnlySpan<int> numbers = [10, 20, 30, 40];
StackEnumerable<TakeWhileAtEnumerator<SpanEnumerator<int>, int>, int> prefix =
    numbers.AsStackEnumerable().TakeWhile((x, index) => index < 2);

Console.WriteLine($"Prefix sum: {prefix.Sum()}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [TakeWhile Method](TakeWhile.md)
- [TakeWhileEnumerator&lt;TEnumerator, TSource&gt; Struct](TakeWhileEnumerator.md)
