---
title: "StackLinqExtensions.TryFirst Method (G#)"
site: "G# Documentation"
published: "2026-10-23T00:00:00.000Z"
language: "en"
description: "Finds the first element that satisfies a condition and reports whether one was found."
---

## StackLinqExtensions.TryFirst Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Finds the first element that satisfies a condition and reports whether one was found.

## Overloads

| Name | Description |
| --- | --- |
| [TryFirst&lt;TEnumerator, TSource&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, Func&lt;TSource, Boolean&gt;, out TSource)](#tryfirsttenumerator-tsourcestackenumerabletenumerator-tsource-functsource-boolean-out-tsource) | Finds the first element that satisfies a condition. |
| [TryFirst&lt;TEnumerator, TSource, TState&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, TState, Func&lt;TSource, TState, Boolean&gt;, out TSource)](#tryfirsttenumerator-tsource-tstatestackenumerabletenumerator-tsource-tstate-functsource-tstate-boolean-out-tsource) | Finds the first element that satisfies a condition that receives a state value. |

## TryFirst&lt;TEnumerator, TSource&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, Func&lt;TSource, Boolean&gt;, out TSource)

Finds the first element that satisfies a condition and reports whether one was found.

```csharp
public static bool TryFirst<TEnumerator, TSource>(this StackEnumerable<TEnumerator, TSource> source, Func<TSource, bool> predicate, out TSource value)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source` _StackEnumerable&lt;TEnumerator, TSource&gt;_<br>
The sequence to search.

`predicate` _Func&lt;TSource, Boolean&gt;_<br>
A function to test each element for a condition.

`value` _TSource_<br>
The first matching element, or the default value of `TSource` when there is none.

#### Returns

_Boolean_<br>
`true` if an element satisfies the condition; otherwise, `false`.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `predicate` is `null`.

## TryFirst&lt;TEnumerator, TSource, TState&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, TState, Func&lt;TSource, TState, Boolean&gt;, out TSource)

Finds the first element that satisfies a condition that receives a caller-supplied state. Pass the outer value as `state` so that the predicate can be a `static` lambda and no closure is allocated.

```csharp
public static bool TryFirst<TEnumerator, TSource, TState>(this StackEnumerable<TEnumerator, TSource> source, TState state, Func<TSource, TState, bool> predicate, out TSource value)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source` _StackEnumerable&lt;TEnumerator, TSource&gt;_<br>
The sequence to search.

`state` _TState_<br>
The value passed to every call of `predicate`.

`predicate` _Func&lt;TSource, TState, Boolean&gt;_<br>
A function to test each element for a condition; the second parameter is `state`.

`value` _TSource_<br>
The first matching element, or the default value of `TSource` when there is none.

#### Returns

_Boolean_<br>
`true` if an element satisfies the condition; otherwise, `false`.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `predicate` is `null`.

## Remarks

`TryFirst` follows the `TryGetValue` pattern. Unlike [FirstOrDefault](First.md), it tells a match that equals the default value (a `0`, an empty `KeyValuePair<TKey, TValue>`) from no match at all, which replaces a loop that sets a `found` flag and breaks.

**Execution Target:** Host-only (CPU). A G# kernel reports `GSH0003` for both overloads.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU)
using System;
using Kassyi.ZeroAlloc.Linq;

int[] offsets = [5, 0, 7];

// FirstOrDefault returns 0 both for "found 0" and for "nothing found"
bool foundSmall = offsets
    .AsStackEnumerable()
    .TryFirst(static x => x < 3, out int small);
bool foundLarge = offsets
    .AsStackEnumerable()
    .TryFirst(10, static (x, limit) => x > limit, out int large);

// foundSmall=True (0), foundLarge=False (0)
Console.WriteLine($"foundSmall={foundSmall} ({small}), foundLarge={foundLarge} ({large})");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [StackLinqExtensions Class](StackLinqExtensions.md)
- [First Method](First.md)
- [IndexOf Method](IndexOf.md)
