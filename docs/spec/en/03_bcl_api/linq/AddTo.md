---
title: "StackLinqExtensions.AddTo Method (G#)"
site: "G# Documentation"
published: "2026-10-23T00:00:00.000Z"
language: "en"
description: "Adds every element of a stack-allocated sequence to a collection the caller owns."
---

## StackLinqExtensions.AddTo Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Adds every element of a stack-allocated sequence to a collection the caller owns.

```csharp
public static int AddTo<TEnumerator, TSource>(this StackEnumerable<TEnumerator, TSource> source, ICollection<TSource> destination)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Type Parameters

`TEnumerator`<br>
The type of the source enumerator.

`TSource`<br>
The type of the elements in the sequence.

#### Parameters

`source` _StackEnumerable&lt;TEnumerator, TSource&gt;_<br>
The sequence whose elements are added.

`destination` _ICollection&lt;TSource&gt;_<br>
The collection that receives the elements.

#### Returns

_Int32_<br>
The number of elements added.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `destination` is `null`.

## Remarks

`AddTo` replaces a `foreach` loop that adds to a list. Unlike `ToList`, which StackLinq does not implement, it allocates nothing itself: the caller creates the collection, and growing it is the caller's concern. To add only some elements, use [AddIf](AddIf.md).

**Execution Target:** Host-only (CPU). A G# kernel reports `GSH0003` for it.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU)
using System;
using System.Collections.Generic;
using Kassyi.ZeroAlloc.Linq;

int[] numbers = [1, 2, 3, 4, 5];
var squares = new List<int>();

int added = numbers
    .AsStackEnumerable()
    .Select(static x => x * x)
    .AddTo(squares);

Console.WriteLine($"Added {added}: {string.Join(", ", squares)}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [StackLinqExtensions Class](StackLinqExtensions.md)
- [AddIf Method](AddIf.md)
- [Do Method](Do.md)
