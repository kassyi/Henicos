---
title: "SelectWithStateEnumerator<TEnumerator, TSource, TState, TResult> Struct (G#)"
site: "G# Documentation"
published: "2026-10-23T00:00:00.000Z"
language: "en"
description: "Projects each element with a selector that receives a caller-supplied state, with zero heap allocations."
---

## SelectWithStateEnumerator&lt;TEnumerator, TSource, TState, TResult&gt; Struct

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Projects each element of a sequence into a new form with a selector that receives a caller-supplied state, with zero heap allocations.

```csharp
public ref struct SelectWithStateEnumerator<TEnumerator, TSource, TState, TResult> : IStackEnumerator<TResult>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
```

#### Type Parameters

`TEnumerator`<br>
The type of the underlying source enumerator.

`TSource`<br>
The type of the elements in the source sequence.

`TState`<br>
The type of the state passed to the selector.

`TResult`<br>
The type of the value returned by the selector function.

## Constructors

| Name | Description |
| --- | --- |
| [SelectWithStateEnumerator(TEnumerator, TState, Func&lt;TSource, TState, TResult&gt;)](#selectwithstateenumeratortenumerator-tstate-functsource-tstate-tresult) | Initializes a new instance of the `SelectWithStateEnumerator<TEnumerator, TSource, TState, TResult>` struct. |

## SelectWithStateEnumerator(TEnumerator, TState, Func&lt;TSource, TState, TResult&gt;)

Initializes a new instance of the `SelectWithStateEnumerator<TEnumerator, TSource, TState, TResult>` struct.

```csharp
public SelectWithStateEnumerator(TEnumerator source, TState state, Func<TSource, TState, TResult> selector);
```

#### Parameters

`source` _TEnumerator_<br>
The underlying enumerator providing source elements.

`state` _TState_<br>
The value passed to every call of `selector`.

`selector` _Func&lt;TSource, TState, TResult&gt;_<br>
A transform function to apply to each source element; the second parameter is `state`.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `selector` is `null`.

## Methods

| Name | Description |
| --- | --- |
| [TryGetNext(out TResult)](#trygetnextout-tresult) | Attempts to advance the enumerator to the next element and projects it using the selector. |

## TryGetNext(out TResult)

Attempts to advance the enumerator to the next element and projects it using the selector.

```csharp
public bool TryGetNext(out TResult current);
```

#### Parameters

`current` _TResult_<br>
When this method returns `true`, contains the transformed element; otherwise, the default value of `TResult`.

#### Returns

_Boolean_<br>
`true` if the enumerator successfully advanced to the next element; `false` if the end of the sequence was reached.

## Remarks

`SelectWithStateEnumerator` evaluates projections lazily with $O(1)$ auxiliary space. Passing the outer value as state lets the selector be a `static` lambda, so no closure is allocated.

**Execution Target:** Host-only (CPU). A G# kernel reports `GSH0003` for `Select(state, selector)`; a kernel lambda reads outer values directly.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU)
using System;
using Kassyi.ZeroAlloc.Linq;

int[] values = [1, 2, 3];
int scale = 10;

int total = values
    .AsStackEnumerable()
    .Select(scale, static (v, s) => v * s)
    .Sum();

Console.WriteLine($"Scaled total: {total}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [Select Method](Select.md)
- [SelectEnumerator&lt;TEnumerator, TSource, TResult&gt; Struct](SelectEnumerator.md)
- [WhereWithStateEnumerator&lt;TEnumerator, TSource, TState&gt; Struct](WhereWithStateEnumerator.md)
