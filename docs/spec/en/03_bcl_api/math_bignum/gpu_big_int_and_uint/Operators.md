---
title: "GpuBigInteger and GpuUIntBigInteger Operators (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Reference for standard arithmetic operators on GpuBigInteger and GpuUIntBigInteger."
---

## GpuBigInteger and GpuUIntBigInteger Operators

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

`GpuBigInteger` (Signed) and `GpuUIntBigInteger` (Unsigned) provide standard C# operator overloads (`+`, `-`, `*`, `/`, `%`). 
Because of the **Lazy Evaluation** architecture, these operators **do not execute immediately**. Instead, they return an Abstract Syntax Tree (AST) node (`GpuBigIntExpr` or `GpuBigUIntExpr`) that represents the operation.

> [!NOTE]
> **Behavioral Differences**
> - **`GpuBigInteger` (Signed Wrapper)**: When the Host CPU encounters an addition like `A + (-B)`, it evaluates the sign bits natively and transparently transforms the AST node into subtraction `A - B` before dispatching to the GPU. For details, see [The Architecture: Signed vs. Unsigned Separation](../../../04_guides/07_stdlib_math_and_lazy_evaluation.md#3-the-architecture-signed-vs-unsigned-separation).
> - **`GpuUIntBigInteger` (Unsigned Wrapper)**: No sign orchestration occurs. The AST nodes map directly to the corresponding unsigned PTX hardware math kernels.

## Overloads

| Operator | Description |
| --- | --- |
| [Addition (+)](#addition--) | Adds two numbers. |
| [Subtraction (-)](#subtraction--) | Subtracts a number from another. |
| [Multiplication (*)](#multiplication--) | Multiplies two numbers. |
| [Division (/)](#division--) | Divides a number by another. |
| [Modulus (%)](#modulus--) | Computes the remainder of a division. |

## Addition (+)

Returns a lazy-evaluated expression tree node for addition.

```csharp
public static GpuBigIntExpr operator +(GpuBigInteger left, GpuBigInteger right);
public static GpuBigUIntExpr operator +(GpuUIntBigInteger left, GpuUIntBigInteger right);
```

#### Parameters
`left` _GpuBigInteger_ | _GpuUIntBigInteger_<br>
The first value.

`right` _GpuBigInteger_ | _GpuUIntBigInteger_<br>
The second value.

#### Returns
_GpuBigIntExpr_ | _GpuBigUIntExpr_<br>
An expression tree node that computes the sum when evaluated.

## Subtraction (-)

Returns a lazy-evaluated expression tree node for subtraction.

```csharp
public static GpuBigIntExpr operator -(GpuBigInteger left, GpuBigInteger right);
public static GpuBigUIntExpr operator -(GpuUIntBigInteger left, GpuUIntBigInteger right);
```

#### Parameters
`left` _GpuBigInteger_ | _GpuUIntBigInteger_<br>
The number to subtract from.

`right` _GpuBigInteger_ | _GpuUIntBigInteger_<br>
The number to subtract.

#### Returns
_GpuBigIntExpr_ | _GpuBigUIntExpr_<br>
An expression tree node that computes the difference when evaluated.

## Multiplication (*)

Returns a lazy-evaluated expression tree node for multiplication.

```csharp
public static GpuBigIntExpr operator *(GpuBigInteger left, GpuBigInteger right);
public static GpuBigUIntExpr operator *(GpuUIntBigInteger left, GpuUIntBigInteger right);
```

#### Parameters
`left` _GpuBigInteger_ | _GpuUIntBigInteger_<br>
The first value.

`right` _GpuBigInteger_ | _GpuUIntBigInteger_<br>
The second value.

#### Returns
_GpuBigIntExpr_ | _GpuBigUIntExpr_<br>
An expression tree node that computes the product when evaluated. 
*(Note: Automatically upgrades to NTT for massive operands)*.

## Division (/)

Returns a lazy-evaluated expression tree node for division.

```csharp
public static GpuBigIntExpr operator /(GpuBigInteger dividend, GpuBigInteger divisor);
public static GpuBigUIntExpr operator /(GpuUIntBigInteger dividend, GpuUIntBigInteger divisor);
```

#### Parameters
`dividend` _GpuBigInteger_ | _GpuUIntBigInteger_<br>
The number to be divided.

`divisor` _GpuBigInteger_ | _GpuUIntBigInteger_<br>
The number to divide by.

#### Returns
_GpuBigIntExpr_ | _GpuBigUIntExpr_<br>
An expression tree node that computes the quotient when evaluated.

## Modulus (%)

Returns a lazy-evaluated expression tree node for modulo operation.

```csharp
public static GpuBigIntExpr operator %(GpuBigInteger dividend, GpuBigInteger divisor);
public static GpuBigUIntExpr operator %(GpuUIntBigInteger dividend, GpuUIntBigInteger divisor);
```

#### Parameters
`dividend` _GpuBigInteger_ | _GpuUIntBigInteger_<br>
The number to be divided.

`divisor` _GpuBigInteger_ | _GpuUIntBigInteger_<br>
The number to divide by.

#### Returns
_GpuBigIntExpr_ | _GpuBigUIntExpr_<br>
An expression tree node that computes the remainder when evaluated.

## Examples

### Chained Arithmetic (Lazy Evaluation)

```csharp
// Execution Context: Host (CPU)

using var context = new GpuContext(maxWords: 1024);

var a = new GpuBigInteger(...);
var b = new GpuBigInteger(...);
var c = new GpuBigInteger(...);

// Build the AST: No GPU execution happens here!
var expr = (a + b) * c;

// Evaluate the entire tree in one shot
using var result = expr.Node.Evaluate(context, null);

Console.WriteLine(result.ToBigInteger());
```

## See also

### Related Integer Types
- [GpuBigInteger](GpuBigInteger.md)
- [GpuUIntBigInteger](GpuUIntBigInteger.md)
- [GpuBigInteger and GpuUIntBigInteger Overview](00_index.md)
- [BigFloat Operators](../gpu_big_float/Operators.md)

### Architecture & Internals
- [Arbitrary-Precision Math Overview](../00_index.md)
- [4. Bignum Transpiler Internals](../../../02_architecture/04_bignum_internals.md)
- [Gpu BCL API Overview](../../00_index.md)

### Guides
- [Bignum Expression Trees Guide](../../../04_guides/04_bignum_expression_trees_guide.md)
- [Stdlib Math and Lazy Evaluation Guide](../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
