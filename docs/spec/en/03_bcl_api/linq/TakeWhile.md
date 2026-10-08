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
**Assembly:** `Kassyi.GSharp.Core.dll`

Returns elements from a sequence as long as a specified condition is true.

## Overloads

| Name | Description |
| --- | --- |
| [TakeWhile<TEnumerator, TSource>(TEnumerator, Func<TSource, bool>)](#takewhiletenumeratortsourcetenumeratorfunctsourcebool) | Returns elements as long as a condition is true. |
| [TakeWhile<TEnumerator, TSource>(TEnumerator, Func<TSource, int, bool>)](#takewhiletenumeratortsourcetenumeratorfunctsourceintbool) | Returns elements as long as a condition incorporating index is true. |


## TakeWhile<TEnumerator, TSource>(TEnumerator, Func<TSource, bool>)

Returns elements as long as a condition is true.

```csharp
public static TakeWhileEnumerator<TEnumerator, TSource> TakeWhile<TEnumerator, TSource>(this TEnumerator source, Func<TSource, bool> predicate) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>TakeWhileEnumerator<TEnumerator, TSource></i>

## TakeWhile<TEnumerator, TSource>(TEnumerator, Func<TSource, int, bool>)

Returns elements as long as a condition incorporating index is true.

```csharp
public static TakeWhileAtEnumerator<TEnumerator, TSource> TakeWhile<TEnumerator, TSource>(this TEnumerator source, Func<TSource, int, bool> predicate) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>TakeWhileAtEnumerator<TEnumerator, TSource></i>



## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
