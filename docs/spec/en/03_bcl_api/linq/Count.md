---
title: "StackLinqExtensions.Count Method (G#)"
site: "G# Documentation"
published: "2026-10-09T00:00:00.000Z"
language: "en"
description: "Returns the number of elements in a sequence."
---

## StackLinqExtensions.Count Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Returns the number of elements in a sequence.

## Overloads

| Name | Description |
| --- | --- |
| [Count<TEnumerator, TSource>(TEnumerator)](#counttenumeratortsourcetenumerator) | Returns the number of elements. |
| [Count<TEnumerator, TSource>(TEnumerator, Func<TSource, bool>)](#counttenumeratortsourcetenumeratorfunctsourcebool) | Returns a number that represents how many elements satisfy a condition. |


## Count<TEnumerator, TSource>(TEnumerator)

Returns the number of elements.

```csharp
public static int Count<TEnumerator, TSource>(this TEnumerator source) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>int</i>

## Count<TEnumerator, TSource>(TEnumerator, Func<TSource, bool>)

Returns a number that represents how many elements satisfy a condition.

```csharp
public static int Count<TEnumerator, TSource>(this TEnumerator source, Func<TSource, bool> predicate) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>int</i>



## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
