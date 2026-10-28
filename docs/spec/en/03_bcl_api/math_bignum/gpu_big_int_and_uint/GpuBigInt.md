---
title: "GpuBigInt Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "A lightweight value type wrapping a GpuBigUInt with a sign bit, used for signed arbitrary-precision arithmetic and series summation on NVIDIA GPUs."
---

## GpuBigInt Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

A lightweight value type wrapping an arbitrary-precision [`GpuBigUInt`](GpuBigUInt.md) with a sign bit, used for signed arbitrary-precision arithmetic and series summation on NVIDIA GPUs.

```csharp
public readonly struct GpuBigInt : IEquatable<GpuBigInt>, IComparable<GpuBigInt>, IDisposable
```

Implements: `IEquatable<GpuBigInt>`, `IComparable<GpuBigInt>`, `IDisposable`

## Constructors

| Name | Description |
| --- | --- |
| [GpuBigInt(GpuBigUInt, Boolean)](#gpubigintgpubiguint-boolean) | Initializes a new instance of the `GpuBigInt` struct. |

## GpuBigInt(GpuBigUInt, Boolean)

Initializes a new instance of the `GpuBigInt` struct.

```csharp
public GpuBigInt([TakesOwnership] GpuBigUInt value, bool isNegative);
```

#### Parameters

`value` _[GpuBigUInt](GpuBigUInt.md)_<br>
The underlying `GpuBigUInt` representing the absolute magnitude. Takes ownership of the buffer.

`isNegative` _Boolean_<br>
`true` if the integer is negative; otherwise, `false`.

## Properties

| Name | Description |
| --- | --- |
| [IsNegative](#isnegative) | Gets a value indicating whether the signed integer is strictly negative. |
| [Value](#value) | Gets the underlying absolute value buffer stored in GPU device memory. |

## IsNegative

Gets a value indicating whether the signed integer is strictly negative.

```csharp
public bool IsNegative { get; }
```

## Value

Gets the underlying absolute value buffer stored in GPU device memory.

```csharp
public GpuBigUInt Value { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [CompareTo(GpuBigInt)](#comparetogpubigint) | Compares this instance with another `GpuBigInt` and returns an integer that indicates whether this instance precedes, follows, or appears in the same position in the sort order as the parameter. |
| [Dispose()](#dispose) | Releases the GPU global device memory resources allocated for the underlying `Value`. |
| [Equals(GpuBigInt)](#equalsgpubigint) | Indicates whether the current instance has the same magnitude buffer and the same sign as another `GpuBigInt`. |
| [Equals(Object?)](#equalsobject) | Determines whether the specified object is equal to the current instance. |
| [GetHashCode()](#gethashcode) | Returns the hash code of the buffer identity and the sign, consistent with `Equals(GpuBigInt)`. |

## CompareTo(GpuBigInt)

Compares this instance with another `GpuBigInt` and returns an integer that indicates whether this instance precedes, follows, or appears in the same position in the sort order as the specified parameter.

```csharp
public int CompareTo(GpuBigInt other);
```

#### Parameters

`other` _[GpuBigInt](GpuBigInt.md)_<br>
A `GpuBigInt` to compare with this instance.

#### Returns

_Int32_<br>
A signed integer that indicates the relative values of this instance and `other`: less than zero if this instance is less than `other`; zero if this instance equals `other`; greater than zero if this instance is greater than `other`. Both `+0` and `-0` compare as equal.

## Dispose()

Releases the GPU global device memory resources allocated for the underlying `Value`.

```csharp
public void Dispose();
```

## Equals(GpuBigInt)

Indicates whether the current instance has the same magnitude buffer and the same sign as another `GpuBigInt`.

```csharp
public bool Equals(GpuBigInt other);
```

#### Parameters

`other` _[GpuBigInt](GpuBigInt.md)_<br>
A `GpuBigInt` to compare with this instance.

#### Returns

_Boolean_<br>
`true` if `Value` refers to the same buffer (`GpuBigUInt.Equals(GpuBigUInt)`) and `IsNegative` is the same; otherwise, `false`.

#### Remarks

Unlike the value-equal float types, `Equals` and `GetHashCode` compare identity: two separate buffers holding the same value (including `+0` and `-0`) are not equal. Use `==` or `CompareTo` to compare values on the GPU. Neither member reads device memory, so both also work on a disposed value.

## Equals(Object?)

Determines whether the specified object is equal to the current instance.

```csharp
public override bool Equals(object? obj);
```

#### Parameters

`obj` _Object?_<br>
The object to compare with the current instance.

#### Returns

_Boolean_<br>
`true` if `obj` is a `GpuBigInt` and equals the current instance; otherwise, `false`.

## GetHashCode()

Returns the hash code of the buffer identity and the sign, consistent with `Equals(GpuBigInt)`.

```csharp
public override int GetHashCode();
```

#### Returns

_Int32_<br>
A 32-bit signed integer hash code.

## Operators

| Name | Description |
| --- | --- |
| `operator +(GpuBigInt, GpuBigInt)` | Creates a deferred [`GpuBigIntExpr`](expressions/GpuBigIntExpr.md) representing the addition of two signed big integers. |
| `operator -(GpuBigInt, GpuBigInt)` | Creates a deferred [`GpuBigIntExpr`](expressions/GpuBigIntExpr.md) representing the subtraction of two signed big integers. |
| `operator *(GpuBigInt, GpuBigInt)` | Creates a deferred [`GpuBigIntExpr`](expressions/GpuBigIntExpr.md) representing the multiplication of two signed big integers. |
| `operator /(GpuBigInt, GpuBigInt)` | Creates a deferred [`GpuBigIntExpr`](expressions/GpuBigIntExpr.md) representing the division of two signed big integers. |
| `operator ==(GpuBigInt, GpuBigInt)` | Determines whether two specified `GpuBigInt` instances have the same value on the GPU (`+0` equals `-0`). |
| `operator !=(GpuBigInt, GpuBigInt)` | Determines whether two specified `GpuBigInt` instances have different values on the GPU. |
| `operator <(GpuBigInt, GpuBigInt)` | Determines whether one specified `GpuBigInt` is less than another specified `GpuBigInt`. |
| `operator >(GpuBigInt, GpuBigInt)` | Determines whether one specified `GpuBigInt` is greater than another specified `GpuBigInt`. |
| `operator <=(GpuBigInt, GpuBigInt)` | Determines whether one specified `GpuBigInt` is less than or equal to another specified `GpuBigInt`. |
| `operator >=(GpuBigInt, GpuBigInt)` | Determines whether one specified `GpuBigInt` is greater than or equal to another specified `GpuBigInt`. |

## Examples

### Lazy Evaluation (Host Orchestration)

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
#nullable enable
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// Execution Context: Host (CPU)
using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

using var uintA = GpuBigUInt.FromBigInteger(workspace, new BigInteger(100), 4);
using var uintB = GpuBigUInt.FromBigInteger(workspace, new BigInteger(50), 4);

// Wrap with a sign to represent signed integers
var valA = new GpuBigInt(uintA, isNegative: false);
var valB = new GpuBigInt(uintB, isNegative: true);

// Operator overloads on CPU return AST expressions (Lazy Evaluation)
GpuBigIntExpr sumExpr = valA + valB;

// Evaluate dynamically to a new buffer
using GpuBigInt sumResult = sumExpr.Node.Evaluate(workspace, null);

Console.WriteLine($"Sum evaluated to={sumResult.Value.ToBigInteger(sumResult.IsNegative)}");
```

### Immediate Evaluation (GPU)

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

// Execution Context: Host (CPU)
Example.ComputeBigIntMath();

public class Example
{
    // Execution Context: G# Kernel (GPU)
    [GpuKernel]
    public static void ComputeBigIntMath()
    {
        // On the GPU device, dynamic AST creation is prohibited.
        // Instead, we use fixed-width value types (e.g., GpuUInt128) for immediate zero-allocation evaluation.
        GpuUInt128 sigA = [150];
        GpuUInt128 sigB = [50];
        GpuUInt128 resultSig = sigA + sigB;

        Console.WriteLine($"Result={resultSig.Value.Limbs[0]}");
    }
}
```

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuWorkspace](../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)

### Guides

- [4. Practical Guides and Best Practices](../../../04_guides/00_index.md)
- [7. Stdlib Math and Lazy Evaluation](../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
- [4. BigNum Expression Trees Guide](../../../04_guides/04_bignum_expression_trees_guide.md)
