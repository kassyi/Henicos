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
**Assembly:** `Kassyi.GSharp.Core.dll`

Bypasses elements in a sequence as long as a specified condition is true and then returns the remaining elements.

## Overloads

| Name | Description |
| --- | --- |
| [SkipWhile<TEnumerator, TSource>(TEnumerator, Func<TSource, bool>)](#skipwhiletenumeratortsourcetenumeratorfunctsourcebool) | Bypasses elements as long as a condition is true. |
| [SkipWhile<TEnumerator, TSource>(TEnumerator, Func<TSource, int, bool>)](#skipwhiletenumeratortsourcetenumeratorfunctsourceintbool) | Bypasses elements as long as an indexed condition is true. |


## SkipWhile<TEnumerator, TSource>(TEnumerator, Func<TSource, bool>)

Bypasses elements as long as a condition is true.

```csharp
public static SkipWhileEnumerator<TEnumerator, TSource> SkipWhile<TEnumerator, TSource>(this TEnumerator source, Func<TSource, bool> predicate) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>SkipWhileEnumerator<TEnumerator, TSource></i>

## SkipWhile<TEnumerator, TSource>(TEnumerator, Func<TSource, int, bool>)

Bypasses elements as long as an indexed condition is true.

```csharp
public static SkipWhileAtEnumerator<TEnumerator, TSource> SkipWhile<TEnumerator, TSource>(this TEnumerator source, Func<TSource, int, bool> predicate) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>SkipWhileAtEnumerator<TEnumerator, TSource></i>



## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
