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
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Computes the sum of a sequence of numeric values.

## Overloads

| Name | Description |
| --- | --- |
| [Sum\<TEnumerator\>(StackEnumerable\<TEnumerator, int\>)](#sum-overload-1) | Computes the sum of a sequence of `int` values. |
| [Sum\<TEnumerator\>(ParallelStackEnumerable\<TEnumerator, int\>)](#sum-overload-2) | Computes the sum of a parallel sequence of `int` values. CPU fallback implementation. |
| [Sum\<TEnumerator, TSource\>(ParallelStackEnumerable\<TEnumerator, TSource\>)](#sum-overload-3) | Computes the sum of a parallel sequence of custom types. CPU fallback implementation. |
| [Sum\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, int\>)](#sum-overload-4) | Computes the sum of the sequence of `int` values that are obtained by invoking a transform function on each element of the input sequence. |
| [Sum\<TEnumerator\>(StackEnumerable\<TEnumerator, float\>)](#sum-overload-5) | Computes the sum of a sequence of `float` values. |
| [Sum\<TEnumerator\>(ParallelStackEnumerable\<TEnumerator, float\>)](#sum-overload-6) | Computes the sum of a parallel sequence of `float` values. CPU fallback implementation. |
| [Sum\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, float\>)](#sum-overload-7) | Computes the sum of the sequence of `float` values that are obtained by invoking a transform function on each element of the input sequence. |
| [Sum\<TEnumerator\>(StackEnumerable\<TEnumerator, double\>)](#sum-overload-8) | Computes the sum of a sequence of `double` values. |
| [Sum\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, double\>)](#sum-overload-9) | Computes the sum of the sequence of `double` values that are obtained by invoking a transform function on each element of the input sequence. |
| `Sum` for `uint`, `long` and `ulong` | The same three forms as for `int` (a sequence, a parallel sequence and a selector); see [Integer sums](#integer-sums). |

## Integer sums

The `Sum` of `int`, `uint`, `long` and `ulong` is **exact**: the elements are added in a wider type (64 bits for `int` and `uint`, 128 bits for `long` and `ulong`) and the total is range-checked once at the end. When the exact sum does not fit the result type, `Sum` throws `OverflowException`, on the host, on the CPU paths and in a CUDA kernel alike.

The result does not depend on the order of the additions. This is why the block and grid reductions of `AsParallel().Sum()` (which add the elements in another order than a loop does) give the same value as the sequential sum.

### Difference from .NET

`System.Linq.Enumerable.Sum` checks every addition: it throws as soon as the running sum overflows, even when later elements would bring it back. StackLinq throws only when the mathematical sum of all the elements does not fit the result type. The two differ only on an input whose running sum overflows and whose exact sum fits: `[int.MaxValue, 1, -1]` sums to `int.MaxValue`, and `[int.MaxValue, 1, int.MinValue]` to `0`, where .NET throws. StackLinq gives the correct value there; it never returns a wrong one. An input whose exact sum does not fit throws in both.

### Why the accumulators have these widths

An accumulator must hold the exact sum of every sequence it can be given. A sequence has fewer than 2^31 elements on the host (an array index is an `int`) and on a device thread; a grid-wide `AsParallel()` sum can combine the partial sums of the threads of a whole grid, so its element count is bounded by 2^62 (2^31 blocks of up to 2^31 elements each, which no memory holds, but which the index arithmetic permits). The widths follow from that bound:

| Element type | Largest magnitude of one element | Accumulator | Largest exact sum it holds |
| :--- | :--- | :--- | :--- |
| `int`, `uint` | 2^32 | 64 bits inside a thread and a block (fewer than 2^31 elements: below 2^63) | 2^63 |
| `int`, `uint` | 2^32 | 128 bits for the total of a grid (up to 2^62 elements: 2^62 x 2^32 = 2^94 < 2^127) | 2^127 |
| `long`, `ulong` | 2^64 | 128 bits everywhere (2^62 x 2^64 = 2^126 < 2^127) | 2^127 |

On the host a 64-bit accumulator is enough for `int` and `uint` (fewer than 2^31 elements of at most 2^32 each), and 128 bits for `long` and `ulong`.

`Average` is not changed: it adds in a `double` accumulator on the device and in a `long` on the host, and never overflows.

<a id="sum-overload-1"></a>

## Sum\<TEnumerator\>(StackEnumerable\<TEnumerator, int\>)

Computes the sum of a sequence of `int` values.

```csharp
public static int Sum<TEnumerator>(
    this StackEnumerable<TEnumerator, int> source)
    where TEnumerator : struct, IStackEnumerator<int>, allows ref struct;
```

#### Parameters

`source`<br>
A sequence of `int` values to calculate the sum of.

#### Returns

The sum of the values in the sequence.

**Performance:** Consumes the enumerator in $O(N)$ time with zero heap allocations.

<a id="sum-overload-2"></a>

## Sum\<TEnumerator\>(ParallelStackEnumerable\<TEnumerator, int\>)

Computes the sum of a parallel sequence of `int` values. CPU fallback implementation.

```csharp
public static int Sum<TEnumerator>(
    this ParallelStackEnumerable<TEnumerator, int> source)
    where TEnumerator : struct, IStackEnumerator<int>, allows ref struct;
```

#### Parameters

`source`<br>
A parallel sequence of values that are used to calculate a sum.

#### Returns

The sum of the values in the sequence.

<a id="sum-overload-3"></a>

## Sum\<TEnumerator, TSource\>(ParallelStackEnumerable\<TEnumerator, TSource\>)

Computes the sum of a parallel sequence of custom types. CPU fallback implementation.

```csharp
public static TSource Sum<TEnumerator, TSource>(
    this ParallelStackEnumerable<TEnumerator, TSource> source)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    where TSource : IAdditionOperators<TSource, TSource, TSource>, IAdditiveIdentity<TSource, TSource>;
```

#### Parameters

`source`<br>
A parallel sequence of values that are used to calculate a sum.

#### Returns

The sum of the values in the sequence.

<a id="sum-overload-4"></a>

## Sum\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, int\>)

Computes the sum of the sequence of `int` values that are obtained by invoking a transform function on each element of the input sequence.

```csharp
public static int Sum<TEnumerator, TSource>(
    this StackEnumerable<TEnumerator, TSource> source,
    Func<TSource, int> selector)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
A sequence of values that are used to calculate a sum.

`selector`<br>
A transform function to apply to each element.

#### Returns

The sum of the projected values.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `selector` is `null`.

**Performance:** Short-circuits the creation of a dedicated enumerator by chaining `Select<TEnumerator, TSource, TResult>(StackEnumerable<TEnumerator, TSource>, Func<TSource, TResult>)` internally in $O(N)$ time with zero heap allocations.

<a id="sum-overload-5"></a>

## Sum\<TEnumerator\>(StackEnumerable\<TEnumerator, float\>)

Computes the sum of a sequence of `float` values.

```csharp
public static float Sum<TEnumerator>(
    this StackEnumerable<TEnumerator, float> source)
    where TEnumerator : struct, IStackEnumerator<float>, allows ref struct;
```

#### Parameters

`source`<br>
A sequence of `float` values to calculate the sum of.

#### Returns

The sum of the values in the sequence.

As `System.Linq`, the elements are added in sequence order in a `double` accumulator, which is rounded to `float` once at the end.

**Performance:** Consumes the enumerator in $O(N)$ time with zero heap allocations.

<a id="sum-overload-6"></a>

## Sum\<TEnumerator\>(ParallelStackEnumerable\<TEnumerator, float\>)

Computes the sum of a parallel sequence of `float` values. CPU fallback implementation.

```csharp
public static float Sum<TEnumerator>(
    this ParallelStackEnumerable<TEnumerator, float> source)
    where TEnumerator : struct, IStackEnumerator<float>, allows ref struct;
```

#### Parameters

`source`<br>
A parallel sequence of `float` values to calculate the sum of.

#### Returns

The sum of the values in the sequence.

As the sequential `Sum` and as PLINQ, the elements are added in a `double` accumulator, which is rounded to `float` once at the end; `AsParallel()` does not trade that accuracy for speed.

<a id="sum-overload-7"></a>

## Sum\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, float\>)

Computes the sum of the sequence of `float` values that are obtained by invoking a transform function on each element of the input sequence.

```csharp
public static float Sum<TEnumerator, TSource>(
    this StackEnumerable<TEnumerator, TSource> source,
    Func<TSource, float> selector)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
A sequence of values that are used to calculate a sum.

`selector`<br>
A transform function to apply to each element.

#### Returns

The sum of the projected values.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `selector` is `null`.

**Performance:** Short-circuits the creation of a dedicated enumerator by chaining `Select<TEnumerator, TSource, TResult>(StackEnumerable<TEnumerator, TSource>, Func<TSource, TResult>)` internally in $O(N)$ time with zero heap allocations.

<a id="sum-overload-8"></a>

## Sum\<TEnumerator\>(StackEnumerable\<TEnumerator, double\>)

Computes the sum of a sequence of `double` values.

```csharp
public static double Sum<TEnumerator>(
    this StackEnumerable<TEnumerator, double> source)
    where TEnumerator : struct, IStackEnumerator<double>, allows ref struct;
```

#### Parameters

`source`<br>
A sequence of `double` values to calculate the sum of.

#### Returns

The sum of the values in the sequence.

As `System.Linq`, the elements are added in sequence order.

**Performance:** Consumes the enumerator in $O(N)$ time with zero heap allocations.

<a id="sum-overload-9"></a>

## Sum\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, double\>)

Computes the sum of the sequence of `double` values that are obtained by invoking a transform function on each element of the input sequence.

```csharp
public static double Sum<TEnumerator, TSource>(
    this StackEnumerable<TEnumerator, TSource> source,
    Func<TSource, double> selector)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
A sequence of values that are used to calculate a sum.

`selector`<br>
A transform function to apply to each element.

#### Returns

The sum of the projected values.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `selector` is `null`.

**Performance:** Short-circuits the creation of a dedicated enumerator by chaining `Select<TEnumerator, TSource, TResult>(StackEnumerable<TEnumerator, TSource>, Func<TSource, TResult>)` internally in $O(N)$ time with zero heap allocations.

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
`AsParallel().Sum()` keeps the `double` accumulator for a `float` sum, as PLINQ does (see
[ParallelStackEnumerable](ParallelStackEnumerable.md)).

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
