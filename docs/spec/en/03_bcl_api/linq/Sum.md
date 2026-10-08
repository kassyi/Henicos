---
title: "StackLinqExtensions.Sum Method (G#)"
site: "G# Documentation"
published: "2026-10-09T00:00:00.000Z"
language: "en"
description: "Computes the sum of a sequence of numeric values."
---

## StackLinqExtensions.Sum Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Computes the sum of a sequence of numeric values.

## Overloads

| Name | Description |
| --- | --- |
| [Sum<TEnumerator>(TEnumerator)](#sumtenumeratortenumerator) | Computes the sum of a sequence. |


## Sum<TEnumerator>(TEnumerator)

Computes the sum of a sequence.

```csharp
public static int Sum<TEnumerator>(this TEnumerator source) where TEnumerator : struct, IStackEnumerator<int>, allows ref struct;
```

#### Returns

<i>int</i>



## Examples

```csharp
int[] numbers = [1, 2, 3];
int total = numbers.AsStackEnumerable().Sum();
```

## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
