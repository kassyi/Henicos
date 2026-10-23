---
title: "WhereWithStateEnumerator<TEnumerator, TSource, TState> Struct (G#)"
site: "G# Documentation"
published: "2026-10-23T00:00:00.000Z"
language: "en"
description: "Filters a sequence with a predicate that receives a caller-supplied state, with zero heap allocations."
---

## WhereWithStateEnumerator&lt;TEnumerator, TSource, TState&gt; Struct

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Filters a sequence of elements based on a predicate that receives a caller-supplied state, with zero heap allocations.

```csharp
public ref struct WhereWithStateEnumerator<TEnumerator, TSource, TState> : IStackEnumerator<TSource>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
```

#### Type Parameters

`TEnumerator`<br>
The type of the underlying source enumerator.

`TSource`<br>
The type of the elements in the sequence.

`TState`<br>
The type of the state passed to the predicate.

## Constructors

| Name | Description |
| --- | --- |
| [WhereWithStateEnumerator(TEnumerator, TState, Func&lt;TSource, TState, Boolean&gt;)](#wherewithstateenumeratortenumerator-tstate-functsource-tstate-boolean) | Initializes a new instance of the `WhereWithStateEnumerator<TEnumerator, TSource, TState>` struct. |

## WhereWithStateEnumerator(TEnumerator, TState, Func&lt;TSource, TState, Boolean&gt;)

Initializes a new instance of the `WhereWithStateEnumerator<TEnumerator, TSource, TState>` struct.

```csharp
public WhereWithStateEnumerator(TEnumerator source, TState state, Func<TSource, TState, bool> predicate);
```

#### Parameters

`source` _TEnumerator_<br>
The underlying enumerator providing source elements.

`state` _TState_<br>
The value passed to every call of `predicate`.

`predicate` _Func&lt;TSource, TState, Boolean&gt;_<br>
A function to test each element for a condition; the second parameter is `state`.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `predicate` is `null`.

## Methods

| Name | Description |
| --- | --- |
| [TryGetNext(out TSource)](#trygetnextout-tsource) | Attempts to advance the enumerator to the next element that satisfies the predicate. |

## TryGetNext(out TSource)

Attempts to advance the enumerator to the next element that satisfies the predicate.

```csharp
public bool TryGetNext(out TSource current);
```

#### Parameters

`current` _TSource_<br>
When this method returns `true`, contains the next element satisfying the predicate; otherwise, the default value of `TSource`.

#### Returns

_Boolean_<br>
`true` if a matching element is found; `false` if the end of the sequence was reached without finding another match.

## Remarks

`WhereWithStateEnumerator` evaluates elements lazily with $O(1)$ auxiliary space. Passing the outer value as state lets the predicate be a `static` lambda, so no closure is allocated.

**Execution Target:** Host-only (CPU). A G# kernel reports `GSH0003` for `Where(state, predicate)`; a kernel lambda reads outer values directly.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU)
using System;
using Kassyi.ZeroAlloc.Linq;

int[] values = [4, 9, 2, 12, 7];
int threshold = 5;

int above = values
    .AsStackEnumerable()
    .Where(threshold, static (v, t) => v > t)
    .Count();

Console.WriteLine($"Above {threshold}: {above}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [Where Method](Where.md)
- [WhereEnumerator&lt;TEnumerator, TSource&gt; Struct](WhereEnumerator.md)
- [SelectWithStateEnumerator&lt;TEnumerator, TSource, TState, TResult&gt; Struct](SelectWithStateEnumerator.md)
