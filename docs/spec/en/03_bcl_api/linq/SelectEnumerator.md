---
title: "SelectEnumerator<TEnumerator, TSource, TResult> Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Projects each element of a sequence into a new form with zero heap allocations."
---

## SelectEnumerator&lt;TEnumerator, TSource, TResult&gt; Struct

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Projects each element of a sequence into a new form with zero heap allocations.

```csharp
public ref struct SelectEnumerator<TEnumerator, TSource, TResult> : IStackEnumerator<TResult>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
```

#### Type Parameters

`TEnumerator`<br>
The type of the underlying source enumerator.

`TSource`<br>
The type of the elements in the source sequence.

`TResult`<br>
The type of the value returned by the selector function.

## Constructors

| Name | Description |
| --- | --- |
| [SelectEnumerator(TEnumerator, Func&lt;TSource, TResult&gt;)](#selectenumeratortenumerator-functsource-tresult) | Initializes a new instance of the `SelectEnumerator<TEnumerator, TSource, TResult>` struct. |

## SelectEnumerator(TEnumerator, Func&lt;TSource, TResult&gt;)

Initializes a new instance of the `SelectEnumerator<TEnumerator, TSource, TResult>` struct.

```csharp
public SelectEnumerator(TEnumerator source, Func<TSource, TResult> selector);
```

#### Parameters

`source` _TEnumerator_<br>
The underlying enumerator providing source elements.

`selector` _Func&lt;TSource, TResult&gt;_<br>
A transform function to apply to each source element.

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

`SelectEnumerator<TEnumerator, TSource, TResult>` evaluates projections lazily with $O(1)$ auxiliary space and zero heap allocations.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU) / Device (GPU)
using System;
using Kassyi.ZeroAlloc.Linq;

ReadOnlySpan<int> numbers = [1, 2, 3, 4];
StackEnumerable<SelectEnumerator<SpanEnumerator<int>, int, int>, int> doubled =
    numbers
        .AsStackEnumerable()
        .Select(x => x * 2);

Console.WriteLine($"Doubled sum: {doubled.Sum()}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [Select Method](Select.md)
- [SelectAtEnumerator&lt;TEnumerator, TSource, TResult&gt; Struct](SelectAtEnumerator.md)
