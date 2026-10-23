---
title: "StackLinqExtensions.IndexOf Method (G#)"
site: "G# Documentation"
published: "2026-10-23T00:00:00.000Z"
language: "en"
description: "Returns the zero-based position of the first element that satisfies a condition."
---

## StackLinqExtensions.IndexOf Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Returns the zero-based position of the first element that satisfies a condition.

## Overloads

| Name | Description |
| --- | --- |
| [IndexOf&lt;TEnumerator, TSource&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, Func&lt;TSource, Boolean&gt;)](#indexoftenumerator-tsourcestackenumerabletenumerator-tsource-functsource-boolean) | Returns the position of the first element that satisfies a condition. |
| [IndexOf&lt;TEnumerator, TSource, TState&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, TState, Func&lt;TSource, TState, Boolean&gt;)](#indexoftenumerator-tsource-tstatestackenumerabletenumerator-tsource-tstate-functsource-tstate-boolean) | Returns the position of the first element that satisfies a condition that receives a state value. |

## IndexOf&lt;TEnumerator, TSource&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, Func&lt;TSource, Boolean&gt;)

Returns the zero-based position of the first element that satisfies a condition.

```csharp
public static int IndexOf<TEnumerator, TSource>(this StackEnumerable<TEnumerator, TSource> source, Func<TSource, bool> predicate)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source` _StackEnumerable&lt;TEnumerator, TSource&gt;_<br>
The sequence to search.

`predicate` _Func&lt;TSource, Boolean&gt;_<br>
A function to test each element for a condition.

#### Returns

_Int32_<br>
The position of the first matching element, or `-1` when there is none.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `predicate` is `null`.

## IndexOf&lt;TEnumerator, TSource, TState&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, TState, Func&lt;TSource, TState, Boolean&gt;)

Returns the zero-based position of the first element that satisfies a condition that receives a caller-supplied state. Pass the outer value as `state` so that the predicate can be a `static` lambda and no closure is allocated.

```csharp
public static int IndexOf<TEnumerator, TSource, TState>(this StackEnumerable<TEnumerator, TSource> source, TState state, Func<TSource, TState, bool> predicate)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source` _StackEnumerable&lt;TEnumerator, TSource&gt;_<br>
The sequence to search.

`state` _TState_<br>
The value passed to every call of `predicate`.

`predicate` _Func&lt;TSource, TState, Boolean&gt;_<br>
A function to test each element for a condition; the second parameter is `state`.

#### Returns

_Int32_<br>
The position of the first matching element, or `-1` when there is none.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `predicate` is `null`.

## Remarks

The position counts the elements of the sequence `IndexOf` is called on, after any `Where`, `Skip` or `Take` before it. To search part of a sequence, chain `Take(n)` first: `-1` then means no match among the first `n` elements.

**Execution Target:** Host-only (CPU). A G# kernel reports `GSH0003` for both overloads.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU)
using System;
using Kassyi.ZeroAlloc.Linq;

string[] keywords = ["int", "float", "double", "float"];

int first = keywords
    .AsStackEnumerable()
    .IndexOf("float", static (k, name) => k == name);
int missing = keywords
    .AsStackEnumerable()
    .Take(2)
    .IndexOf(static k => k == "double");

Console.WriteLine($"first={first}, missing={missing}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [StackLinqExtensions Class](StackLinqExtensions.md)
- [TryFirst Method](TryFirst.md)
- [Take Method](Take.md)
