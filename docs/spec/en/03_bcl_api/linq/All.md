---
title: "StackLinqExtensions.All Method (G#)"
site: "G# Documentation"
published: "2026-10-09T00:00:00.000Z"
language: "en"
description: "Determines whether all elements of a sequence satisfy a condition."
---

## StackLinqExtensions.All Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Determines whether all elements of a sequence satisfy a condition.

## Overloads

| Name | Description |
| --- | --- |
| [All<TEnumerator, TSource>(TEnumerator, Func<TSource, bool>)](#alltenumeratortsourcetenumeratorfunctsourcebool) | Determines whether all elements satisfy a condition. |


## All<TEnumerator, TSource>(TEnumerator, Func<TSource, bool>)

Determines whether all elements satisfy a condition.

```csharp
public static bool All<TEnumerator, TSource>(this TEnumerator source, Func<TSource, bool> predicate) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>bool</i>



## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
