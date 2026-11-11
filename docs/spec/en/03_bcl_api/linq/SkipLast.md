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
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Bypasses a specified number of contiguous elements from the end of a sequence.

## Overloads

| Name | Description |
| --- | --- |
| [SkipLast\<TSource\>(StackEnumerable\<SpanEnumerator\<TSource\>, TSource\>, int)](#skiplast-overload-1) | Returns a new stack-allocated enumerable that contains the elements from `source` with the last `count` elements omitted. |
| [SkipLast\<TSource\>(StackEnumerable\<ArrayEnumerator\<TSource\>, TSource\>, int)](#skiplast-overload-2) | Returns a new stack-allocated enumerable that contains the elements from `source` with the last `count` elements omitted. |

<a id="skiplast-overload-1"></a>

## SkipLast\<TSource\>(StackEnumerable\<SpanEnumerator\<TSource\>, TSource\>, int)

Returns a new stack-allocated enumerable that contains the elements from `source` with the last `count` elements omitted.

```csharp
public static StackEnumerable<SpanEnumerator<TSource>, TSource> SkipLast<TSource>(
    this StackEnumerable<SpanEnumerator<TSource>, TSource> source, int count);
```

#### Parameters

`source`<br>
The source span sequence to omit elements from.

`count`<br>
The number of elements to omit from the end of the sequence.

#### Returns

A stack-allocated enumerable containing elements with the last `count` elements omitted.

<a id="skiplast-overload-2"></a>

## SkipLast\<TSource\>(StackEnumerable\<ArrayEnumerator\<TSource\>, TSource\>, int)

Returns a new stack-allocated enumerable that contains the elements from `source` with the last `count` elements omitted.

```csharp
public static StackEnumerable<SpanEnumerator<TSource>, TSource> SkipLast<TSource>(
    this StackEnumerable<ArrayEnumerator<TSource>, TSource> source, int count);
```

#### Parameters

`source`<br>
The source array sequence to omit elements from.

`count`<br>
The number of elements to omit from the end of the sequence.

#### Returns

A stack-allocated enumerable containing elements with the last `count` elements omitted.

## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
