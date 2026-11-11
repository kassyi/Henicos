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
