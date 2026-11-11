---
title: "StackLinqExtensions.Single Method (G#)"
site: "G# Documentation"
published: "2026-10-09T00:00:00.000Z"
language: "en"
description: "Returns the only element of a sequence, and throws an exception if there is not exactly one element."
---

## StackLinqExtensions.Single Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Returns the only element of a sequence, and throws an exception if there is not exactly one element.

## Overloads

| Name | Description |
| --- | --- |
| [Single\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>)](#single-overload-1) | Returns the only element of a stack-allocated sequence, and throws an exception if there is not exactly one element in the sequence. |
| [Single\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, bool\>)](#single-overload-2) | Returns the only element of a stack-allocated sequence that satisfies a specified condition, and throws an exception if more than one such element exists. |
| [SingleOrDefault\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>)](#single-overload-3) | Returns the only element of a stack-allocated sequence, or a default value if the sequence is empty; this method throws an exception if there is more than one element in the sequence. |
| [SingleOrDefault\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, bool\>)](#single-overload-4) | Returns the only element of a stack-allocated sequence that satisfies a specified condition, or a default value if no such element exists; this method throws an exception if more than one element satisfies the condition. |

<a id="single-overload-1"></a>

## Single\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>)

Returns the only element of a stack-allocated sequence, and throws an exception if there is not exactly one element in the sequence.

```csharp
public static TSource Single<TEnumerator, TSource>(
    this StackEnumerable<TEnumerator, TSource> source)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The source sequence to return the single element of.

#### Returns

The single element of the input sequence.

#### Exceptions

`InvalidOperationException`<br>
The input sequence contains no elements or more than one element.

<a id="single-overload-2"></a>

## Single\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, bool\>)

Returns the only element of a stack-allocated sequence that satisfies a specified condition, and throws an exception if more than one such element exists.

```csharp
public static TSource Single<TEnumerator, TSource>(
    this StackEnumerable<TEnumerator, TSource> source,
    Func<TSource, bool> predicate)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The source sequence to return a single element from.

`predicate`<br>
A function to test an element for a condition.

#### Returns

The single element of the input sequence that satisfies the condition.

#### Exceptions

`ArgumentNullException`<br>
`predicate` is `null`.

`InvalidOperationException`<br>
No element satisfies the condition in `predicate`, or more than one element satisfies the condition.

<a id="single-overload-3"></a>

## SingleOrDefault\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>)

Returns the only element of a stack-allocated sequence, or a default value if the sequence is empty; this method throws an exception if there is more than one element in the sequence.

```csharp
public static TSource? SingleOrDefault<TEnumerator, TSource>(
    this StackEnumerable<TEnumerator, TSource> source)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The source sequence to return the single element of.

#### Returns

The single element of the input sequence, or default value if the sequence contains no elements.

#### Exceptions

`InvalidOperationException`<br>
The input sequence contains more than one element.

<a id="single-overload-4"></a>

## SingleOrDefault\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, bool\>)

Returns the only element of a stack-allocated sequence that satisfies a specified condition, or a default value if no such element exists; this method throws an exception if more than one element satisfies the condition.

```csharp
public static TSource? SingleOrDefault<TEnumerator, TSource>(
    this StackEnumerable<TEnumerator, TSource> source,
    Func<TSource, bool> predicate)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The source sequence to return a single element from.

`predicate`<br>
A function to test an element for a condition.

#### Returns

The single element of the input sequence that satisfies the condition, or default value if no such element is found.

#### Exceptions

`ArgumentNullException`<br>
`predicate` is `null`.

`InvalidOperationException`<br>
More than one element satisfies the condition in `predicate`.

## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
