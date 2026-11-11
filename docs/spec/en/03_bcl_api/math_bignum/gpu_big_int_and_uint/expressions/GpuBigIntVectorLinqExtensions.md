---
title: "GpuBigIntVectorLinqExtensions Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Provides batched and vectorized LINQ operations for contiguous GpuBigIntArray pipelines."
---

## GpuBigIntVectorLinqExtensions Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Provides batched and vectorized LINQ extension methods for [GpuBigIntArray](../GpuBigIntArray.md), compiling deferred query expressions into fused multi-block GPU kernels and parallel reduction trees.

```csharp
public static class GpuBigIntVectorLinqExtensions
```

## Methods

| Name | Description |
| --- | --- |
| [All(IGpuBoolVectorExpression)](#boolean-combinators-and-quantifiers-and-or-not-any-all) | Determines whether all elements of a boolean vector expression evaluate to `true`. |
| [And(IGpuBoolVectorExpression, IGpuBoolVectorExpression)](#boolean-combinators-and-quantifiers-and-or-not-any-all) | Performs an element-wise logical AND on two boolean vector expressions. |
| [Any(IGpuBoolVectorExpression)](#boolean-combinators-and-quantifiers-and-or-not-any-all) | Determines whether any element of a boolean vector expression evaluates to `true`. |
| [AsVectorizedEnumerable(GpuBigIntArray)](#asvectorizedenumerablegpubigintarray) | Converts a `GpuBigIntArray` into a vectorized deferred expression. |
| [Count(IGpuBigIntVectorExpression)](#sum--max--min--count) | Computes the number of elements in the vectorized expression. |
| [IsEqualTo(IGpuBigIntVectorExpression, GpuBigInt)](#element-wise-comparisons-isequalto-isnotequalto-isgreaterthan-isgreaterthanorequalto-islessthan-islessthanorequalto) | Evaluates if each element is equal to a scalar. |
| [IsEqualTo(IGpuBigIntVectorExpression, IGpuBigIntVectorExpression)](#element-wise-comparisons-isequalto-isnotequalto-isgreaterthan-isgreaterthanorequalto-islessthan-islessthanorequalto) | Evaluates if elements in the left vector are equal to the right vector element-wise. |
| [IsGreaterThan(IGpuBigIntVectorExpression, GpuBigInt)](#element-wise-comparisons-isequalto-isnotequalto-isgreaterthan-isgreaterthanorequalto-islessthan-islessthanorequalto) | Evaluates if each element is greater than a scalar. |
| [IsGreaterThan(IGpuBigIntVectorExpression, IGpuBigIntVectorExpression)](#element-wise-comparisons-isequalto-isnotequalto-isgreaterthan-isgreaterthanorequalto-islessthan-islessthanorequalto) | Evaluates if elements in the left vector are greater than the right vector element-wise. |
| [IsGreaterThanOrEqualTo(IGpuBigIntVectorExpression, GpuBigInt)](#element-wise-comparisons-isequalto-isnotequalto-isgreaterthan-isgreaterthanorequalto-islessthan-islessthanorequalto) | Evaluates if each element is greater than or equal to a scalar. |
| [IsGreaterThanOrEqualTo(IGpuBigIntVectorExpression, IGpuBigIntVectorExpression)](#element-wise-comparisons-isequalto-isnotequalto-isgreaterthan-isgreaterthanorequalto-islessthan-islessthanorequalto) | Evaluates if elements in the left vector are greater than or equal to the right vector element-wise. |
| [IsLessThan(IGpuBigIntVectorExpression, GpuBigInt)](#element-wise-comparisons-isequalto-isnotequalto-isgreaterthan-isgreaterthanorequalto-islessthan-islessthanorequalto) | Evaluates if each element is less than a scalar. |
| [IsLessThan(IGpuBigIntVectorExpression, IGpuBigIntVectorExpression)](#element-wise-comparisons-isequalto-isnotequalto-isgreaterthan-isgreaterthanorequalto-islessthan-islessthanorequalto) | Evaluates if elements in the left vector are less than the right vector element-wise. |
| [IsLessThanOrEqualTo(IGpuBigIntVectorExpression, GpuBigInt)](#element-wise-comparisons-isequalto-isnotequalto-isgreaterthan-isgreaterthanorequalto-islessthan-islessthanorequalto) | Evaluates if each element is less than or equal to a scalar. |
| [IsLessThanOrEqualTo(IGpuBigIntVectorExpression, IGpuBigIntVectorExpression)](#element-wise-comparisons-isequalto-isnotequalto-isgreaterthan-isgreaterthanorequalto-islessthan-islessthanorequalto) | Evaluates if elements in the left vector are less than or equal to the right vector element-wise. |
| [IsNotEqualTo(IGpuBigIntVectorExpression, GpuBigInt)](#element-wise-comparisons-isequalto-isnotequalto-isgreaterthan-isgreaterthanorequalto-islessthan-islessthanorequalto) | Evaluates if each element is not equal to a scalar. |
| [IsNotEqualTo(IGpuBigIntVectorExpression, IGpuBigIntVectorExpression)](#element-wise-comparisons-isequalto-isnotequalto-isgreaterthan-isgreaterthanorequalto-islessthan-islessthanorequalto) | Evaluates if elements in the left vector are not equal to the right vector element-wise. |
| [Max(IGpuBigIntVectorExpression)](#sum--max--min--count) | Computes the maximum value of a vectorized expression. |
| [Min(IGpuBigIntVectorExpression)](#sum--max--min--count) | Computes the minimum value of a vectorized expression. |
| [Not(IGpuBoolVectorExpression)](#boolean-combinators-and-quantifiers-and-or-not-any-all) | Performs an element-wise logical NOT on a boolean vector expression. |
| [Or(IGpuBoolVectorExpression, IGpuBoolVectorExpression)](#boolean-combinators-and-quantifiers-and-or-not-any-all) | Performs an element-wise logical OR on two boolean vector expressions. |
| [SelectAdd(IGpuBigIntVectorExpression, GpuBigInt)](#selectadd--selectsubtract--selectmultiply) | Adds a scalar to each element of a vectorized expression. |
| [SelectDivide(IGpuBigIntVectorExpression, GpuBigInt)](#selectdivide) | Divides each element of a vectorized expression by a scalar. |
| [SelectDivideByUInt32(IGpuBigIntVectorExpression, UInt32)](#selectdividebyuint32) | Divides each element of a vectorized expression by a 32-bit unsigned integer scalar. |
| [SelectModulo(IGpuBigIntVectorExpression, GpuBigInt)](#selectmodulo) | Computes the remainder (modulo) of each element of a vectorized expression divided by a scalar. |
| [SelectMultiply(IGpuBigIntVectorExpression, GpuBigInt)](#selectadd--selectsubtract--selectmultiply) | Multiplies each element of a vectorized expression by a scalar on GPU device. |
| [SelectSqrt(IGpuBigIntVectorExpression)](#selectsqrt) | Computes the floor integer square root (`isqrt(x)`) of each element in a vectorized expression. |
| [SelectSubtract(IGpuBigIntVectorExpression, GpuBigInt)](#selectadd--selectsubtract--selectmultiply) | Subtracts a scalar from each element of a vectorized expression. |
| [Skip(IGpuBigIntVectorExpression, Int32)](#skip--take) | Bypasses a specified number of elements in a sequence and returns the remaining elements. |
| [Sum(IGpuBigIntVectorExpression)](#sum--max--min--count) | Computes the parallel sum reduction of a vectorized expression. |
| [Take(IGpuBigIntVectorExpression, Int32)](#skip--take) | Returns a specified number of contiguous elements from the start of a sequence. |
| [Where(IGpuBigIntVectorExpression, IGpuBoolVectorExpression)](#whereigpubigintvectorexpression-igpuboolvectorexpression) | Filters a vectorized sequence of values based on a predicate (GPU stream compaction). |
| [ZipAdd(IGpuBigIntVectorExpression, IGpuBigIntVectorExpression)](#zipadd--zipsubtract--zipmultiply) | Adds two vectorized expressions element-wise. |
| [ZipDivide(IGpuBigIntVectorExpression, IGpuBigIntVectorExpression)](#zipdivide) | Divides the first vectorized expression by the second element-wise. |
| [ZipModulo(IGpuBigIntVectorExpression, IGpuBigIntVectorExpression)](#zipmodulo) | Computes the remainder (modulo) of the first vectorized expression divided by the second element-wise. |
| [ZipMultiply(IGpuBigIntVectorExpression, IGpuBigIntVectorExpression)](#zipadd--zipsubtract--zipmultiply) | Multiplies the first vectorized expression by the second element-wise. |
| [ZipSubtract(IGpuBigIntVectorExpression, IGpuBigIntVectorExpression)](#zipadd--zipsubtract--zipmultiply) | Subtracts the second vectorized expression from the first element-wise. |

## AsVectorizedEnumerable(GpuBigIntArray)

Converts a `GpuBigIntArray` into a vectorized deferred expression leaf node.

```csharp
public static IGpuBigIntVectorExpression AsVectorizedEnumerable(this GpuBigIntArray source);
```

## Where(IGpuBigIntVectorExpression, IGpuBoolVectorExpression)

Filters a vectorized sequence of values based on a predicate. Executes a Stream Compaction (`Where`) operation on the GPU.

```csharp
public static IGpuBigIntVectorExpression Where(
    this IGpuBigIntVectorExpression source,
    IGpuBoolVectorExpression predicate);
```

The number of selected elements is computed on the device, and the host does not wait for it: `Count`, `Sum`, `Max`, `Min`, `Any`, `All`, `Skip`, `Take` and the element-wise operations after a `Where` use only the selected elements, as `System.Linq` does. `Length` of the result throws `InvalidOperationException`; `Capacity` is the length of `source`, and `Evaluate` reads the count (one synchronization) and returns exactly the selected elements. `predicate` must have the length of `source` (`ArgumentException`).

Two separately built `Where` results cannot be combined element-wise, because their counts are known only on the device (`NotSupportedException`): write `v.Where(p.And(q))`. `v.Where(p).Where(m)` works when `m` has the capacity's length.

## SelectAdd / SelectSubtract / SelectMultiply

Applies a scalar addition, subtraction, or multiplication to each element of a vectorized expression using a grid-stride parallel kernel.

Output `WordsPerElement` is `Math.Max(source.WordsPerElement, scalar.NumWords) + 1` for `SelectAdd` and `SelectSubtract`, so the sum or difference never wraps, and `source.WordsPerElement + scalar.NumWords` for `SelectMultiply`.

```csharp
public static IGpuBigIntVectorExpression SelectAdd(this IGpuBigIntVectorExpression source, GpuBigInt scalar);
public static IGpuBigIntVectorExpression SelectSubtract(this IGpuBigIntVectorExpression source, GpuBigInt scalar);
public static IGpuBigIntVectorExpression SelectMultiply(this IGpuBigIntVectorExpression source, GpuBigInt scalar);
```

## ZipAdd / ZipSubtract / ZipMultiply

Performs element-wise addition, subtraction, or multiplication across two vectorized expressions of equal length.

Output `WordsPerElement` is `Math.Max(left.WordsPerElement, right.WordsPerElement) + 1` for `ZipAdd` and `ZipSubtract`, so the sum or difference never wraps, and `left.WordsPerElement + right.WordsPerElement` for `ZipMultiply`.

```csharp
public static IGpuBigIntVectorExpression ZipAdd(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right);
public static IGpuBigIntVectorExpression ZipSubtract(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right);
public static IGpuBigIntVectorExpression ZipMultiply(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right);
```

## SelectDivide

Divides each element of `source` by `scalar`, truncating the quotient toward zero as C# integer division and
`BigInteger.Divide` do. A non-zero quotient is negative when `source` and `scalar` have opposite signs, and a zero
quotient is normalized to `+0`. Output `WordsPerElement` is `Math.Max(source.WordsPerElement, scalar.NumWords)`.

```csharp
public static IGpuBigIntVectorExpression SelectDivide(
    this IGpuBigIntVectorExpression source,
    GpuBigInt scalar);
```

When the vector is non-empty and `scalar` is zero, evaluation throws `DivideByZeroException` before the
division runs. Passing a `targetResult` with a different `WordsPerElement` or fewer elements than evaluated throws
`ArgumentException` before evaluating the inputs. When `WordsPerElement` exceeds 512 words, each evaluation allocates
`2 * width` words of temporary device memory per element (4 bytes each: about 480 MB for 100,000 elements of 600 words)
and releases it after the launch.

```csharp
using var array = new GpuBigIntArray(workspace, length: 3, wordsPerElement: 1);
workspace.Provider.CopyHostToDevice(array.Values.DevicePointer, [100u, 47u, 5u], 3, workspace.Stream);
workspace.Provider.CopyHostToDevice(array.Signs.DevicePointer, [0u, 1u, 1u], 3, workspace.Stream);

using var divisorMag = GpuBigUInt.FromBigInteger(workspace, 7, minWords: 1);
var divisor = new GpuBigInt(divisorMag, isNegative: false);

using var quotients = array.AsVectorizedEnumerable().SelectDivide(divisor);
GpuBigIntArrayView view = quotients.Evaluate(workspace); // [14, -6, 0]
```

## SelectDivideByUInt32

Divides each element of `source` by a 32-bit unsigned integer `divisor`, truncating the quotient toward zero. A
non-zero quotient keeps the sign of the source element, and a zero quotient is normalized to `+0`. Output
`WordsPerElement` equals `source.WordsPerElement`.

```csharp
public static IGpuBigIntVectorExpression SelectDivideByUInt32(
    this IGpuBigIntVectorExpression source,
    uint divisor);
```

Evaluation throws `DivideByZeroException` immediately when `divisor == 0`, before evaluating `source` or inspecting
`targetResult` (including on an empty vector). Passing a `targetResult` with a different `WordsPerElement` or fewer
elements than evaluated throws `ArgumentException`. When `source` is filtered by `Where`, evaluation reads the device
element count (one synchronization) and divides only the selected elements.

```csharp
using var array = new GpuBigIntArray(workspace, length: 3, wordsPerElement: 1);
workspace.Provider.CopyHostToDevice(array.Values.DevicePointer, [100u, 47u, 5u], 3, workspace.Stream);
workspace.Provider.CopyHostToDevice(array.Signs.DevicePointer, [0u, 1u, 1u], 3, workspace.Stream);

using var quotients = array.AsVectorizedEnumerable().SelectDivideByUInt32(7u);
GpuBigIntArrayView view = quotients.Evaluate(workspace); // [14, -6, 0]
```

## SelectModulo

Computes the remainder of dividing each element of `source` by `scalar`, matching C# `%` and `BigInteger.Remainder`
(remainder of division truncated toward zero). A non-zero remainder takes the sign of the dividend (`source`),
independent of the sign of `scalar`, and a zero remainder is normalized to `+0`. Output `WordsPerElement` is
`Math.Max(source.WordsPerElement, scalar.NumWords)`.

```csharp
public static IGpuBigIntVectorExpression SelectModulo(
    this IGpuBigIntVectorExpression source,
    GpuBigInt scalar);
```

When the vector is non-empty and `scalar` is zero, evaluation throws `DivideByZeroException` before the
division runs. Passing a `targetResult` with a different `WordsPerElement` or fewer elements than evaluated throws
`ArgumentException` before evaluating the inputs. When `WordsPerElement` exceeds 512 words, each evaluation allocates
`2 * width` words of temporary device memory per element (4 bytes each: about 480 MB for 100,000 elements of 600 words)
and releases it after the launch.

```csharp
using var array = new GpuBigIntArray(workspace, length: 3, wordsPerElement: 1);
workspace.Provider.CopyHostToDevice(array.Values.DevicePointer, [100u, 47u, 14u], 3, workspace.Stream);
workspace.Provider.CopyHostToDevice(array.Signs.DevicePointer, [0u, 1u, 1u], 3, workspace.Stream);

using var divisorMag = GpuBigUInt.FromBigInteger(workspace, 7, minWords: 1);
var negativeDivisor = new GpuBigInt(divisorMag, isNegative: true);

using var remainders = array.AsVectorizedEnumerable().SelectModulo(negativeDivisor);
GpuBigIntArrayView view = remainders.Evaluate(workspace); // [2, -5, 0]
```

## SelectSqrt

Computes the floor integer square root ($\lfloor\sqrt{x}\rfloor$, the largest non-negative integer $r$ with
$r^2 \le x$) of each element in `source`. Every result is non-negative (`+`), and a negative zero element produces
`+0`. Output `WordsPerElement` equals `source.WordsPerElement`.

```csharp
public static IGpuBigIntVectorExpression SelectSqrt(this IGpuBigIntVectorExpression source);
```

Evaluation throws `InvalidOperationException` if any evaluated element is strictly negative (`< 0`). When `source` is
filtered by `Where`, evaluation reads the device element count (one synchronization) and computes roots only for the
selected elements, so excluded negative elements do not throw. Passing a `targetResult` with a different
`WordsPerElement` or fewer elements than evaluated throws `ArgumentException`.

```csharp
using var array = new GpuBigIntArray(workspace, length: 4, wordsPerElement: 1);
workspace.Provider.CopyHostToDevice(array.Values.DevicePointer, [0u, 15u, 16u, 10000u], 4, workspace.Stream);

using var roots = array.AsVectorizedEnumerable().SelectSqrt();
GpuBigIntArrayView view = roots.Evaluate(workspace); // [0, 3, 4, 100]
```

## ZipDivide

Divides each element of `left` by the corresponding element of `right`, truncating the quotient toward zero as C#
integer division and `BigInteger.Divide` do. A non-zero quotient is negative when the paired elements have opposite
signs, and a zero quotient is normalized to `+0`. Output `WordsPerElement` is
`Math.Max(left.WordsPerElement, right.WordsPerElement)`.

```csharp
public static IGpuBigIntVectorExpression ZipDivide(
    this IGpuBigIntVectorExpression left,
    IGpuBigIntVectorExpression right);
```

Evaluation throws `DivideByZeroException` after the launch if the divisor of any evaluated element in `right` is zero
(elements past a `Where` count are ignored). Because that check is read back from the device (one synchronization of
the workspace stream), evaluating a non-empty `ZipDivide` while the stream captures a CUDA graph throws
`NotSupportedException`. Combining two separately built `Where` results also throws `NotSupportedException`. Mismatched
vector lengths or an incompatible `targetResult` throw `ArgumentException`. When `WordsPerElement` exceeds 512 words,
each evaluation allocates `2 * width` words of temporary device memory per element (4 bytes each: about 480 MB for
100,000 elements of 600 words) and releases it after the launch.

```csharp
using var left = new GpuBigIntArray(workspace, length: 4, wordsPerElement: 1);
using var right = new GpuBigIntArray(workspace, length: 4, wordsPerElement: 1);
workspace.Provider.CopyHostToDevice(left.Values.DevicePointer, [100u, 47u, 25u, 81u], 4, workspace.Stream);
workspace.Provider.CopyHostToDevice(left.Signs.DevicePointer, [0u, 1u, 0u, 1u], 4, workspace.Stream);
workspace.Provider.CopyHostToDevice(right.Values.DevicePointer, [7u, 6u, 4u, 9u], 4, workspace.Stream);
workspace.Provider.CopyHostToDevice(right.Signs.DevicePointer, [0u, 0u, 1u, 1u], 4, workspace.Stream);

using var quotients = left.AsVectorizedEnumerable().ZipDivide(right.AsVectorizedEnumerable());
GpuBigIntArrayView view = quotients.Evaluate(workspace); // [14, -7, -6, 9]
```

## ZipModulo

Computes the element-wise remainder of dividing `left` by `right`, matching C# `%` and `BigInteger.Remainder`
(remainder of division truncated toward zero). A non-zero remainder takes the sign of the dividend element in `left`,
independent of the sign of `right`, and a zero remainder is normalized to `+0`. Output `WordsPerElement` is
`Math.Max(left.WordsPerElement, right.WordsPerElement)`.

```csharp
public static IGpuBigIntVectorExpression ZipModulo(
    this IGpuBigIntVectorExpression left,
    IGpuBigIntVectorExpression right);
```

Evaluation throws `DivideByZeroException` after the launch if the divisor of any evaluated element in `right` is zero
(elements past a `Where` count are ignored). Because that check is read back from the device (one synchronization of
the workspace stream), evaluating a non-empty `ZipModulo` while the stream captures a CUDA graph throws
`NotSupportedException`. Combining two separately built `Where` results also throws `NotSupportedException`. Mismatched
vector lengths or an incompatible `targetResult` throw `ArgumentException`. When `WordsPerElement` exceeds 512 words,
each evaluation allocates `2 * width` words of temporary device memory per element (4 bytes each: about 480 MB for
100,000 elements of 600 words) and releases it after the launch.

```csharp
using var left = new GpuBigIntArray(workspace, length: 4, wordsPerElement: 1);
using var right = new GpuBigIntArray(workspace, length: 4, wordsPerElement: 1);
workspace.Provider.CopyHostToDevice(left.Values.DevicePointer, [100u, 47u, 25u, 81u], 4, workspace.Stream);
workspace.Provider.CopyHostToDevice(left.Signs.DevicePointer, [0u, 1u, 0u, 1u], 4, workspace.Stream);
workspace.Provider.CopyHostToDevice(right.Values.DevicePointer, [7u, 6u, 4u, 9u], 4, workspace.Stream);
workspace.Provider.CopyHostToDevice(right.Signs.DevicePointer, [0u, 0u, 1u, 1u], 4, workspace.Stream);

using var remainders = left.AsVectorizedEnumerable().ZipModulo(right.AsVectorizedEnumerable());
GpuBigIntArrayView view = remainders.Evaluate(workspace); // [2, -5, 1, 0]
```

## Skip / Take

Slices a vectorized sequence by bypassing `count` leading elements (`Skip`) or taking `count` contiguous elements from the start (`Take`). Throws `ArgumentNullException` if `source` is `null`.

```csharp
public static IGpuBigIntVectorExpression Skip(this IGpuBigIntVectorExpression source, int count);
public static IGpuBigIntVectorExpression Take(this IGpuBigIntVectorExpression source, int count);
```

## Sum / Max / Min / Count

Computes parallel reductions over all elements in a vectorized expression, returning a deferred scalar [GpuBigIntExpr](GpuBigIntExpr.md).

```csharp
public static GpuBigIntExpr Sum(this IGpuBigIntVectorExpression source);
public static GpuBigIntExpr Max(this IGpuBigIntVectorExpression source);
public static GpuBigIntExpr Min(this IGpuBigIntVectorExpression source);
public static GpuBigIntExpr Count(this IGpuBigIntVectorExpression source);
```

`Max` and `Min` return a value with the same number of words as the elements (`WordsPerElement`). `Sum` never overflows: its bound is `BitsPerElement + ceil(log2 Capacity)` bits, and the result has the words of that bound, at most one word more than the elements:

- The reduction accumulates in one word more than the elements, so every value and every intermediate sum is exact.
- `Evaluate(destination)` writes the magnitude of the result into `destination` and returns a `GpuBigInt` that wraps `destination` with the sign of the result. `destination` must have at least the words of the result (`NumWords` of the expression's node; `ArgumentException` otherwise, before anything is written); a wider one gets zeros above the magnitude.
- `Evaluate(workspace)` allocates a new buffer for the magnitude; the caller owns it and disposes it. If evaluation throws, that buffer is released.
- When `Evaluate(destination)` throws, a destination that is too narrow (`ArgumentException`) is not touched; after an empty `Max` or `Min` reached only on the device (after `Where`) its contents are unspecified.
- Evaluation reads two words back from the device: the sign and the number of reduced elements.
- A difference of equal magnitudes (`ZipSubtract`, `SelectSubtract`) has the sign `+`, as `BigInteger` has no negative zero.
- An empty vector behaves as in `System.Linq`: `Max` and `Min` throw `InvalidOperationException` ("Sequence contains no elements") on evaluation without evaluating the vector or launching anything, so a destination is not touched, `Sum` is +0 (a destination is cleared), and `Count` is 0.

## Element-Wise Comparisons (IsEqualTo, IsNotEqualTo, IsGreaterThan, IsGreaterThanOrEqualTo, IsLessThan, IsLessThanOrEqualTo)

Compares each element of a vectorized expression against either a `GpuBigInt` scalar or another `IGpuBigIntVectorExpression`, returning an `IGpuBoolVectorExpression`.

The comparison is signed and agrees with `BigInteger.CompareTo`: `-100 < 50`, `-3 != 3`, and `-100 < -3`. A negative zero (sign set, magnitude 0) equals zero. The two sides may have different word counts: each side is read with its own word count, and the missing upper words of the narrower side are 0, so a 4-word `3` equals an 8-word `3`. A scalar is read with its own word count, which may be smaller or larger than the elements'. Two vectors must have the same length (`ArgumentException`).

```csharp
public static IGpuBoolVectorExpression IsEqualTo(this IGpuBigIntVectorExpression source, GpuBigInt scalar);
public static IGpuBoolVectorExpression IsNotEqualTo(this IGpuBigIntVectorExpression source, GpuBigInt scalar);
public static IGpuBoolVectorExpression IsGreaterThan(this IGpuBigIntVectorExpression source, GpuBigInt scalar);
public static IGpuBoolVectorExpression IsGreaterThanOrEqualTo(this IGpuBigIntVectorExpression source, GpuBigInt scalar);
public static IGpuBoolVectorExpression IsLessThan(this IGpuBigIntVectorExpression source, GpuBigInt scalar);
public static IGpuBoolVectorExpression IsLessThanOrEqualTo(this IGpuBigIntVectorExpression source, GpuBigInt scalar);

public static IGpuBoolVectorExpression IsEqualTo(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right);
public static IGpuBoolVectorExpression IsNotEqualTo(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right);
public static IGpuBoolVectorExpression IsGreaterThan(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right);
public static IGpuBoolVectorExpression IsGreaterThanOrEqualTo(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right);
public static IGpuBoolVectorExpression IsLessThan(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right);
public static IGpuBoolVectorExpression IsLessThanOrEqualTo(this IGpuBigIntVectorExpression left, IGpuBigIntVectorExpression right);
```

## Boolean Combinators and Quantifiers (And, Or, Not, Any, All)

Combines boolean vector expressions element-wise (`And`, `Or`, `Not`) or reduces a boolean vector expression to a deferred scalar [IGpuBoolExpr](BoolVectorExpressions.md) (`Any`, `All`).

```csharp
public static IGpuBoolVectorExpression And(this IGpuBoolVectorExpression left, IGpuBoolVectorExpression right);
public static IGpuBoolVectorExpression Or(this IGpuBoolVectorExpression left, IGpuBoolVectorExpression right);
public static IGpuBoolVectorExpression Not(this IGpuBoolVectorExpression source);
public static IGpuBoolExpr Any(this IGpuBoolVectorExpression source);
public static IGpuBoolExpr All(this IGpuBoolVectorExpression source);
```

## Remarks

Unlike standard LINQ or scalar expression trees that evaluate element-by-element, `GpuBigIntVectorLinqExtensions` constructs execution plans that operate directly over contiguous arrays. Reduction operations (`Sum`, `Max`, `Min`, `Count`) automatically partition the workload into multi-pass chunk kernels, scaling efficiently up to millions of elements.

`Count` returns a [GpuBigIntExpr](GpuBigIntExpr.md) that can be compiled into a native CUDA Graph; after a `Where`, every launch counts the selected elements again on the device:
- **One-Liner Evaluation:** Chain `.EvaluateWithCudaGraph(workspace)` to capture, instantiate, execute, and destroy a CUDA Graph in a single statement.
- **Precompiled Reusable Execution:** Chain `.CompileToGraph(workspace)` to produce a reusable [GpuBigIntExecutableGraph](GpuBigIntExecutableGraph.md) that can be dispatched repeatedly via `.Execute()` or `.ExecuteAsync()` with zero CPU launch latency.

`Sum`, `Max` and `Min` read their sign, emptiness and overflow back from the device, which a capture cannot record: evaluating them while the stream is capturing throws `NotSupportedException`. Evaluate them directly (`Evaluate`).

## Examples

### Vectorized Inner Product and Parallel Sum Reduction

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// Execution Context: Host (CPU)
using var workspace = new GpuWorkspace(65536, GpuExecutionEngine.GSharpAot);

// Allocate contiguous array of 256 elements
using var array = new GpuBigIntArray(workspace, length: 256, wordsPerElement: 4);

// Scalar multiplier
using var scalarUint = GpuBigUInt.FromBigInteger(workspace, new BigInteger(7), minWords: 4);
var scalar = new GpuBigInt(scalarUint, isNegative: false);

// SelectMultiply widens each element to 4 + 4 = 8 words (256 bits), and Sum over 256 elements
// adds ceil(log2 256) = 8 bits, so the destination needs ceil(264 / 32) = 9 words.
using var sumExpr = array.AsVectorizedEnumerable()
    .SelectMultiply(scalar)
    .Sum();
using var dest = new GpuBigUInt(workspace, sumExpr.Node.NumWords);

var result = sumExpr.Evaluate(dest);

Console.WriteLine($"Reduction result: {result.Value.ToBigInteger(result.IsNegative)}");
```

### One-Liner CUDA Graph Execution

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

using var workspace = new GpuWorkspace(65536, GpuExecutionEngine.GSharpAot);
using var array = new GpuBigIntArray(workspace, length: 256, wordsPerElement: 4);
using var scalarUint = GpuBigUInt.FromBigInteger(workspace, new BigInteger(7), minWords: 4);
var scalar = new GpuBigInt(scalarUint, isNegative: false);

// Count the elements above the scalar with a one-liner CUDA Graph execution
using var graphResult = array.AsVectorizedEnumerable()
    .Where(array.AsVectorizedEnumerable().IsGreaterThan(scalar))
    .Count()
    .EvaluateWithCudaGraph(workspace);

Console.WriteLine($"Graph result: {graphResult.Value.ToBigInteger(graphResult.IsNegative)}");
```

### Precompiled CUDA Graph for High-Frequency Repetition

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

using var workspace = new GpuWorkspace(65536, GpuExecutionEngine.GSharpAot);
using var array = new GpuBigIntArray(workspace, length: 256, wordsPerElement: 4);
using var scalarUint = GpuBigUInt.FromBigInteger(workspace, new BigInteger(7), minWords: 4);
var scalar = new GpuBigInt(scalarUint, isNegative: false);

// Compile the vector pipeline AST into a reusable CUDA Graph
using var precompiledGraph = array.AsVectorizedEnumerable()
    .Where(array.AsVectorizedEnumerable().IsGreaterThan(scalar))
    .Count()
    .CompileToGraph(workspace);

// Execute the graph repeatedly with minimal driver launch latency; each launch counts again
for (int i = 0; i < 10; i++)
{
    precompiledGraph.Execute();
}

Console.WriteLine($"Precompiled result: {precompiledGraph.Result.Value.ToBigInteger(precompiledGraph.Result.IsNegative)}");
```

## See also

### BCL API > Math Bignum
- [GpuBigIntArray Struct](../GpuBigIntArray.md)
- [GpuBigIntExpr Struct](GpuBigIntExpr.md)
- [GpuBigIntExecutableGraph Struct](GpuBigIntExecutableGraph.md)
- [IGpuBigIntVectorExpression Interface](IGpuBigIntVectorExpression.md)
- [IGpuBoolExpr and VectorCompareOperator](BoolVectorExpressions.md)
