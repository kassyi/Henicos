---
title: "StackLinqExtensions.Any Method (G#)"
site: "G# Documentation"
published: "2026-10-09T00:00:00.000Z"
language: "en"
description: "Determines whether any element of a sequence exists or satisfies a condition."
---

## StackLinqExtensions.Any Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Determines whether any element of a sequence exists or satisfies a condition.

## Overloads

| Name | Description |
| --- | --- |
| [Any<TEnumerator, TSource>(TEnumerator)](#anytenumeratortsourcetenumerator) | Determines whether a sequence contains any elements. |
| [Any<TEnumerator, TSource>(TEnumerator, Func<TSource, bool>)](#anytenumeratortsourcetenumeratorfunctsourcebool) | Determines whether any element satisfies a condition. |


## Any<TEnumerator, TSource>(TEnumerator)

Determines whether a sequence contains any elements.

```csharp
public static bool Any<TEnumerator, TSource>(this TEnumerator source) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>bool</i>

## Any<TEnumerator, TSource>(TEnumerator, Func<TSource, bool>)

Determines whether any element satisfies a condition.

```csharp
public static bool Any<TEnumerator, TSource>(this TEnumerator source, Func<TSource, bool> predicate) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>bool</i>



## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
