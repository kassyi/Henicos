---
title: "OfTypeEnumerator<TEnumerator, TSource, TResult> Struct (G#)"
site: "G# Documentation"
published: "2026-10-23T00:00:00.000Z"
language: "en"
description: "Filters a sequence down to the elements of a given type with zero heap allocations."
---

## OfTypeEnumerator&lt;TEnumerator, TSource, TResult&gt; Struct

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Filters a sequence down to the elements of a given type, with zero heap allocations.

```csharp
public ref struct OfTypeEnumerator<TEnumerator, TSource, TResult> : IStackEnumerator<TResult>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
```

#### Type Parameters

`TEnumerator`<br>
The type of the underlying source enumerator.

`TSource`<br>
The type of the elements in the source sequence.

`TResult`<br>
The type to filter the elements on.

## Constructors

| Name | Description |
| --- | --- |
| [OfTypeEnumerator(TEnumerator)](#oftypeenumeratortenumerator) | Initializes a new instance of the `OfTypeEnumerator<TEnumerator, TSource, TResult>` struct. |

## OfTypeEnumerator(TEnumerator)

Initializes a new instance of the `OfTypeEnumerator<TEnumerator, TSource, TResult>` struct.

```csharp
public OfTypeEnumerator(TEnumerator source);
```

#### Parameters

`source` _TEnumerator_<br>
The underlying enumerator providing source elements.

## Methods

| Name | Description |
| --- | --- |
| [TryGetNext(out TResult)](#trygetnextout-tresult) | Attempts to advance the enumerator to the next element of type `TResult`. |

## TryGetNext(out TResult)

Attempts to advance the enumerator to the next element of type `TResult`.

```csharp
public bool TryGetNext(out TResult current);
```

#### Parameters

`current` _TResult_<br>
When this method returns `true`, contains the next element of type `TResult`; otherwise, the default value of `TResult`.

#### Returns

_Boolean_<br>
`true` if an element of type `TResult` is found; `false` if the end of the sequence was reached without finding another one.

## Remarks

`OfTypeEnumerator` evaluates elements lazily with $O(1)$ auxiliary space. A `null` element is never of type `TResult`, so it is skipped as well.

**Execution Target:** Host-only (CPU). A G# kernel reports `GSH0003` for [OfType](OfType.md).

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU)
using System;
using Kassyi.ZeroAlloc.Linq;

string[] tokens = ["int", "", "float", ""];

// A miss becomes null, and OfType<string> drops it
int named = tokens
    .AsStackEnumerable()
    .Select(static t => t.Length == 0 ? null : t)
    .OfType<string>()
    .Count();

Console.WriteLine($"Named tokens: {named}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [OfType Method](OfType.md)
- [WhereEnumerator&lt;TEnumerator, TSource&gt; Struct](WhereEnumerator.md)
