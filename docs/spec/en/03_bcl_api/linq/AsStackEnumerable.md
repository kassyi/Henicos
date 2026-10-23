---
title: "StackLinqExtensions.AsStackEnumerable Method (G#)"
site: "G# Documentation"
published: "2026-10-09T00:00:00.000Z"
language: "en"
description: "Wraps an existing contiguous memory block into a zero-allocation stack enumerator."
---

## StackLinqExtensions.AsStackEnumerable Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Wraps an existing contiguous memory block into a zero-allocation stack enumerator.

## Overloads

| Name | Description |
| --- | --- |
| [AsStackEnumerable<T>(ReadOnlySpan<T>)](#asstackenumerabletreadonlyspant) | Wraps a read-only span. |
| [AsStackEnumerable<T>(Span<T>)](#asstackenumerabletspant) | Wraps a span. |
| [AsStackEnumerable<T>(T[])](#asstackenumerablett) | Wraps an array. |


## AsStackEnumerable<T>(ReadOnlySpan<T>)

Wraps a read-only span.

```csharp
public static SpanEnumerator<T> AsStackEnumerable<T>(this ReadOnlySpan<T> source);
```

#### Returns

<i>SpanEnumerator<T></i>

## AsStackEnumerable<T>(Span<T>)

Wraps a span.

```csharp
public static SpanEnumerator<T> AsStackEnumerable<T>(this Span<T> source);
```

#### Returns

<i>SpanEnumerator<T></i>

## AsStackEnumerable<T>(T[])

Wraps an array.

```csharp
public static ArrayEnumerator<T> AsStackEnumerable<T>(this T[] source);
```

#### Returns

<i>ArrayEnumerator<T></i>



## Examples

```csharp
// CPU
ReadOnlySpan<int> span = stackalloc int[] { 10, 20, 30 };
var enumSpan = span.AsStackEnumerable();

// GPU Kernel
[GpuKernel]
public static void Kernel(int[] data, int[] output)
{
    int tid = ThreadIndex.X;
    int sum = data
        .AsStackEnumerable()
        .Take(10)
        .Sum();
    output[tid] = sum;
}
```

## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
