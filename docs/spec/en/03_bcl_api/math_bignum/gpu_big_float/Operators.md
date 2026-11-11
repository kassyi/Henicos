---
title: "GpuBigFloat Operators (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Reference for standard arithmetic operators on GpuBigFloat."
---

## GpuBigFloat Operators

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

[`GpuBigFloat`](GpuBigFloat.md) and [`GpuBigUFloat`](GpuBigUFloat.md) are `readonly struct` types that overload the C# arithmetic operators `+`, `-`, `*` and `/`. Each operator returns a `BinaryExpr<T, TOp>` value (`TOp` is `AddOp`, `SubOp`, `MulOp` or `DivOp`, see [FloatExpressions](../gpu_big_int_and_uint/expressions/FloatExpressions.md)) that holds the two operands. Assigning it to `GpuBigFloat` or `GpuBigUFloat` evaluates it through an implicit conversion, which runs the operation on the GPU of the operands' `GpuWorkspace` (in CPU fallback mode, on the host) with a `GpuBigFloatExpressionVisitor`.

`BinaryExpr<T, TOp>` and the operator tags are value types, so building an expression allocates nothing on the managed heap. The types are host-side: they own device buffers of a `GpuWorkspace` and cannot be used inside a `[GpuKernel]`.

## Overloads

| Operator | Description |
| --- | --- |
| [Addition (+)](#addition--) | Adds two numbers. |
| [Subtraction (-)](#subtraction--) | Subtracts a number from another. |
| [Multiplication (*)](#multiplication--) | Multiplies two numbers. |
| [Division (/)](#division--) | Divides a number by another. |

The same four operators exist for `GpuBigUFloat`, with `GpuBigUFloat` in place of `GpuBigFloat`.

## Addition (+)

Returns a deferred addition of two values.

```csharp
public static BinaryExpr<GpuBigFloat, AddOp> operator +(GpuBigFloat left, GpuBigFloat right);
```

#### Parameters

`left` _GpuBigFloat_<br>
The first value.

`right` _GpuBigFloat_<br>
The second value.

#### Returns

_BinaryExpr&lt;GpuBigFloat, AddOp&gt;_<br>
An expression that computes the sum when it is converted to `GpuBigFloat`.

## Subtraction (-)

Returns a deferred subtraction of two values.

```csharp
public static BinaryExpr<GpuBigFloat, SubOp> operator -(GpuBigFloat left, GpuBigFloat right);
```

#### Parameters

`left` _GpuBigFloat_<br>
The number to subtract from.

`right` _GpuBigFloat_<br>
The number to subtract.

#### Returns

_BinaryExpr&lt;GpuBigFloat, SubOp&gt;_<br>
An expression that computes the difference when it is converted to `GpuBigFloat`.

#### Remarks

For the unsigned `GpuBigUFloat`, evaluating the difference throws `OverflowException` when `right` is greater than `left`, as with a `checked` `uint` subtraction. The same holds for `Subtract`, `IGpuBigNumber<GpuBigUFloat>.operator -` and the element-wise `SelectSubtract` / `ZipSubtract` of a `GpuBigUFloat` sequence.

## Multiplication (*)

Returns a deferred multiplication of two values.

```csharp
public static BinaryExpr<GpuBigFloat, MulOp> operator *(GpuBigFloat left, GpuBigFloat right);
```

#### Parameters

`left` _GpuBigFloat_<br>
The first value.

`right` _GpuBigFloat_<br>
The second value.

#### Returns

_BinaryExpr&lt;GpuBigFloat, MulOp&gt;_<br>
An expression that computes the product when it is converted to `GpuBigFloat`: the exact product rounded toward zero to the width of the wider operand, like `IGpuBigFloatArithmetic.Multiply`. Evaluating it throws `OverflowException` when the exponent of the product does not fit in `int`.

## Division (/)

Returns a deferred division of two values.

```csharp
public static BinaryExpr<GpuBigFloat, DivOp> operator /(GpuBigFloat dividend, GpuBigFloat divisor);
```

#### Parameters

`dividend` _GpuBigFloat_<br>
The number to be divided.

`divisor` _GpuBigFloat_<br>
The number to divide by.

#### Returns

_BinaryExpr&lt;GpuBigFloat, DivOp&gt;_<br>
An expression that computes the quotient when it is converted to `GpuBigFloat`.

## Examples

### Arithmetic on the GPU from the host

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

// Execution Context: Host (CPU); the operations run on the GPU
using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);

// 150 * 2^0 and 50 * 2^0
using var a = new GpuBigFloat(new GpuBigUInt(workspace, [150u, 0u]), exponent: 0, isNegative: false);
using var b = new GpuBigFloat(new GpuBigUInt(workspace, [50u, 0u]), exponent: 0, isNegative: false);

// Each assignment converts the BinaryExpr and evaluates it
using GpuBigFloat sum = a + b;
using GpuBigFloat difference = a - b;
using GpuBigFloat product = a * b;
using GpuBigFloat quotient = a / b;

Console.WriteLine($"sum: {sum.GetBitLength()} bits, exponent {sum.Exponent}");
```

## Remarks

Floating-point addition and subtraction require exponent alignment. The visitor dispatches the alignment kernels before the addition or subtraction on the GPU.

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
- [GpuBigFloat Struct (G#)](GpuBigFloat.md)
