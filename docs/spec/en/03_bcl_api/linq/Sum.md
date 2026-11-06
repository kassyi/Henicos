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
using Kassyi.ZeroAlloc.Linq;

int[] numbers = [1, 2, 3];
int total = numbers
    .AsStackEnumerable()
    .Sum();
```

## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator that is fully inlineable by the G# Transpiler.

A `float` or `double` sum gives the result of `System.Linq`: the elements are added in sequence order, and a `float`
sum adds them in a `double` accumulator that is rounded to `float` once at the end. This holds on the host, in kernels
on CUDA and on both CPU paths; the sum is never reordered for SIMD. `SumSequential` gives the same result.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
