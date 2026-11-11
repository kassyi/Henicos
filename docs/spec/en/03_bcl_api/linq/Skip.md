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
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Bypasses a specified number of elements in a sequence and then returns the remaining elements.

## Overloads

| Name | Description |
| --- | --- |
| [Skip\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, int)](#skip-overload-1) | Bypasses a specified number of elements in a stack-allocated sequence and then returns the remaining elements. |

<a id="skip-overload-1"></a>

## Skip\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, int)

Bypasses a specified number of elements in a stack-allocated sequence and then returns the remaining elements.

```csharp
public static StackEnumerable<SkipEnumerator<TEnumerator, TSource>, TSource> Skip<TEnumerator, TSource>(this StackEnumerable<TEnumerator, TSource> source, int count) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The sequence to return elements from.

`count`<br>
The number of elements to skip before returning the remaining elements.

#### Returns

A `StackEnumerable<TEnumerator, TSource>` that contains elements that occur after the specified index in the input sequence.

## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
