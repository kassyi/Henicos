---
title: "SkipWhileAtEnumerator<TEnumerator, TSource> Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "An enumerator that bypasses elements in a sequence as long as a specified condition involving element index is true and then returns the remaining elements with zero heap allocations."
---

## SkipWhileAtEnumerator&lt;TEnumerator, TSource&gt; Struct

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

An enumerator that bypasses elements in a sequence as long as a specified condition involving element index is true and then returns the remaining elements with zero heap allocations.

```csharp
public ref struct SkipWhileAtEnumerator<TEnumerator, TSource> : IStackEnumerator<TSource>
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
| [SkipWhileAtEnumerator(TEnumerator, Func&lt;TSource, Int32, Boolean&gt;)](#skipwhileatenumeratortenumerator-functsource-int32-boolean) | Initializes a new instance of the `SkipWhileAtEnumerator<TEnumerator, TSource>` struct. |

## SkipWhileAtEnumerator(TEnumerator, Func&lt;TSource, Int32, Boolean&gt;)

Initializes a new instance of the `SkipWhileAtEnumerator<TEnumerator, TSource>` struct.

```csharp
public SkipWhileAtEnumerator(TEnumerator source, Func<TSource, int, bool> predicate);
```

#### Parameters

`source` _TEnumerator_<br>
The underlying enumerator providing source elements.

`predicate` _Func&lt;TSource, Int32, Boolean&gt;_<br>
A function to test each element and its zero-based index for a condition.

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

ReadOnlySpan<int> numbers = [10, 20, 30, 40];
StackEnumerable<SkipWhileAtEnumerator<SpanEnumerator<int>, int>, int> remaining =
    numbers.AsStackEnumerable().SkipWhile((x, index) => index < 2);

Console.WriteLine($"Remaining sum: {remaining.Sum()}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [SkipWhile Method](SkipWhile.md)
- [SkipWhileEnumerator&lt;TEnumerator, TSource&gt; Struct](SkipWhileEnumerator.md)
