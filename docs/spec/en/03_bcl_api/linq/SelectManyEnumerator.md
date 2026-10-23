---
title: "SelectManyEnumerator<TEnumerator, TSource, TInnerEnumerator, TResult> Struct (G#)"
site: "G# Documentation"
published: "2026-10-23T00:00:00.000Z"
language: "en"
description: "Flattens the inner stack sequences of each source element into one sequence with zero heap allocations."
---

## SelectManyEnumerator&lt;TEnumerator, TSource, TInnerEnumerator, TResult&gt; Struct

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Projects each element of a sequence to an inner stack sequence and flattens the inner sequences into one, with zero heap allocations.

```csharp
public ref struct SelectManyEnumerator<TEnumerator, TSource, TInnerEnumerator, TResult> : IStackEnumerator<TResult>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    where TInnerEnumerator : struct, IStackEnumerator<TResult>, allows ref struct
```

#### Type Parameters

`TEnumerator`<br>
The type of the underlying source enumerator.

`TSource`<br>
The type of the elements in the source sequence.

`TInnerEnumerator`<br>
The type of the enumerator of each inner sequence.

`TResult`<br>
The type of the elements in the inner sequences.

## Constructors

| Name | Description |
| --- | --- |
| [SelectManyEnumerator(TEnumerator, Func&lt;TSource, StackEnumerable&lt;TInnerEnumerator, TResult&gt;&gt;)](#selectmanyenumeratortenumerator-functsource-stackenumerabletinnerenumerator-tresult) | Initializes a new instance of the `SelectManyEnumerator<TEnumerator, TSource, TInnerEnumerator, TResult>` struct. |

## SelectManyEnumerator(TEnumerator, Func&lt;TSource, StackEnumerable&lt;TInnerEnumerator, TResult&gt;&gt;)

Initializes a new instance of the `SelectManyEnumerator<TEnumerator, TSource, TInnerEnumerator, TResult>` struct.

```csharp
public SelectManyEnumerator(TEnumerator source, Func<TSource, StackEnumerable<TInnerEnumerator, TResult>> selector);
```

#### Parameters

`source` _TEnumerator_<br>
The underlying enumerator providing source elements.

`selector` _Func&lt;TSource, StackEnumerable&lt;TInnerEnumerator, TResult&gt;&gt;_<br>
A function that returns the inner sequence of a source element.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `selector` is `null`.

## Methods

| Name | Description |
| --- | --- |
| [TryGetNext(out TResult)](#trygetnextout-tresult) | Attempts to advance to the next element of the current inner sequence, moving to the next source element when the inner sequence is exhausted. |

## TryGetNext(out TResult)

Attempts to advance to the next element of the current inner sequence, moving to the next source element when the inner sequence is exhausted.

```csharp
public bool TryGetNext(out TResult current);
```

#### Parameters

`current` _TResult_<br>
When this method returns `true`, contains the next inner element; otherwise, the default value of `TResult`.

#### Returns

_Boolean_<br>
`true` if an element was produced; `false` if the source and the last inner sequence are exhausted.

## Remarks

`SelectManyEnumerator` holds one inner enumerator at a time: $O(1)$ auxiliary space and zero heap allocations. Empty inner sequences are skipped.

**Execution Target:** Host-only (CPU). A G# kernel reports `GSH0003` for [SelectMany](SelectMany.md).

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU)
using System;
using Kassyi.ZeroAlloc.Linq;

int[][] rows = [[1, 2, 3], [4], [5, 6]];

int total = rows
    .AsStackEnumerable()
    .SelectMany(static r => r
        .AsStackEnumerable()
    ).Sum();

Console.WriteLine($"Total: {total}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [SelectMany Method](SelectMany.md)
- [SelectEnumerator&lt;TEnumerator, TSource, TResult&gt; Struct](SelectEnumerator.md)
