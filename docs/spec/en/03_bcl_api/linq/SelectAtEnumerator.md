---
title: "SelectAtEnumerator<TEnumerator, TSource, TResult> Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Projects each element of a sequence into a new form by incorporating the element's zero-based index."
---

## SelectAtEnumerator&lt;TEnumerator, TSource, TResult&gt; Struct

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Projects each element of a sequence into a new form by incorporating the element's zero-based index.

```csharp
public ref struct SelectAtEnumerator<TEnumerator, TSource, TResult> : IStackEnumerator<TResult>
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
```

#### Type Parameters

`TEnumerator`<br>
The type of the underlying enumerator.

`TSource`<br>
The type of the elements to enumerate.

`TResult`<br>
The type of the projected elements.

## Constructors

| Name | Description |
| --- | --- |
| [SelectAtEnumerator(TEnumerator, Func&lt;TSource, Int32, TResult&gt;)](#selectatenumeratortenumerator-functsource-int32-tresult) | Initializes a new instance of the `SelectAtEnumerator<TEnumerator, TSource, TResult>` struct. |

## SelectAtEnumerator(TEnumerator, Func&lt;TSource, Int32, TResult&gt;)

Initializes a new instance of the `SelectAtEnumerator<TEnumerator, TSource, TResult>` struct.

```csharp
public SelectAtEnumerator(TEnumerator source, Func<TSource, int, TResult> selector);
```

#### Parameters

`source` _TEnumerator_<br>
The source enumerator.

`selector` _Func&lt;TSource, Int32, TResult&gt;_<br>
A transform function to apply to each source element; the second parameter of the function represents the zero-based index of the source element.

## Methods

| Name | Description |
| --- | --- |
| [TryGetNext(out TResult)](#trygetnextout-tresult) | Advances the enumerator to the next element and projects it along with its zero-based index. |

## TryGetNext(out TResult)

Advances the enumerator to the next element and projects it along with its zero-based index.

```csharp
public bool TryGetNext(out TResult current);
```

#### Parameters

`current` _TResult_<br>
When this method returns `true`, contains the projected element; otherwise, the default value of `TResult`.

#### Returns

_Boolean_<br>
`true` if the enumerator successfully advanced to the next element; `false` if the end of the sequence was reached.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU) / Device (GPU)
using System;
using Kassyi.ZeroAlloc.Linq;

ReadOnlySpan<int> numbers = [10, 20, 30];
StackEnumerable<SelectAtEnumerator<SpanEnumerator<int>, int, int>, int> projected =
    numbers
        .AsStackEnumerable()
        .Select((x, index) => x + index);

Console.WriteLine($"Projected sum: {projected.Sum()}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [Select Method](Select.md)
- [SelectEnumerator&lt;TEnumerator, TSource, TResult&gt; Struct](SelectEnumerator.md)
