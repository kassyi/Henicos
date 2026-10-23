---
title: "StackLinqExtensions.SelectMany Method (G#)"
site: "G# Documentation"
published: "2026-10-23T00:00:00.000Z"
language: "en"
description: "Projects each element to an inner stack-allocated sequence and flattens the inner sequences into one."
---

## StackLinqExtensions.SelectMany Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Projects each element to an inner stack-allocated sequence and flattens the inner sequences into one.

```csharp
public static StackEnumerable<SelectManyEnumerator<TEnumerator, TSource, TInnerEnumerator, TResult>, TResult> SelectMany<TEnumerator, TSource, TInnerEnumerator, TResult>(this StackEnumerable<TEnumerator, TSource> source, Func<TSource, StackEnumerable<TInnerEnumerator, TResult>> selector)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    where TInnerEnumerator : struct, IStackEnumerator<TResult>, allows ref struct;
```

#### Type Parameters

`TEnumerator`<br>
The type of the source enumerator.

`TSource`<br>
The type of the elements in the source sequence.

`TInnerEnumerator`<br>
The type of the enumerator of each inner sequence.

`TResult`<br>
The type of the elements in the inner sequences.

#### Parameters

`source` _StackEnumerable&lt;TEnumerator, TSource&gt;_<br>
The sequence whose elements are projected.

`selector` _Func&lt;TSource, StackEnumerable&lt;TInnerEnumerator, TResult&gt;&gt;_<br>
A function that returns the inner sequence of an element, such as `static x => x.Items.AsStackEnumerable()`.

#### Returns

_StackEnumerable&lt;SelectManyEnumerator&lt;TEnumerator, TSource, TInnerEnumerator, TResult&gt;, TResult&gt;_<br>
A sequence over the elements of every inner sequence, in order.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `selector` is `null`.

## Remarks

The result is evaluated lazily by a [SelectManyEnumerator](SelectManyEnumerator.md), which holds one inner enumerator at a time: $O(1)$ auxiliary space and zero heap allocations. An inner sequence that is empty contributes nothing. Unlike `System.Linq.Enumerable.SelectMany`, the selector returns a `StackEnumerable`, so it usually ends with `.AsStackEnumerable()`.

**Execution Target:** Host-only (CPU). A G# kernel reports `GSH0003` for it.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU)
using System;
using System.Collections.Generic;
using Kassyi.ZeroAlloc.Linq;

int[][] groups = [[1, 2], [], [3, 4, 5]];
var flat = new List<int>();

groups
    .AsStackEnumerable()
    .SelectMany(static g => g
        .AsStackEnumerable()
    ).AddTo(flat);

Console.WriteLine($"Flattened: {string.Join(", ", flat)}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [StackLinqExtensions Class](StackLinqExtensions.md)
- [SelectManyEnumerator&lt;TEnumerator, TSource, TInnerEnumerator, TResult&gt; Struct](SelectManyEnumerator.md)
- [Select Method](Select.md)
