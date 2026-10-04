---
title: "GpuBigInt Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "A lightweight value type wrapping a GpuBigUInt with a sign bit, used for tracking alternating series or arbitrary signed integers on the GPU."
---

## GpuBigInt Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

A lightweight value type wrapping a `GpuBigUInt` with a sign bit, used for tracking alternating series or arbitrary signed integers on the GPU.

```csharp
public readonly struct GpuBigInt : IDisposable, IEquatable<GpuBigInt>, IComparable<GpuBigInt>
```

## Constructors

| Name | Description |
| --- | --- |
| [GpuBigInt(GpuBigUInt, bool)](#gpubigintegergpuuintbiginteger-bool) | Initializes a new instance of the `GpuBigInt` struct. |

## GpuBigInt(GpuBigUInt, bool)

Initializes a new instance of the `GpuBigInt` struct.

```csharp
public GpuBigInt([TakesOwnership] GpuBigUInt value, bool isNegative);
```

#### Parameters

`value` _GpuUIntBigInteger_<br>
The underlying unsigned integer value.

`isNegative` _bool_<br>
Indicates whether the value is strictly negative.

## Properties

| Name | Description |
| --- | --- |
| [IsNegative](#isnegative) | Gets a value indicating whether the integer is strictly negative. |
| [Value](#value) | Gets the underlying absolute value buffer on the GPU. |

## IsNegative

Gets a value indicating whether the integer is strictly negative.

```csharp
public bool IsNegative { get; }
```

## Value

Gets the underlying absolute value buffer on the GPU.

```csharp
public GpuBigUInt Value { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [CompareTo(GpuBigInt)](#comparetogpubiginteger) | Compares this instance with another `GpuBigInt` and returns an integer that indicates whether this instance precedes, follows, or appears in the same position in the sort order as the specified parameter. |
| [Dispose()](#dispose) | Disposes the underlying GPU buffer. |
| [Equals(GpuBigInt)](#equalsgpubiginteger) | Indicates whether the current object is equal to another object of the same type. |
| [Equals(object?)](#equalsobject) | Indicates whether this instance and a specified object are equal. |
| [GetHashCode()](#gethashcode) | Returns the hash code for this instance. |

## CompareTo(GpuBigInt)

Compares this instance with another `GpuBigInt` and returns an integer that indicates whether this instance precedes, follows, or appears in the same position in the sort order as the specified parameter.

```csharp
public int CompareTo(GpuBigInt other);
```

#### Parameters

`other` _GpuBigInteger_<br>
An object to compare with this instance.

#### Returns

_int_<br>
A value that indicates the relative order of the objects being compared.

## Dispose()

Disposes the underlying GPU buffer.

```csharp
public void Dispose();
```

## Equals(GpuBigInt)

Indicates whether the current object is equal to another object of the same type.

```csharp
public bool Equals(GpuBigInt other);
```

#### Parameters

`other` _GpuBigInteger_<br>
An object to compare with this object.

#### Returns

_bool_<br>
`true` if the current object is equal to the `other` parameter; otherwise, `false`.

## Equals(object?)

Indicates whether this instance and a specified object are equal.

```csharp
public override bool Equals(object? obj);
```

#### Parameters

`obj` _object?_<br>
The object to compare with the current instance.

#### Returns

_bool_<br>
`true` if `obj` and this instance are the same type and represent the same value; otherwise, `false`.

## GetHashCode()

Returns the hash code for this instance.

```csharp
public override int GetHashCode();
```

#### Returns

_int_<br>
A 32-bit signed integer that is the hash code for this instance.

## Operators

| Name | Description |
| --- | --- |
| `Addition(GpuBigInt, GpuBigInt)` | Adds two big integers. |
| `Subtraction(GpuBigInt, GpuBigInt)` | Subtracts one big integer from another. |
| `Multiply(GpuBigInt, GpuBigInt)` | Multiplies two big integers. |
| `Division(GpuBigInt, GpuBigInt)` | Divides one big integer by another. |
| `Equality(GpuBigInt, GpuBigInt)` | Indicates whether two `GpuBigInt` values are equal. |
| `Inequality(GpuBigInt, GpuBigInt)` | Indicates whether two `GpuBigInt` values are not equal. |
| `LessThan(GpuBigInt, GpuBigInt)` | Returns a value indicating whether a specified `GpuBigInt` is less than another specified `GpuBigInt`. |
| `GreaterThan(GpuBigInt, GpuBigInt)` | Returns a value indicating whether a specified `GpuBigInt` is greater than another specified `GpuBigInt`. |
| `LessThanOrEqual(GpuBigInt, GpuBigInt)` | Returns a value indicating whether a specified `GpuBigInt` is less than or equal to another specified `GpuBigInt`. |
| `GreaterThanOrEqual(GpuBigInt, GpuBigInt)` | Returns a value indicating whether a specified `GpuBigInt` is greater than or equal to another specified `GpuBigInt`. |

## Examples

### Lazy Evaluation (CPU)

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// Execution Context: Host (CPU)
try {
    using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
    
    using var uintA = GpuBigUInt.FromBigInteger(workspace, new BigInteger(100), 4);
    using var uintB = GpuBigUInt.FromBigInteger(workspace, new BigInteger(50), 4);

    // Wrap with a sign to represent signed integers
    var valA = new GpuBigInt(uintA, isNegative: false);
    var valB = new GpuBigInt(uintB, isNegative: false);

    // Operator overloads on CPU return AST expressions (Lazy Evaluation)
    var sumExpr = valA + valB;
    
    // Evaluate dynamically to a new buffer
    using var sumResult = (GpuBigInt)sumExpr.Node.Evaluate(workspace, null);
    
    Console.WriteLine($"Sum evaluated to={sumResult.Value.ToBigInteger(sumResult.IsNegative)}");
} catch (GpuOnlyException) {
    Console.WriteLine("Cannot run on CPU.");
}
```

### Immediate Evaluation (GPU)

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.GpuMath;
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
        // Instead, we use GpuBigIntMath for immediate zero-allocation evaluation.
        
        // Setup initial values (using generic fixed-size for simplicity)
        GpuUInt128 sigA = [150];
        GpuUInt128 sigB = [50];
        GpuUInt128 resultSig = [0];
        
        // Execute addition immediately
        GpuBigIntMath.Add(sigA, sigB, resultSig);
        
        Console.WriteLine($"Result={resultSig.Limbs[0]}");
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
