---
title: "StackLinqExtensions.SkipLast Method (G#)"
site: "G# Documentation"
published: "2026-10-09T00:00:00.000Z"
language: "en"
description: "Bypasses a specified number of contiguous elements from the end of a sequence."
---

## StackLinqExtensions.SkipLast Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Bypasses a specified number of contiguous elements from the end of a sequence.

## Overloads

| Name | Description |
| --- | --- |
| [SkipLast<TEnumerator, TSource>(TEnumerator, int)](#skiplasttenumeratortsourcetenumeratorint) | Bypasses elements from the end of a sequence. |


## SkipLast<TEnumerator, TSource>(TEnumerator, int)

Bypasses elements from the end of a sequence.

```csharp
public static SkipLastEnumerator<TEnumerator, TSource> SkipLast<TEnumerator, TSource>(this TEnumerator source, int count) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>SkipLastEnumerator<TEnumerator, TSource></i>



## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
