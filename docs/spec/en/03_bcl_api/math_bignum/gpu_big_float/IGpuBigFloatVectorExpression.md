---
title: "IGpuBigFloatVectorExpression Interface (G#)"
site: "G# Documentation"
published: "2026-11-02T00:00:00.000Z"
language: "en"
description: "Defines the contract for deferred, vectorized GPU expressions over GpuBigFloatArray."
---

## IGpuBigFloatVectorExpression Interface

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Represents a deferred, vectorized GPU expression node operating on batches of arbitrary-precision floating-point numbers (`GpuBigFloatArray`). Each node manages its own pooled intermediate device buffers (`GpuBigFloatArray`) and releases them when disposed.

```csharp
public interface IGpuBigFloatVectorExpression : IDisposable
```

## Properties

| Name | Description |
| --- | --- |
| [Capacity](#capacity) | Gets the upper bound on the number of elements in the vectorized expression without synchronizing the device. |
| [Length](#length) | Gets the exact number of elements in the vectorized expression when known on the host. |
| [WordsPerElement](#wordsperelement) | Gets the number of 32-bit significand words allocated per element in this expression's output. |

## Capacity

Gets the upper bound on the number of elements the expression can yield without synchronizing the device. For unfiltered expressions, `Capacity` equals `Length`. For expressions containing `Where`, `Capacity` returns the pre-filter capacity adjusted by any downstream `Skip` and `Take` offsets.

```csharp
public int Capacity => Length;
```

## Length

Gets the exact number of elements in the vectorized expression when statically known on the host.

```csharp
public int Length { get; }
```

#### Exceptions

`InvalidOperationException`<br>
Thrown when the expression tree contains a `Where` filter, because the surviving element count is computed on the device during evaluation. Use [`Capacity`](#capacity) for the upper bound, [`TryGetLength`](#trygetlengthout-int) to check whether the length is known on the host, or [`GpuBigFloatVectorLinqExtensions.Count(GpuWorkspace)`](GpuBigFloatVectorLinqExtensions.md#countgpuworkspace) to evaluate and read the device count.

## WordsPerElement

Gets the number of 32-bit significand words allocated per element in the evaluated result of this expression.

```csharp
public int WordsPerElement { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [Evaluate(GpuWorkspace, int, int?, GpuBigFloatArrayView)](#evaluategpuworkspace-int-int-gpubigfloatarrayview) | Executes the expression tree on the GPU (or CPU fallback) and returns a borrowed `GpuBigFloatArrayView` of the evaluated elements. |
| [TryGetLength(out int)](#trygetlengthout-int) | Attempts to retrieve the exact number of elements on the host without evaluating the expression on the device. |

## Evaluate(GpuWorkspace, int, int?, GpuBigFloatArrayView)

Executes the deferred vectorized expression tree using the specified `workspace` and returns a borrowed [`GpuBigFloatArrayView`](GpuBigFloatArrayView.md) of the result.

```csharp
public GpuBigFloatArrayView Evaluate(
    GpuWorkspace workspace,
    int elementOffset = 0,
    int? elementCount = null,
    GpuBigFloatArrayView targetResult = default);
```

#### Parameters

`workspace` _[GpuWorkspace](../gpu_workspace/GpuWorkspace.md)_<br>
The workspace providing the execution engine, CUDA stream, and memory pool.

`elementOffset` _int_<br>
The zero-based starting element index within the expression to evaluate. Defaults to `0`.

`elementCount` _int?_<br>
The number of elements to evaluate, or `null` to evaluate all remaining elements (`Capacity - elementOffset`).

`targetResult` _[GpuBigFloatArrayView](GpuBigFloatArrayView.md)_<br>
An optional caller-supplied destination view. When non-empty (`!targetResult.IsEmpty`), the expression writes directly into `targetResult` instead of allocating an internal cache buffer.

#### Returns

_[GpuBigFloatArrayView](GpuBigFloatArrayView.md)_<br>
A borrowed view over the evaluated elements. When the expression contains a `Where` filter, `Evaluate` reads the compacted element count from the device once and slices the returned view to the exact number of surviving elements.

#### Exceptions

`ArgumentNullException`<br>
`workspace` is `null`.

`ArgumentOutOfRangeException`<br>
`elementOffset` is negative, `elementCount` is negative, or `elementOffset + elementCount` exceeds `Capacity`.

## TryGetLength(out int)

Checks whether the exact element count is known on the host without executing the expression on the device.

```csharp
public bool TryGetLength(out int length)
{
    length = Length;
    return true;
}
```

#### Parameters

`length` _int_<br>
When this method returns `true`, contains the exact element count; when this method returns `false` (because the expression contains `Where`), set to `0`.

#### Returns

_bool_<br>
`true` if the element count is known on the host; `false` if the expression contains a `Where` filter whose element count is only known on the device after evaluation.

## Remarks

1. **Buffer Lifetime and Disposal**: Intermediate and terminal `IGpuBigFloatVectorExpression` nodes cache their device output buffers across repeated evaluations. Always dispose the root expression (`using var expr = ...`) before disposing the parent `GpuWorkspace`, and do not read the `GpuBigFloatArrayView` returned by `Evaluate` after the expression is disposed.
2. **Filtered Length Rules**: When an expression contains `Where`:
   - `Length` throws `InvalidOperationException`.
   - `TryGetLength(out int length)` returns `false` (with `length = 0`).
   - `Capacity` returns the upper bound on the number of elements without synchronizing the device.
   - `Evaluate(workspace)` reads the compacted element count from the device once and returns a `GpuBigFloatArrayView` whose `Length` equals the exact number of surviving elements.

## Examples

### Inspecting Length vs. Capacity on Unfiltered and Filtered Expressions

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// Execution Context: Host (CPU)
using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);
using var array = new GpuBigFloatArray(workspace, length: 4, wordsPerElement: 4);
using var zero = new GpuBigFloat(workspace, 4);

for (int i = 0; i < 4; i++)
{
    using var val = new GpuBigFloat(new GpuBigUInt(workspace, (ulong)(i + 1)), exponent: 0, isNegative: i % 2 == 1);
    array.SetElement(i, val);
}

using IGpuBigFloatVectorExpression unfiltered = array.AsVectorizedEnumerable();
Console.WriteLine($"Unfiltered TryGetLength: {unfiltered.TryGetLength(out int len)} (Length = {len})");

using IGpuBoolVectorExpression mask = unfiltered.IsGreaterThan(zero);
using IGpuBigFloatVectorExpression filtered = unfiltered.Where(mask);
Console.WriteLine($"Filtered TryGetLength: {filtered.TryGetLength(out _)}, Capacity: {filtered.Capacity}");

GpuBigFloatArrayView result = filtered.Evaluate(workspace);
Console.WriteLine($"Evaluated surviving elements: {result.Length}");
```

## See also

### BCL API > Math (BigNum)

- [GpuBigFloatVectorLinqExtensions Class](GpuBigFloatVectorLinqExtensions.md)
- [GpuBigFloatArray Struct](GpuBigFloatArray.md)
- [GpuBigFloatArrayView Struct](GpuBigFloatArrayView.md)
- [IGpuBigIntVectorExpression Interface](../gpu_big_int_and_uint/expressions/IGpuBigIntVectorExpression.md)
- [IGpuBoolVectorExpression Interface](../gpu_big_int_and_uint/expressions/IGpuBoolVectorExpression.md)
- [Arbitrary-Precision Floats (GpuBigFloat)](00_index.md)
