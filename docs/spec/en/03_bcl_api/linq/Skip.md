---
title: "StackLinqExtensions.Skip Method (G#)"
site: "G# Documentation"
published: "2026-10-09T00:00:00.000Z"
language: "en"
description: "Bypasses a specified number of elements in a sequence and then returns the remaining elements."
---

## StackLinqExtensions.Skip Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Bypasses a specified number of elements in a sequence and then returns the remaining elements.

## Overloads

| Name | Description |
| --- | --- |
| [Skip<TEnumerator, TSource>(TEnumerator, int)](#skiptenumeratortsourcetenumeratorint) | Bypasses a specified number of elements. |


## Skip<TEnumerator, TSource>(TEnumerator, int)

Bypasses a specified number of elements.

```csharp
public static SkipEnumerator<TEnumerator, TSource> Skip<TEnumerator, TSource>(this TEnumerator source, int count) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>SkipEnumerator<TEnumerator, TSource></i>



## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
