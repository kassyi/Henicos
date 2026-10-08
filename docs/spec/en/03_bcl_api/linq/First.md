---
title: "StackLinqExtensions.First Method (G#)"
site: "G# Documentation"
published: "2026-10-09T00:00:00.000Z"
language: "en"
description: "Returns the first element of a sequence."
---

## StackLinqExtensions.First Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Returns the first element of a sequence.

## Overloads

| Name | Description |
| --- | --- |
| [First<TEnumerator, TSource>(TEnumerator)](#firsttenumeratortsourcetenumerator) | Returns the first element. |
| [First<TEnumerator, TSource>(TEnumerator, Func<TSource, bool>)](#firsttenumeratortsourcetenumeratorfunctsourcebool) | Returns the first element that satisfies a specified condition. |


## First<TEnumerator, TSource>(TEnumerator)

Returns the first element.

```csharp
public static TSource First<TEnumerator, TSource>(this TEnumerator source) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>TSource</i>

## First<TEnumerator, TSource>(TEnumerator, Func<TSource, bool>)

Returns the first element that satisfies a specified condition.

```csharp
public static TSource First<TEnumerator, TSource>(this TEnumerator source, Func<TSource, bool> predicate) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>TSource</i>



## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
