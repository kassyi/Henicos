---
title: "StackLinqExtensions.SkipWhile Method (G#)"
site: "G# Documentation"
published: "2026-10-09T00:00:00.000Z"
language: "en"
description: "Bypasses elements in a sequence as long as a specified condition is true and then returns the remaining elements."
---

## StackLinqExtensions.SkipWhile Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Bypasses elements in a sequence as long as a specified condition is true and then returns the remaining elements.

## Overloads

| Name | Description |
| --- | --- |
| [SkipWhile\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, bool\>)](#skipwhile-overload-1) | Bypasses elements in a stack-allocated sequence as long as a specified condition is true and then returns the remaining elements. |
| [SkipWhile\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, int, bool\>)](#skipwhile-overload-2) | Bypasses elements in a stack-allocated sequence as long as a specified condition is true and then returns the remaining elements. The element's index is used in the logic of the predicate function. |

<a id="skipwhile-overload-1"></a>

## SkipWhile\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, bool\>)

Bypasses elements in a stack-allocated sequence as long as a specified condition is true and then returns the remaining elements.

```csharp
public static StackEnumerable<SkipWhileEnumerator<TEnumerator, TSource>, TSource> SkipWhile<TEnumerator, TSource>(
    this StackEnumerable<TEnumerator, TSource> source, Func<TSource, bool> predicate)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The source sequence to return elements from.

`predicate`<br>
A function to test each element for a condition.

#### Returns

A `StackEnumerable<TEnumerator, TSource>` that contains elements from the input sequence starting at the first element that does not pass the test.

#### Exceptions

`ArgumentNullException`<br>
`predicate` is `null`.

<a id="skipwhile-overload-2"></a>

## SkipWhile\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, int, bool\>)

Bypasses elements in a stack-allocated sequence as long as a specified condition is true and then returns the remaining elements. The element's index is used in the logic of the predicate function.

```csharp
public static StackEnumerable<SkipWhileAtEnumerator<TEnumerator, TSource>, TSource> SkipWhile<TEnumerator, TSource>(
    this StackEnumerable<TEnumerator, TSource> source, Func<TSource, int, bool> predicate)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The source sequence to return elements from.

`predicate`<br>
A function to test each source element for a condition; the second parameter of the function represents the index of the source element.

#### Returns

A `StackEnumerable<TEnumerator, TSource>` that contains elements from the input sequence starting at the first element that does not pass the test.

#### Exceptions

`ArgumentNullException`<br>
`predicate` is `null`.

## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
