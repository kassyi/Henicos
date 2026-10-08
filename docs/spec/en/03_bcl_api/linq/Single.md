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
**Assembly:** `Kassyi.GSharp.Core.dll`

Returns the only element of a sequence, and throws an exception if there is not exactly one element.

## Overloads

| Name | Description |
| --- | --- |
| [Single<TEnumerator, TSource>(TEnumerator)](#singletenumeratortsourcetenumerator) | Returns the only element. |
| [Single<TEnumerator, TSource>(TEnumerator, Func<TSource, bool>)](#singletenumeratortsourcetenumeratorfunctsourcebool) | Returns the only element that satisfies a specified condition. |


## Single<TEnumerator, TSource>(TEnumerator)

Returns the only element.

```csharp
public static TSource Single<TEnumerator, TSource>(this TEnumerator source) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>TSource</i>

## Single<TEnumerator, TSource>(TEnumerator, Func<TSource, bool>)

Returns the only element that satisfies a specified condition.

```csharp
public static TSource Single<TEnumerator, TSource>(this TEnumerator source, Func<TSource, bool> predicate) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>TSource</i>



## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
