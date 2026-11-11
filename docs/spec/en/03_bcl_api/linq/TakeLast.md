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
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Returns a specified number of contiguous elements from the end of a sequence.

## Overloads

| Name | Description |
| --- | --- |
| [TakeLast\<TSource\>(StackEnumerable\<SpanEnumerator\<TSource\>, TSource\>, int)](#takelast-overload-1) | Returns a new stack-allocated enumerable that contains the last `count` elements from `source`. |
| [TakeLast\<TSource\>(StackEnumerable\<ArrayEnumerator\<TSource\>, TSource\>, int)](#takelast-overload-2) | Returns a new stack-allocated enumerable that contains the last `count` elements from `source`. |

<a id="takelast-overload-1"></a>

## TakeLast\<TSource\>(StackEnumerable\<SpanEnumerator\<TSource\>, TSource\>, int)

Returns a new stack-allocated enumerable that contains the last `count` elements from `source`.

```csharp
public static StackEnumerable<SpanEnumerator<TSource>, TSource> TakeLast<TSource>(this StackEnumerable<SpanEnumerator<TSource>, TSource> source, int count);
```

#### Parameters

`source`<br>
The source span sequence to return elements from.

`count`<br>
The number of elements to take from the end of the sequence.

#### Returns

A stack-allocated enumerable containing the last `count` elements.

<a id="takelast-overload-2"></a>

## TakeLast\<TSource\>(StackEnumerable\<ArrayEnumerator\<TSource\>, TSource\>, int)

Returns a new stack-allocated enumerable that contains the last `count` elements from `source`.

```csharp
public static StackEnumerable<SpanEnumerator<TSource>, TSource> TakeLast<TSource>(this StackEnumerable<ArrayEnumerator<TSource>, TSource> source, int count);
```

#### Parameters

`source`<br>
The source array sequence to return elements from.

`count`<br>
The number of elements to take from the end of the sequence.

#### Returns

A stack-allocated enumerable containing the last `count` elements.

## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
