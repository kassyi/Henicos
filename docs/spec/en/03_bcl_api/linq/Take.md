---
title: "StackLinqExtensions.Take Method (G#)"
site: "G# Documentation"
published: "2026-10-09T00:00:00.000Z"
language: "en"
description: "Returns a specified number of contiguous elements from the start of a sequence."
---

## StackLinqExtensions.Take Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Returns a specified number of contiguous elements from the start of a sequence.

## Overloads

| Name | Description |
| --- | --- |
| [Take\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, int)](#take-overload-1) | Returns a specified number of contiguous elements from the start of a stack-allocated sequence. |

<a id="take-overload-1"></a>

## Take\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, int)

Returns a specified number of contiguous elements from the start of a stack-allocated sequence.

```csharp
public static StackEnumerable<TakeEnumerator<TEnumerator, TSource>, TSource> Take<TEnumerator, TSource>(
    this StackEnumerable<TEnumerator, TSource> source, int count)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The sequence to return elements from.

`count`<br>
The number of elements to return.

#### Returns

A `StackEnumerable<TEnumerator, TSource>` that contains the specified number of elements from the start of the input sequence.

## Examples

```csharp
using Kassyi.ZeroAlloc.Linq;

int[] numbers = [1, 2, 3, 4, 5];
var taken = numbers
    .AsStackEnumerable()
    .Take(3);
```

## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
