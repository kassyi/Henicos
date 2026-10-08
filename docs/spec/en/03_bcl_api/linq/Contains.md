---
title: "StackLinqExtensions.Contains Method (G#)"
site: "G# Documentation"
published: "2026-10-09T00:00:00.000Z"
language: "en"
description: "Determines whether a sequence contains a specified element."
---

## StackLinqExtensions.Contains Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Determines whether a sequence contains a specified element.

## Overloads

| Name | Description |
| --- | --- |
| [Contains<TEnumerator, TSource>(TEnumerator, TSource)](#containstenumeratortsourcetenumeratortsource) | Determines whether a sequence contains a specified element. |


## Contains<TEnumerator, TSource>(TEnumerator, TSource)

Determines whether a sequence contains a specified element.

```csharp
public static bool Contains<TEnumerator, TSource>(this TEnumerator source, TSource value) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>bool</i>



## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
