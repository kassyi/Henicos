---
title: "SpanEnumerator<T> Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Provides a zero-allocation, forward-only stack enumerator over a ReadOnlySpan<T>."
---

## SpanEnumerator&lt;T&gt; Struct

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Provides a zero-allocation, forward-only stack enumerator over a `ReadOnlySpan<T>`.

```csharp
public ref struct SpanEnumerator<T> : IStackEnumerator<T>
```

#### Type Parameters

`T`<br>
The type of elements in the span.

## Constructors

| Name | Description |
| --- | --- |
| [SpanEnumerator(ReadOnlySpan&lt;T&gt;)](#spanenumeratorreadonlyspant) | Initializes a new instance of the `SpanEnumerator<T>` struct wrapping the specified read-only span. |

## SpanEnumerator(ReadOnlySpan&lt;T&gt;)

Initializes a new instance of the `SpanEnumerator<T>` struct wrapping the specified read-only span.

```csharp
public SpanEnumerator(ReadOnlySpan<T> source);
```

#### Parameters

`source` _ReadOnlySpan&lt;T&gt;_<br>
The read-only span to enumerate.

## Methods

| Name | Description |
| --- | --- |
| [TryGetNext(out T)](#trygetnextout-t) | Attempts to advance the enumerator to the next element of the span. |

## TryGetNext(out T)

Attempts to advance the enumerator to the next element of the span.

```csharp
public bool TryGetNext(out T current);
```

#### Parameters

`current` _T_<br>
When this method returns `true`, contains the element at the current index; otherwise, the default value of `T`.

#### Returns

_Boolean_<br>
`true` if the enumerator successfully advanced to the next element; `false` if the end of the span was reached.

## Remarks

`SpanEnumerator<T>` operates directly over stack or contiguous memory (`Span<T>`, `ReadOnlySpan<T>`, or `List<T>` backing spans) with zero heap allocations.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU) / Device (GPU)
using System;
using Kassyi.ZeroAlloc.Linq;

ReadOnlySpan<int> span = [100, 200, 300];
var enumerator = new SpanEnumerator<int>(span);

int total = 0;
while (enumerator.TryGetNext(out int item))
{
    total += item;
}

Console.WriteLine($"Total: {total}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [ArrayEnumerator&lt;T&gt; Struct](ArrayEnumerator.md)
- [AsStackEnumerable Method](AsStackEnumerable.md)
