---
title: "StackLinqExtensions.AddIf Method (G#)"
site: "G# Documentation"
published: "2026-10-23T00:00:00.000Z"
language: "en"
description: "Adds the elements that satisfy a condition to a collection the caller owns."
---

## StackLinqExtensions.AddIf Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Adds the elements that satisfy a condition to a collection the caller owns.

## Overloads

| Name | Description |
| --- | --- |
| [AddIf&lt;TEnumerator, TSource&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, ICollection&lt;TSource&gt;, Func&lt;TSource, Boolean&gt;)](#addiftenumerator-tsourcestackenumerabletenumerator-tsource-icollectiontsource-functsource-boolean) | Adds the elements that satisfy a condition. |
| [AddIf&lt;TEnumerator, TSource, TState&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, ICollection&lt;TSource&gt;, TState, Func&lt;TSource, TState, Boolean&gt;)](#addiftenumerator-tsource-tstatestackenumerabletenumerator-tsource-icollectiontsource-tstate-functsource-tstate-boolean) | Adds the elements that satisfy a condition that receives a state value. |

## AddIf&lt;TEnumerator, TSource&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, ICollection&lt;TSource&gt;, Func&lt;TSource, Boolean&gt;)

Adds the elements that satisfy a condition to a collection the caller owns.

```csharp
public static int AddIf<TEnumerator, TSource>(this StackEnumerable<TEnumerator, TSource> source, ICollection<TSource> destination, Func<TSource, bool> predicate)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source` _StackEnumerable&lt;TEnumerator, TSource&gt;_<br>
The sequence whose elements are tested.

`destination` _ICollection&lt;TSource&gt;_<br>
The collection that receives the matching elements.

`predicate` _Func&lt;TSource, Boolean&gt;_<br>
A function to test each element for a condition.

#### Returns

_Int32_<br>
The number of elements added.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `destination` or `predicate` is `null`.

## AddIf&lt;TEnumerator, TSource, TState&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, ICollection&lt;TSource&gt;, TState, Func&lt;TSource, TState, Boolean&gt;)

Adds the elements that satisfy a condition that receives a caller-supplied state. Pass the outer value as `state` so that the predicate can be a `static` lambda and no closure is allocated.

```csharp
public static int AddIf<TEnumerator, TSource, TState>(this StackEnumerable<TEnumerator, TSource> source, ICollection<TSource> destination, TState state, Func<TSource, TState, bool> predicate)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source` _StackEnumerable&lt;TEnumerator, TSource&gt;_<br>
The sequence whose elements are tested.

`destination` _ICollection&lt;TSource&gt;_<br>
The collection that receives the matching elements.

`state` _TState_<br>
The value passed to every call of `predicate`.

`predicate` _Func&lt;TSource, TState, Boolean&gt;_<br>
A function to test each element for a condition; the second parameter is `state`.

#### Returns

_Int32_<br>
The number of elements added.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `destination` or `predicate` is `null`.

## Remarks

`AddIf` replaces a `foreach` loop that tests each element and adds the matches to a list. With a `HashSet<T>` as the state and `seen.Add(x)` as the predicate, it keeps the first occurrence of each value in order, which removes duplicates without `Distinct` (not implemented by StackLinq).

**Execution Target:** Host-only (CPU). A G# kernel reports `GSH0003` for both overloads.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU)
using System;
using System.Collections.Generic;
using Kassyi.ZeroAlloc.Linq;

int[] numbers = [3, 1, 3, 2, 1, 4];

var evens = new List<int>();
numbers
    .AsStackEnumerable()
    .AddIf(evens, static x => x % 2 == 0);

// Keep the first of each value, in order
var unique = new List<int>();
numbers
    .AsStackEnumerable()
    .AddIf(unique, new HashSet<int>(), static (x, seen) => seen.Add(x));

Console.WriteLine($"Evens: {string.Join(", ", evens)}; unique: {string.Join(", ", unique)}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [StackLinqExtensions Class](StackLinqExtensions.md)
- [AddTo Method](AddTo.md)
- [DoIf Method](DoIf.md)
