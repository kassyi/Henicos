---
title: "StackLinqExtensions.Aggregate Method (G#)"
site: "G# Documentation"
published: "2026-10-09T00:00:00.000Z"
language: "en"
description: "Applies an accumulator function over a sequence."
---

## StackLinqExtensions.Aggregate Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Applies an accumulator function over a sequence.

## Overloads

| Name | Description |
| --- | --- |
| [Aggregate<TEnumerator, TSource, TAccumulate>(TEnumerator, TAccumulate, Func<TAccumulate, TSource, TAccumulate>)](#aggregatetenumeratortsourcetaccumulatetenumeratortaccumulatefunctaccumulatetsourcetaccumulate) | Applies an accumulator function. |


## Aggregate<TEnumerator, TSource, TAccumulate>(TEnumerator, TAccumulate, Func<TAccumulate, TSource, TAccumulate>)

Applies an accumulator function.

```csharp
public static TAccumulate Aggregate<TEnumerator, TSource, TAccumulate>(this TEnumerator source, TAccumulate seed, Func<TAccumulate, TSource, TAccumulate> func) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>TAccumulate</i>



## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
