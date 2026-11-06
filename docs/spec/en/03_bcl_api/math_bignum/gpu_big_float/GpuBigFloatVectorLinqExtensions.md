---
title: "GpuBigFloatVectorLinqExtensions Class (G#)"
site: "G# Documentation"
published: "2026-11-02T00:00:00.000Z"
language: "en"
description: "Provides vectorized GPU LINQ extension methods, transcendental projections, stream compaction, and parallel reductions for GpuBigFloatArray."
---

## GpuBigFloatVectorLinqExtensions Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Provides extension methods for constructing and evaluating vectorized GPU LINQ expression trees over [`GpuBigFloatArray`](GpuBigFloatArray.md) and [`IGpuBigFloatVectorExpression`](IGpuBigFloatVectorExpression.md), including scalar and element-wise arithmetic (`Select*`, `Zip*`), unary and transcendental projections (`SelectSqrt`, `SelectRsqrt`, `SelectReciprocal`, `SelectExp`, `SelectLog`, `SelectPow`), stream compaction (`Where`), slicing (`Skip`, `Take`), relational comparisons (`Is*`), and parallel reductions (`Sum`, `Min`, `Max`, `Average`, `Count`).

```csharp
public static class GpuBigFloatVectorLinqExtensions
```

## Methods

### Conversion & Slicing

| Name | Description |
| --- | --- |
| [AsVectorizedEnumerable(GpuBigFloatArray)](#asvectorizedenumerablegpubigfloatarray) | Wraps a `GpuBigFloatArray` as the root `IGpuBigFloatVectorExpression` node. |
| [Skip(IGpuBigFloatVectorExpression, int)](#skipigpubigfloatvectorexpression-int) | Bypasses a specified number of elements and returns the remaining elements. |
| [Take(IGpuBigFloatVectorExpression, int)](#takeigpubigfloatvectorexpression-int) | Returns a specified number of contiguous elements from the start of the sequence. |
| [Where(IGpuBigFloatVectorExpression, IGpuBoolVectorExpression)](#whereigpubigfloatvectorexpression-igpuboolvectorexpression) | Filters elements on the GPU using stream compaction (`Blelloch` exclusive scan) based on a boolean predicate vector. |

### Scalar & Unary Projections (`Select*`)

| Name | Description |
| --- | --- |
| [SelectAdd\<TScalar\>(IGpuBigFloatVectorExpression, TScalar)](#selectaddtscalarigpubigfloatvectorexpression-tscalar) | Projects each element by adding a scalar `TScalar` (`GpuBigFloat` or `GpuBigFloat<TSize>`). |
| [SelectSubtract\<TScalar\>(IGpuBigFloatVectorExpression, TScalar)](#selectsubtracttscalarigpubigfloatvectorexpression-tscalar) | Projects each element by subtracting a scalar `TScalar`. |
| [SelectMultiply\<TScalar\>(IGpuBigFloatVectorExpression, TScalar)](#selectmultiplytscalarigpubigfloatvectorexpression-tscalar) | Projects each element by multiplying by a scalar `TScalar`. |
| [SelectDivide\<TScalar\>(IGpuBigFloatVectorExpression, TScalar)](#selectdividetscalarigpubigfloatvectorexpression-tscalar) | Projects each element by dividing by a scalar `TScalar`. |
| [SelectPow\<TScalar\>(IGpuBigFloatVectorExpression, TScalar)](#selectpowtscalarigpubigfloatvectorexpression-tscalar) | Projects each element by raising it to a scalar power `TScalar`. |
| [SelectDivideByUInt32(IGpuBigFloatVectorExpression, uint)](#selectdividebyuint32igpubigfloatvectorexpression-uint) | Projects each element by dividing its significand by a 32-bit unsigned integer `divisor`. |
| [SelectSqrt(IGpuBigFloatVectorExpression)](#selectsqrtigpubigfloatvectorexpression) | Projects each element to its square root ($\sqrt{x}$). |
| [SelectRsqrt(IGpuBigFloatVectorExpression)](#selectrsqrtigpubigfloatvectorexpression) | Projects each element to its reciprocal square root ($1 / \sqrt{x}$). |
| [SelectReciprocal(IGpuBigFloatVectorExpression)](#selectreciprocaligpubigfloatvectorexpression) | Projects each element to its reciprocal ($1 / x$). |
| [SelectExp(IGpuBigFloatVectorExpression)](#selectexpigpubigfloatvectorexpression) | Projects each element to its base-$e$ exponential ($e^x$). |
| [SelectLog(IGpuBigFloatVectorExpression)](#selectlogigpubigfloatvectorexpression) | Projects each element to its natural logarithm ($\ln x$). |

### Element-Wise Binary Operations (`Zip*`)

| Name | Description |
| --- | --- |
| [ZipAdd(IGpuBigFloatVectorExpression, IGpuBigFloatVectorExpression)](#zipaddigpubigfloatvectorexpression-igpubigfloatvectorexpression) | Performs element-wise addition of two float vector expressions. |
| [ZipSubtract(IGpuBigFloatVectorExpression, IGpuBigFloatVectorExpression)](#zipsubtractigpubigfloatvectorexpression-igpubigfloatvectorexpression) | Performs element-wise subtraction of two float vector expressions. |
| [ZipMultiply(IGpuBigFloatVectorExpression, IGpuBigFloatVectorExpression)](#zipmultiplyigpubigfloatvectorexpression-igpubigfloatvectorexpression) | Performs element-wise multiplication of two float vector expressions. |
| [ZipDivide(IGpuBigFloatVectorExpression, IGpuBigFloatVectorExpression)](#zipdivideigpubigfloatvectorexpression-igpubigfloatvectorexpression) | Performs element-wise division of two float vector expressions. |
| [ZipPow(IGpuBigFloatVectorExpression, IGpuBigFloatVectorExpression)](#zippowigpubigfloatvectorexpression-igpubigfloatvectorexpression) | Raises each element of the left vector expression to the corresponding element of the right vector expression. |

### Relational Comparisons (`Is*`)

| Name | Description |
| --- | --- |
| [IsEqualTo\<TScalar\>(IGpuBigFloatVectorExpression, TScalar)](#isequaltotscalarigpubigfloatvectorexpression-tscalar) | Compares each element for equality (`==`) with a scalar `TScalar`. |
| [IsEqualTo(IGpuBigFloatVectorExpression, IGpuBigFloatVectorExpression)](#isequaltoigpubigfloatvectorexpression-igpubigfloatvectorexpression) | Compares elements of two float vectors for equality (`==`). |
| [IsNotEqualTo\<TScalar\>(IGpuBigFloatVectorExpression, TScalar)](#isnotequaltotscalarigpubigfloatvectorexpression-tscalar) | Compares each element for inequality (`!=`) with a scalar `TScalar`. |
| [IsNotEqualTo(IGpuBigFloatVectorExpression, IGpuBigFloatVectorExpression)](#isnotequaltoigpubigfloatvectorexpression-igpubigfloatvectorexpression) | Compares elements of two float vectors for inequality (`!=`). |
| [IsGreaterThan\<TScalar\>(IGpuBigFloatVectorExpression, TScalar)](#isgreaterthantscalarigpubigfloatvectorexpression-tscalar) | Compares whether each element is strictly greater (`>`) than a scalar `TScalar`. |
| [IsGreaterThan(IGpuBigFloatVectorExpression, IGpuBigFloatVectorExpression)](#isgreaterthanigpubigfloatvectorexpression-igpubigfloatvectorexpression) | Compares whether each element in the left vector is strictly greater (`>`) than the corresponding element in the right vector. |
| [IsGreaterThanOrEqualTo\<TScalar\>(IGpuBigFloatVectorExpression, TScalar)](#isgreaterthanorequaltotscalarigpubigfloatvectorexpression-tscalar) | Compares whether each element is greater than or equal (`>=`) to a scalar `TScalar`. |
| [IsGreaterThanOrEqualTo(IGpuBigFloatVectorExpression, IGpuBigFloatVectorExpression)](#isgreaterthanorequaltoigpubigfloatvectorexpression-igpubigfloatvectorexpression) | Compares whether each element in the left vector is greater than or equal (`>=`) to the corresponding element in the right vector. |
| [IsLessThan\<TScalar\>(IGpuBigFloatVectorExpression, TScalar)](#islessthantscalarigpubigfloatvectorexpression-tscalar) | Compares whether each element is strictly less (`<`) than a scalar `TScalar`. |
| [IsLessThan(IGpuBigFloatVectorExpression, IGpuBigFloatVectorExpression)](#islessthanigpubigfloatvectorexpression-igpubigfloatvectorexpression) | Compares whether each element in the left vector is strictly less (`<`) than the corresponding element in the right vector. |
| [IsLessThanOrEqualTo\<TScalar\>(IGpuBigFloatVectorExpression, TScalar)](#islessthanorequaltotscalarigpubigfloatvectorexpression-tscalar) | Compares whether each element is less than or equal (`<=`) to a scalar `TScalar`. |
| [IsLessThanOrEqualTo(IGpuBigFloatVectorExpression, IGpuBigFloatVectorExpression)](#islessthanorequaltoigpubigfloatvectorexpression-igpubigfloatvectorexpression) | Compares whether each element in the left vector is less than or equal (`<=`) to the corresponding element in the right vector. |

### Terminal Reductions & Materialization

| Name | Description |
| --- | --- |
| [Count(IGpuBigFloatVectorExpression)](#countigpubigfloatvectorexpression) | Returns the element count when known on the host without device execution. |
| [Count(IGpuBigFloatVectorExpression, GpuWorkspace)](#countgpuworkspace) | Evaluates the expression if needed and returns the exact element count (synchronizing once when filtered by `Where`). |
| [Sum(IGpuBigFloatVectorExpression, GpuWorkspace)](#sumigpubigfloatvectorexpression-gpuworkspace) | Computes the sum of all elements in the vectorized expression using multi-pass GPU tree reduction. |
| [Max(IGpuBigFloatVectorExpression, GpuWorkspace)](#maxigpubigfloatvectorexpression-gpuworkspace) | Computes the maximum value in the vectorized expression using multi-pass GPU tree reduction. |
| [Min(IGpuBigFloatVectorExpression, GpuWorkspace)](#minigpubigfloatvectorexpression-gpuworkspace) | Computes the minimum value in the vectorized expression using multi-pass GPU tree reduction. |
| [Average(IGpuBigFloatVectorExpression, GpuWorkspace)](#averageigpubigfloatvectorexpression-gpuworkspace) | Computes the arithmetic mean ($\text{Sum} / \text{Count}$) of all elements in a single evaluation. |
| [EvaluateTo(IGpuBigFloatVectorExpression, GpuWorkspace, GpuBigFloatArray)](#evaluatetoigpubigfloatvectorexpression-gpuworkspace-gpubigfloatarray) | Evaluates an unfiltered expression directly into a pre-allocated `GpuBigFloatArray`. |

---

## AsVectorizedEnumerable(GpuBigFloatArray)

Wraps a `GpuBigFloatArray` as a root `IGpuBigFloatVectorExpression`. Disposing the returned expression does not dispose the underlying `source` array.

```csharp
public static IGpuBigFloatVectorExpression AsVectorizedEnumerable(this GpuBigFloatArray source);
```

## Where(IGpuBigFloatVectorExpression, IGpuBoolVectorExpression)

Filters elements of `source` on the GPU using Blelloch exclusive prefix scan and stream compaction (`GpuStreamCompactionKernels.CompactFloatVector`).

```csharp
public static IGpuBigFloatVectorExpression Where(
    this IGpuBigFloatVectorExpression source,
    IGpuBoolVectorExpression predicate);
```

#### Exceptions

`ArgumentNullException`<br>
`source` or `predicate` is `null`.

`ArgumentException`<br>
`source` and `predicate` have different static lengths, or different `Capacity` values when filtered.

`NotSupportedException`<br>
`source` and `predicate` depend on two separately constructed `Where` expressions whose compacted lengths are only known at evaluation time.

## Skip(IGpuBigFloatVectorExpression, int)

Bypasses `count` elements from the start of `source` (clamped to `[0, length]`, or shifted in `ElementCountContext.Skip` when `source` is filtered by `Where`).

```csharp
public static IGpuBigFloatVectorExpression Skip(this IGpuBigFloatVectorExpression source, int count);
```

#### Exceptions

`ArgumentOutOfRangeException`<br>
`count < 0`.

## Take(IGpuBigFloatVectorExpression, int)

Limits `source` to at most `count` elements from the start (clamped to `[0, length]`, or clamped via `ElementCountContext.Limit` when `source` is filtered by `Where`).

```csharp
public static IGpuBigFloatVectorExpression Take(this IGpuBigFloatVectorExpression source, int count);
```

#### Exceptions

`ArgumentOutOfRangeException`<br>
`count < 0`.

## SelectAdd\<TScalar\>(IGpuBigFloatVectorExpression, TScalar)

Adds `scalar` to every element of `source`. Output `WordsPerElement` is `Math.Max(source.WordsPerElement, scalar.Words) + 1`.

```csharp
public static IGpuBigFloatVectorExpression SelectAdd<TScalar>(
    this IGpuBigFloatVectorExpression source,
    TScalar scalar)
    where TScalar : struct, IGpuBigNumber<TScalar>;
```

## SelectSubtract\<TScalar\>(IGpuBigFloatVectorExpression, TScalar)

Subtracts `scalar` from every element of `source`. Output `WordsPerElement` is `Math.Max(source.WordsPerElement, scalar.Words) + 1`.

```csharp
public static IGpuBigFloatVectorExpression SelectSubtract<TScalar>(
    this IGpuBigFloatVectorExpression source,
    TScalar scalar)
    where TScalar : struct, IGpuBigNumber<TScalar>;
```

## SelectMultiply\<TScalar\>(IGpuBigFloatVectorExpression, TScalar)

Multiplies every element of `source` by `scalar`. Output `WordsPerElement` is `source.WordsPerElement + scalar.Words`.

```csharp
public static IGpuBigFloatVectorExpression SelectMultiply<TScalar>(
    this IGpuBigFloatVectorExpression source,
    TScalar scalar)
    where TScalar : struct, IGpuBigNumber<TScalar>;
```

## SelectDivide\<TScalar\>(IGpuBigFloatVectorExpression, TScalar)

Divides every element of `source` by `scalar`. Output `WordsPerElement` is `Math.Max(source.WordsPerElement, scalar.Words) + 1`. Throws `DivideByZeroException` during evaluation if `scalar` is zero.

```csharp
public static IGpuBigFloatVectorExpression SelectDivide<TScalar>(
    this IGpuBigFloatVectorExpression source,
    TScalar scalar)
    where TScalar : struct, IGpuBigNumber<TScalar>;
```

## SelectPow\<TScalar\>(IGpuBigFloatVectorExpression, TScalar)

Raises every element of `source` to `exponent` using [`IGpuBigFloatArithmetic.Pow`](IGpuBigFloatArithmetic.md). Output `WordsPerElement` is `Math.Max(source.WordsPerElement, exponent.Words)`.

```csharp
public static IGpuBigFloatVectorExpression SelectPow<TScalar>(
    this IGpuBigFloatVectorExpression source,
    TScalar exponent)
    where TScalar : struct, IGpuBigNumber<TScalar>;
```

## SelectDivideByUInt32(IGpuBigFloatVectorExpression, uint)

Divides the significand of every element in `source` by a 32-bit unsigned integer `divisor` using [`IGpuBigFloatArithmetic.DivideByUInt32`](IGpuBigFloatArithmetic.md). Output `WordsPerElement` equals `source.WordsPerElement`. Throws `DivideByZeroException` during evaluation if `divisor == 0`.

```csharp
public static IGpuBigFloatVectorExpression SelectDivideByUInt32(
    this IGpuBigFloatVectorExpression source,
    uint divisor);
```

## SelectSqrt(IGpuBigFloatVectorExpression)

Computes the square root ($\sqrt{x}$) of each element in `source` at `source.WordsPerElement` precision.

```csharp
public static IGpuBigFloatVectorExpression SelectSqrt(this IGpuBigFloatVectorExpression source);
```

## SelectRsqrt(IGpuBigFloatVectorExpression)

Computes the reciprocal square root ($1 / \sqrt{x}$) of each element in `source` at `source.WordsPerElement` precision.

```csharp
public static IGpuBigFloatVectorExpression SelectRsqrt(this IGpuBigFloatVectorExpression source);
```

## SelectReciprocal(IGpuBigFloatVectorExpression)

Computes the reciprocal ($1 / x$) of each element in `source` at `source.WordsPerElement` precision.

```csharp
public static IGpuBigFloatVectorExpression SelectReciprocal(this IGpuBigFloatVectorExpression source);
```

## SelectExp(IGpuBigFloatVectorExpression)

Computes the base-$e$ exponential ($e^x$) of each element in `source` at `source.WordsPerElement` precision.

```csharp
public static IGpuBigFloatVectorExpression SelectExp(this IGpuBigFloatVectorExpression source);
```

## SelectLog(IGpuBigFloatVectorExpression)

Computes the natural logarithm ($\ln x$) of each element in `source` at `source.WordsPerElement` precision.

```csharp
public static IGpuBigFloatVectorExpression SelectLog(this IGpuBigFloatVectorExpression source);
```

## ZipAdd(IGpuBigFloatVectorExpression, IGpuBigFloatVectorExpression)

Performs element-wise addition of `left` and `right`. Output `WordsPerElement` is `Math.Max(left.WordsPerElement, right.WordsPerElement) + 1`.

```csharp
public static IGpuBigFloatVectorExpression ZipAdd(
    this IGpuBigFloatVectorExpression left,
    IGpuBigFloatVectorExpression right);
```

## ZipSubtract(IGpuBigFloatVectorExpression, IGpuBigFloatVectorExpression)

Performs element-wise subtraction of `right` from `left`. Output `WordsPerElement` is `Math.Max(left.WordsPerElement, right.WordsPerElement) + 1`.

```csharp
public static IGpuBigFloatVectorExpression ZipSubtract(
    this IGpuBigFloatVectorExpression left,
    IGpuBigFloatVectorExpression right);
```

## ZipMultiply(IGpuBigFloatVectorExpression, IGpuBigFloatVectorExpression)

Performs element-wise multiplication of `left` and `right`. Output `WordsPerElement` is `left.WordsPerElement + right.WordsPerElement`.

```csharp
public static IGpuBigFloatVectorExpression ZipMultiply(
    this IGpuBigFloatVectorExpression left,
    IGpuBigFloatVectorExpression right);
```

## ZipDivide(IGpuBigFloatVectorExpression, IGpuBigFloatVectorExpression)

Performs element-wise division of `left` by `right`. Output `WordsPerElement` is `Math.Max(left.WordsPerElement, right.WordsPerElement) + 1`. Throws `DivideByZeroException` during evaluation if any active divisor element in `right` is zero. The check is read back from the device (one synchronization of the workspace stream), so evaluating it while the stream captures a CUDA graph throws `NotSupportedException`.

```csharp
public static IGpuBigFloatVectorExpression ZipDivide(
    this IGpuBigFloatVectorExpression left,
    IGpuBigFloatVectorExpression right);
```

## ZipPow(IGpuBigFloatVectorExpression, IGpuBigFloatVectorExpression)

Raises each element of `left` to the power of the corresponding element in `right`. Output `WordsPerElement` is `Math.Max(left.WordsPerElement, right.WordsPerElement)`.

```csharp
public static IGpuBigFloatVectorExpression ZipPow(
    this IGpuBigFloatVectorExpression left,
    IGpuBigFloatVectorExpression right);
```

## IsEqualTo / IsNotEqualTo / IsGreaterThan / IsGreaterThanOrEqualTo / IsLessThan / IsLessThanOrEqualTo

Constructs a deferred [`IGpuBoolVectorExpression`](../gpu_big_int_and_uint/expressions/IGpuBoolVectorExpression.md) comparing each element of `source` against a scalar `TScalar` or a second `IGpuBigFloatVectorExpression`:

```csharp
public static IGpuBoolVectorExpression IsEqualTo<TScalar>(this IGpuBigFloatVectorExpression source, TScalar scalar)
    where TScalar : struct, IGpuBigNumber<TScalar>;
public static IGpuBoolVectorExpression IsNotEqualTo<TScalar>(this IGpuBigFloatVectorExpression source, TScalar scalar)
    where TScalar : struct, IGpuBigNumber<TScalar>;
public static IGpuBoolVectorExpression IsGreaterThan<TScalar>(this IGpuBigFloatVectorExpression source, TScalar scalar)
    where TScalar : struct, IGpuBigNumber<TScalar>;
public static IGpuBoolVectorExpression IsGreaterThanOrEqualTo<TScalar>(this IGpuBigFloatVectorExpression source, TScalar scalar)
    where TScalar : struct, IGpuBigNumber<TScalar>;
public static IGpuBoolVectorExpression IsLessThan<TScalar>(this IGpuBigFloatVectorExpression source, TScalar scalar)
    where TScalar : struct, IGpuBigNumber<TScalar>;
public static IGpuBoolVectorExpression IsLessThanOrEqualTo<TScalar>(this IGpuBigFloatVectorExpression source, TScalar scalar)
    where TScalar : struct, IGpuBigNumber<TScalar>;

public static IGpuBoolVectorExpression IsEqualTo(this IGpuBigFloatVectorExpression left, IGpuBigFloatVectorExpression right);
public static IGpuBoolVectorExpression IsNotEqualTo(this IGpuBigFloatVectorExpression left, IGpuBigFloatVectorExpression right);
public static IGpuBoolVectorExpression IsGreaterThan(this IGpuBigFloatVectorExpression left, IGpuBigFloatVectorExpression right);
public static IGpuBoolVectorExpression IsGreaterThanOrEqualTo(this IGpuBigFloatVectorExpression left, IGpuBigFloatVectorExpression right);
public static IGpuBoolVectorExpression IsLessThan(this IGpuBigFloatVectorExpression left, IGpuBigFloatVectorExpression right);
public static IGpuBoolVectorExpression IsLessThanOrEqualTo(this IGpuBigFloatVectorExpression left, IGpuBigFloatVectorExpression right);
```

## Count(IGpuBigFloatVectorExpression)

Returns the number of elements in `source` when known statically on the host without running on the device.

```csharp
public static int Count(this IGpuBigFloatVectorExpression source);
```

#### Exceptions

`InvalidOperationException`<br>
Thrown when `source` contains a `Where` filter (`"The length of a filtered vector expression is only known on the device; call Count(workspace) to read it."`).

## Count(IGpuBigFloatVectorExpression, GpuWorkspace)

Returns the exact number of elements in `source`. If `source` contains a `Where` filter, evaluates the filter and reads the compacted element count from the device with one stream synchronization; otherwise returns the statically known length without device execution. Returns `0` for an empty sequence.

```csharp
public static int Count(this IGpuBigFloatVectorExpression source, GpuWorkspace workspace);
```

## Sum(IGpuBigFloatVectorExpression, GpuWorkspace)

Evaluates `source` and reduces all active elements by summation (`VectorFloatSumReductionChunkKernel`). Returns positive zero (`+0` with `source.WordsPerElement` words) when the sequence is empty.

```csharp
public static GpuBigFloat Sum(this IGpuBigFloatVectorExpression source, GpuWorkspace workspace);
```

## Max(IGpuBigFloatVectorExpression, GpuWorkspace)

Evaluates `source` and reduces all active elements to their maximum (`VectorFloatMaxReductionChunkKernel`).

```csharp
public static GpuBigFloat Max(this IGpuBigFloatVectorExpression source, GpuWorkspace workspace);
```

#### Exceptions

`InvalidOperationException`<br>
The sequence is empty (`"Sequence contains no elements."`), including when a `Where` filter selects `0` elements.

## Min(IGpuBigFloatVectorExpression, GpuWorkspace)

Evaluates `source` and reduces all active elements to their minimum (`VectorFloatMinReductionChunkKernel`).

```csharp
public static GpuBigFloat Min(this IGpuBigFloatVectorExpression source, GpuWorkspace workspace);
```

#### Exceptions

`InvalidOperationException`<br>
The sequence is empty (`"Sequence contains no elements."`), including when a `Where` filter selects `0` elements.

## Average(IGpuBigFloatVectorExpression, GpuWorkspace)

Evaluates `source` once, computes the sum and the active element count in the same evaluation, and divides the sum by the element count (`DivideByUInt32`).

```csharp
public static GpuBigFloat Average(this IGpuBigFloatVectorExpression source, GpuWorkspace workspace);
```

#### Exceptions

`InvalidOperationException`<br>
The sequence is empty (`"Sequence contains no elements."`), including when a `Where` filter selects `0` elements.

## EvaluateTo(IGpuBigFloatVectorExpression, GpuWorkspace, GpuBigFloatArray)

Evaluates an unfiltered expression directly into the pre-allocated `destination` array without allocating an intermediate result buffer at the root node.

```csharp
public static void EvaluateTo(
    this IGpuBigFloatVectorExpression source,
    GpuWorkspace workspace,
    GpuBigFloatArray destination);
```

#### Exceptions

`InvalidOperationException`<br>
`source` contains a `Where` filter (`!source.TryGetLength(out _)`). `EvaluateTo` refuses filtered vectors before writing to `destination` because `GpuBigFloatArray` has a fixed `Length` and cannot be resized to a runtime compacted count; use [`IGpuBigFloatVectorExpression.Evaluate`](IGpuBigFloatVectorExpression.md#evaluategpuworkspace-int-int-gpubigfloatarrayview) instead.

`ArgumentException`<br>
`destination` is uninitialized (`destination.Significands.Words == null`) or `destination.Length < source.Length`.

## Behavioral Rules & Constraints

1. **`Length`, `Capacity`, and `TryGetLength` on Filtered Expressions**:
   When an `IGpuBigFloatVectorExpression` contains a `Where` clause, the number of surviving elements is computed on the device without stalling the host thread. Accessing `Length` throws `InvalidOperationException`; use `Capacity` to query the upper bound without device synchronization, or `TryGetLength(out int length)` to check whether the length is known on the host (`false` when filtered).
2. **`Count()` vs. `Count(GpuWorkspace)`**:
   `Count()` is a host-only query that throws `InvalidOperationException` on a filtered expression. `Count(GpuWorkspace)` evaluates the pipeline if needed and reads the compacted device count with one synchronization.
3. **`EvaluateTo` Rejects Filtered Expressions**:
   `EvaluateTo(workspace, destination)` checks `TryGetLength` upfront and throws `InvalidOperationException` without modifying `destination` when the expression contains `Where`. To materialize a filtered expression, call `expr.Evaluate(workspace)` to obtain a `GpuBigFloatArrayView` sliced to the exact surviving count.
4. **Empty Sequences**:
   When a sequence has `0` elements (either statically via `Take(0)` / `Skip` past the end, or dynamically when `Where` selects `0` elements):
   - `Max(workspace)`, `Min(workspace)`, and `Average(workspace)` throw `InvalidOperationException` (`"Sequence contains no elements."`).
   - `Sum(workspace)` returns `+0` (`IsNegative == false`, zero significand of `WordsPerElement` words).
   - `Count(workspace)` (and `Count()` when unfiltered) returns `0`.
5. **Combining Filtered Expressions (`Zip*`, Vector `Is*`, and `Where`)**:
   Combining two separately constructed `Where` expressions element-by-element in `ZipAdd`, `ZipSubtract`, `ZipMultiply`, `ZipDivide`, `ZipPow`, vector-to-vector `Is*` comparisons, or `Where` throws `NotSupportedException` (`"Cannot combine two filtered vector expressions whose lengths are only known at evaluation time."`). Two branches derived from the *same* `Where` node with identical `Skip` and `Take` offsets share the same device count and can be combined safely.

## Examples

### 1. Scalar and Transcendental Projections (`Select*`)

```csharp
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);
using var input = new GpuBigFloatArray(workspace, length: 4, wordsPerElement: 4);
using var offset = new GpuBigFloat(new GpuBigUInt(workspace, 1UL), exponent: 0, isNegative: false);

for (int i = 0; i < 4; i++)
{
    using var val = new GpuBigFloat(new GpuBigUInt(workspace, (ulong)(i + 1)), exponent: 0, isNegative: false);
    input.SetElement(i, val);
}

// Compute ln(sqrt(x + 1)) / 2 across the batch
using IGpuBigFloatVectorExpression expr = input.AsVectorizedEnumerable()
    .SelectAdd(offset)
    .SelectSqrt()
    .SelectLog()
    .SelectDivideByUInt32(2u);

GpuBigFloatArrayView view = expr.Evaluate(workspace);
```

### 2. Element-Wise Binary Operations (`Zip*`)

```csharp
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);
using var left = new GpuBigFloatArray(workspace, length: 4, wordsPerElement: 4);
using var right = new GpuBigFloatArray(workspace, length: 4, wordsPerElement: 4);

for (int i = 0; i < 4; i++)
{
    using var a = new GpuBigFloat(new GpuBigUInt(workspace, (ulong)(i + 2)), exponent: 0, isNegative: false);
    using var b = new GpuBigFloat(new GpuBigUInt(workspace, 2UL), exponent: 0, isNegative: false);
    left.SetElement(i, a);
    right.SetElement(i, b);
}

using IGpuBigFloatVectorExpression leftVec = left.AsVectorizedEnumerable();
using IGpuBigFloatVectorExpression rightVec = right.AsVectorizedEnumerable();
using IGpuBigFloatVectorExpression zipExpr = leftVec.ZipMultiply(rightVec).ZipAdd(leftVec);

using var destination = new GpuBigFloatArray(workspace, length: 4, wordsPerElement: zipExpr.WordsPerElement);
zipExpr.EvaluateTo(workspace, destination);
```

### 3. Relational Comparisons, Filtering (`Where`), Slicing (`Skip` / `Take`), and `Count`

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);
using var values = new GpuBigFloatArray(workspace, length: 8, wordsPerElement: 4);
using var threshold = new GpuBigFloat(new GpuBigUInt(workspace, 3UL), exponent: 0, isNegative: false);

for (int i = 0; i < 8; i++)
{
    using var v = new GpuBigFloat(new GpuBigUInt(workspace, (ulong)(i + 1)), exponent: 0, isNegative: false);
    values.SetElement(i, v);
}

using IGpuBigFloatVectorExpression source = values.AsVectorizedEnumerable();
using IGpuBoolVectorExpression predicate = source.IsGreaterThan(threshold);
using IGpuBigFloatVectorExpression filtered = source.Where(predicate).Skip(1).Take(3);

// Filtered length is read from the device via Count(workspace) or Evaluate(workspace)
int survivingCount = filtered.Count(workspace);
GpuBigFloatArrayView compactedView = filtered.Evaluate(workspace);
Console.WriteLine($"Count: {survivingCount}, View Length: {compactedView.Length}");
```

### 4. Terminal Reductions (`Sum`, `Min`, `Max`, `Average`)

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);
using var data = new GpuBigFloatArray(workspace, length: 4, wordsPerElement: 4);

for (int i = 0; i < 4; i++)
{
    using var v = new GpuBigFloat(new GpuBigUInt(workspace, (ulong)((i + 1) * 10)), exponent: 0, isNegative: false);
    data.SetElement(i, v);
}

using IGpuBigFloatVectorExpression vec = data.AsVectorizedEnumerable();
using GpuBigFloat sum = vec.Sum(workspace);
using GpuBigFloat min = vec.Min(workspace);
using GpuBigFloat max = vec.Max(workspace);
using GpuBigFloat avg = vec.Average(workspace);

Console.WriteLine($"Sum bit length: {sum.Magnitude.Significand.GetBitLength()}, Avg exponent: {avg.Magnitude.Exponent}");
```

## See also

### BCL API > Math (BigNum)

- [IGpuBigFloatVectorExpression Interface](IGpuBigFloatVectorExpression.md)
- [GpuBigFloatArray Struct](GpuBigFloatArray.md)
- [GpuBigFloatArrayView Struct](GpuBigFloatArrayView.md)
- [IGpuBoolVectorExpression Interface](../gpu_big_int_and_uint/expressions/IGpuBoolVectorExpression.md)
- [GpuBigIntVectorLinqExtensions Class](../gpu_big_int_and_uint/expressions/GpuBigIntVectorLinqExtensions.md)
- [Arbitrary-Precision Floats (GpuBigFloat)](00_index.md)
