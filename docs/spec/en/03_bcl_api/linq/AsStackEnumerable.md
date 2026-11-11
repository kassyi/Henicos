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
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Wraps an existing contiguous memory block into a zero-allocation stack enumerator.

## Overloads

| Name | Description |
| --- | --- |
| [AsStackEnumerable\<T\>(T[])](#asstackenumerable-overload-1) | Wraps a managed array into a zero-allocation `StackEnumerable<TEnumerator, T>`. |
| [AsStackEnumerable\<T\>(List\<T\>)](#asstackenumerable-overload-2) | Wraps a `Collections.Generic.List<T>` into a zero-allocation `StackEnumerable<TEnumerator, T>`. Host-only. |
| [AsStackEnumerable\<T\>(Span\<T\>)](#asstackenumerable-overload-3) | Wraps a `Span<T>` into a zero-allocation `StackEnumerable<TEnumerator, T>`. |
| [AsStackEnumerable\<T\>(ReadOnlySpan\<T\>)](#asstackenumerable-overload-4) | Wraps a `ReadOnlySpan<T>` into a zero-allocation `StackEnumerable<TEnumerator, T>`. |

<a id="asstackenumerable-overload-1"></a>

## AsStackEnumerable\<T\>(T[])

Wraps a managed array into a zero-allocation `StackEnumerable<TEnumerator, T>`.

```csharp
public static StackEnumerable<ArrayEnumerator<T>, T> AsStackEnumerable<T>(this T[] source);
```

#### Parameters

`source`<br>
The source array to wrap.

#### Returns

A stack-allocated enumerable wrapping an `ArrayEnumerator<T>`.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `source` is `null`.

**Performance:** Operates in $O(1)$ time with zero heap allocations.

<a id="asstackenumerable-overload-2"></a>

## AsStackEnumerable\<T\>(List\<T\>)

Wraps a `Collections.Generic.List<T>` into a zero-allocation `StackEnumerable<TEnumerator, T>`.

```csharp
public static StackEnumerable<SpanEnumerator<T>, T> AsStackEnumerable<T>(this List<T> source);
```

#### Parameters

`source`<br>
The source list to wrap.

#### Returns

A stack-allocated enumerable wrapping a `SpanEnumerator<T>`.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `source` is `null`.

**Performance:** Operates in $O(1)$ time with zero heap allocations by accessing the underlying span.

**Execution Target:** Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.

<a id="asstackenumerable-overload-3"></a>

## AsStackEnumerable\<T\>(Span\<T\>)

Wraps a `Span<T>` into a zero-allocation `StackEnumerable<TEnumerator, T>`.

```csharp
public static StackEnumerable<SpanEnumerator<T>, T> AsStackEnumerable<T>(this Span<T> source);
```

#### Parameters

`source`<br>
The source span to wrap.

#### Returns

A stack-allocated enumerable wrapping a `SpanEnumerator<T>`.

**Performance:** Operates in $O(1)$ time with zero heap allocations.

<a id="asstackenumerable-overload-4"></a>

## AsStackEnumerable\<T\>(ReadOnlySpan\<T\>)

Wraps a `ReadOnlySpan<T>` into a zero-allocation `StackEnumerable<TEnumerator, T>`.

```csharp
public static StackEnumerable<SpanEnumerator<T>, T> AsStackEnumerable<T>(this ReadOnlySpan<T> source);
```

#### Parameters

`source`<br>
The source read-only span to wrap.

#### Returns

A stack-allocated enumerable wrapping a `SpanEnumerator<T>`.

**Performance:** Operates in $O(1)$ time with zero heap allocations.

## Examples

```csharp
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

// CPU
ReadOnlySpan<int> span = stackalloc int[] { 10, 20, 30 };
var enumSpan = span.AsStackEnumerable();

// GPU Kernel
public static partial class StackLinqKernels
{
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
}
```

## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
