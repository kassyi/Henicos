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
**Assembly:** `Kassyi.GSharp.Core.dll`

Returns a specified number of contiguous elements from the start of a sequence.

## Overloads

| Name | Description |
| --- | --- |
| [Take<TEnumerator, TSource>(TEnumerator, int)](#taketenumeratortsourcetenumeratorint) | Returns elements from the start. |


## Take<TEnumerator, TSource>(TEnumerator, int)

Returns elements from the start.

```csharp
public static TakeEnumerator<TEnumerator, TSource> Take<TEnumerator, TSource>(this TEnumerator source, int count) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>TakeEnumerator<TEnumerator, TSource></i>



## Examples

```csharp
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
