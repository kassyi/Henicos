---
title: "StackLinqExtensions.TakeLast Method (G#)"
site: "G# Documentation"
published: "2026-10-09T00:00:00.000Z"
language: "en"
description: "Returns a specified number of contiguous elements from the end of a sequence."
---

## StackLinqExtensions.TakeLast Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Returns a specified number of contiguous elements from the end of a sequence.

## Overloads

| Name | Description |
| --- | --- |
| [TakeLast<TEnumerator, TSource>(TEnumerator, int)](#takelasttenumeratortsourcetenumeratorint) | Returns elements from the end of a sequence. |


## TakeLast<TEnumerator, TSource>(TEnumerator, int)

Returns elements from the end of a sequence.

```csharp
public static TakeLastEnumerator<TEnumerator, TSource> TakeLast<TEnumerator, TSource>(this TEnumerator source, int count) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>TakeLastEnumerator<TEnumerator, TSource></i>



## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
