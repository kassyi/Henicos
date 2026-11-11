---
title: "StackLinqExtensions.TakeWhile Method (G#)"
site: "G# Documentation"
published: "2026-10-09T00:00:00.000Z"
language: "en"
description: "Returns elements from a sequence as long as a specified condition is true."
---

## StackLinqExtensions.TakeWhile Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Returns elements from a sequence as long as a specified condition is true.

## Overloads

| Name | Description |
| --- | --- |
| [TakeWhile\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, bool\>)](#takewhile-overload-1) | Returns elements from a stack-allocated sequence as long as a specified condition is true. |
| [TakeWhile\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, int, bool\>)](#takewhile-overload-2) | Returns elements from a stack-allocated sequence as long as a specified condition is true. The element's index is used in the logic of the predicate function. |

<a id="takewhile-overload-1"></a>

## TakeWhile\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, bool\>)

Returns elements from a stack-allocated sequence as long as a specified condition is true.

```csharp
public static StackEnumerable<TakeWhileEnumerator<TEnumerator, TSource>, TSource> TakeWhile<TEnumerator, TSource>(this StackEnumerable<TEnumerator, TSource> source, Func<TSource, bool> predicate) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The source sequence to return elements from.

`predicate`<br>
A function to test each element for a condition.

#### Returns

A `StackEnumerable<TEnumerator, TSource>` that contains elements from the input sequence that occur before the element at which the test no longer passes.

#### Exceptions

`ArgumentNullException`<br>
`predicate` is `null`.

<a id="takewhile-overload-2"></a>

## TakeWhile\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, int, bool\>)

Returns elements from a stack-allocated sequence as long as a specified condition is true. The element's index is used in the logic of the predicate function.

```csharp
public static StackEnumerable<TakeWhileAtEnumerator<TEnumerator, TSource>, TSource> TakeWhile<TEnumerator, TSource>(this StackEnumerable<TEnumerator, TSource> source, Func<TSource, int, bool> predicate) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The source sequence to return elements from.

`predicate`<br>
A function to test each source element for a condition; the second parameter of the function represents the index of the source element.

#### Returns

A `StackEnumerable<TEnumerator, TSource>` that contains elements from the input sequence that occur before the element at which the test no longer passes.

#### Exceptions

`ArgumentNullException`<br>
`predicate` is `null`.

## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
