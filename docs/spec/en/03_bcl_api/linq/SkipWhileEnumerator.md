---
title: "SkipWhileEnumerator<TEnumerator, TSource> Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "An enumerator that bypasses elements in a sequence as long as a specified condition is true and then returns the remaining elements with zero heap allocations."
---

## SkipWhileEnumerator&lt;TEnumerator, TSource&gt; Struct

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

An enumerator that bypasses elements in a sequence as long as a specified condition is true and then returns the remaining elements with zero heap allocations.

```csharp
public ref struct SkipWhileEnumerator<TEnumerator, TSource> : IStackEnumerator<TSource>
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
| [SkipWhileEnumerator(TEnumerator, Func&lt;TSource, Boolean&gt;)](#skipwhileenumeratortenumerator-functsource-boolean) | Initializes a new instance of the `SkipWhileEnumerator<TEnumerator, TSource>` struct. |

## SkipWhileEnumerator(TEnumerator, Func&lt;TSource, Boolean&gt;)

Initializes a new instance of the `SkipWhileEnumerator<TEnumerator, TSource>` struct.

```csharp
public SkipWhileEnumerator(TEnumerator source, Func<TSource, bool> predicate);
```

#### Parameters

`source` _TEnumerator_<br>
The underlying enumerator providing source elements.

`predicate` _Func&lt;TSource, Boolean&gt;_<br>
A function to test each element for a condition.

## Methods

| Name | Description |
| --- | --- |
| [TryGetNext(out TSource)](#trygetnextout-tsource) | Advances the enumerator to the next element of the sequence after elements matching the predicate have been skipped. |

## TryGetNext(out TSource)

Advances the enumerator to the next element of the sequence after elements matching the predicate have been skipped.

```csharp
public bool TryGetNext(out TSource current);
```

#### Parameters

`current` _TSource_<br>
When this method returns `true`, contains the next element in the sequence; otherwise, the default value of `TSource`.

#### Returns

_Boolean_<br>
`true` if the enumerator was successfully advanced to the next element; `false` if the end of the sequence was reached.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU) / Device (GPU)
using System;
using Kassyi.ZeroAlloc.Linq;

ReadOnlySpan<int> numbers = [5, 10, 25, 30];
StackEnumerable<SkipWhileEnumerator<SpanEnumerator<int>, int>, int> remaining =
    numbers.AsStackEnumerable().SkipWhile(x => x < 20);

Console.WriteLine($"Remaining sum: {remaining.Sum()}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [SkipWhile Method](SkipWhile.md)
- [SkipWhileAtEnumerator&lt;TEnumerator, TSource&gt; Struct](SkipWhileAtEnumerator.md)
