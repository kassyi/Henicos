---
title: "StackLinqExtensions.Select Method (G#)"
site: "G# Documentation"
published: "2026-10-09T00:00:00.000Z"
language: "en"
description: "Projects each element of a sequence into a new form."
---

## StackLinqExtensions.Select Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Projects each element of a sequence into a new form.

## Overloads

| Name | Description |
| --- | --- |
| [Select<TEnumerator, TSource, TResult>(TEnumerator, Func<TSource, TResult>)](#selecttenumeratortsourcetresulttenumeratorfunctsourcetresult) | Projects elements into a new form. |
| [Select<TEnumerator, TSource, TResult>(TEnumerator, Func<TSource, int, TResult>)](#selecttenumeratortsourcetresulttenumeratorfunctsourceinttresult) | Projects elements into a new form incorporating index. |


## Select<TEnumerator, TSource, TResult>(TEnumerator, Func<TSource, TResult>)

Projects elements into a new form.

```csharp
public static SelectEnumerator<TEnumerator, TSource, TResult> Select<TEnumerator, TSource, TResult>(this TEnumerator source, Func<TSource, TResult> selector) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>SelectEnumerator<TEnumerator, TSource, TResult></i>

## Select<TEnumerator, TSource, TResult>(TEnumerator, Func<TSource, int, TResult>)

Projects elements into a new form incorporating index.

```csharp
public static SelectAtEnumerator<TEnumerator, TSource, TResult> Select<TEnumerator, TSource, TResult>(this TEnumerator source, Func<TSource, int, TResult> selector) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>SelectAtEnumerator<TEnumerator, TSource, TResult></i>



## Examples

```csharp
int[] numbers = [1, 2, 3];

// Multiply by 10
var tens = numbers.AsStackEnumerable().Select(x => x * 10);

// Offset by index
var indexed = numbers.AsStackEnumerable().Select((x, i) => x + i);
```

## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
