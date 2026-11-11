---
title: "GpuBigInt and GpuBigUInt Operators (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Reference for standard arithmetic operators on GpuBigInt and GpuBigUInt."
---

## GpuBigInt and GpuBigUInt Operators

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

`GpuBigInt` (Signed) and `GpuBigUInt` (Unsigned) provide standard C# operator overloads (`+`, `-`, `*`, `/`, `%`); `GpuBigUInt` also provides the bitwise operators `&`, `|`, `^`, `~`, `<<` and `>>`.
Because of the **Lazy Evaluation** architecture, these operators **do not execute immediately**. Instead, they return an Abstract Syntax Tree (AST) node (`GpuBigIntExpr` or `GpuBigUIntExpr`) that represents the operation.

> [!WARNING]
> **Host-Side Allocations Occur Here**
> Although GSharp enforces a strict zero-allocation policy on the GPU device and avoids `cudaMalloc` by using `GpuWorkspace` pooling, constructing this Abstract Syntax Tree (AST) via operator overloads _does_ allocate lightweight `class` nodes on the managed .NET heap. If you are operating in an extreme high-frequency loop on the CPU, building the expression tree itself will generate garbage.

> [!NOTE]
> **Behavioral Differences**
>
> - **`GpuBigInt` (Signed Wrapper)**: When the Host CPU encounters an addition like `A + (-B)`, it evaluates the sign bits natively and transparently transforms the AST node into subtraction `A - B` before dispatching to the GPU. For details, see [The Architecture: Signed vs. Unsigned Separation](../../../04_guides/07_stdlib_math_and_lazy_evaluation.md#3-the-architecture-signed-vs-unsigned-separation).
> - **`GpuBigUInt` (Unsigned Wrapper)**: No sign orchestration occurs. The AST nodes map directly to the corresponding unsigned PTX hardware math kernels.

## Overloads

| Operator | Description |
| --- | --- |
| [Addition (+)](#addition--) | Adds two numbers. |
| [Subtraction (-)](#subtraction--) | Subtracts a number from another. |
| [Multiplication (*)](#multiplication--) | Multiplies two numbers. |
| [Division (/)](#division--) | Divides a number by another. |
| [Modulus (%)](#modulus--) | Computes the remainder of a division. |
| [Bitwise and shift operators](#bitwise-and-shift-operators) | `&`, `\|`, `^`, `~`, `<<`, `>>` on `GpuBigUInt`, and `~` on a signed expression. |

The same operators are defined on the expression types `GpuBigIntExpr` and `GpuBigUIntExpr`, so an expression can be combined further before it is evaluated. A `GpuBigInt` or `GpuBigUInt` converts to its expression type implicitly.

## Addition (+)

Returns a lazy-evaluated expression tree node for addition.

```csharp
public static GpuBigIntExpr operator +(GpuBigInt left, GpuBigInt right);
public static GpuBigUIntExpr operator +(GpuBigUInt left, GpuBigUInt right);
```

#### Parameters

`left` _GpuBigInt_ | _GpuBigUInt_<br>
The first value.

`right` _GpuBigInt_ | _GpuBigUInt_<br>
The second value.

#### Returns

_GpuBigIntExpr_ | _GpuBigUIntExpr_<br>
An expression tree node that computes the sum when evaluated.

## Subtraction (-)

Returns a lazy-evaluated expression tree node for subtraction.

```csharp
public static GpuBigIntExpr operator -(GpuBigInt left, GpuBigInt right);
public static GpuBigUIntExpr operator -(GpuBigUInt left, GpuBigUInt right);
```

#### Parameters

`left` _GpuBigInt_ | _GpuBigUInt_<br>
The number to subtract from.

`right` _GpuBigInt_ | _GpuBigUInt_<br>
The number to subtract.

#### Returns

_GpuBigIntExpr_ | _GpuBigUIntExpr_<br>
An expression tree node that computes the difference when evaluated.

## Multiplication (*)

Returns a lazy-evaluated expression tree node for multiplication.

```csharp
public static GpuBigIntExpr operator *(GpuBigInt left, GpuBigInt right);
public static GpuBigUIntExpr operator *(GpuBigUInt left, GpuBigUInt right);
```

#### Parameters

`left` _GpuBigInt_ | _GpuBigUInt_<br>
The first value.

`right` _GpuBigInt_ | _GpuBigUInt_<br>
The second value.

#### Returns

_GpuBigIntExpr_ | _GpuBigUIntExpr_<br>
An expression tree node that computes the product when evaluated.
_(Note: Automatically upgrades to NTT for massive operands)_.

## Division (/)

Returns a lazy-evaluated expression tree node for division.

```csharp
public static GpuBigIntExpr operator /(GpuBigInt dividend, GpuBigInt divisor);
public static GpuBigUIntExpr operator /(GpuBigUInt dividend, GpuBigUInt divisor);
```

#### Parameters

`dividend` _GpuBigInt_ | _GpuBigUInt_<br>
The number to be divided.

`divisor` _GpuBigInt_ | _GpuBigUInt_<br>
The number to divide by.

#### Returns

_GpuBigIntExpr_ | _GpuBigUIntExpr_<br>
An expression tree node that computes the quotient when evaluated.

## Modulus (%)

Returns a lazy-evaluated expression tree node for the remainder of a division.

```csharp
public static GpuBigIntExpr operator %(GpuBigInt left, GpuBigInt right);
public static GpuBigUIntExpr operator %(GpuBigUInt left, GpuBigUInt right);
```

#### Parameters

`left` _GpuBigInt_ | _GpuBigUInt_<br>
The dividend.

`right` _GpuBigInt_ | _GpuBigUInt_<br>
The divisor.

#### Returns

_GpuBigIntExpr_ | _GpuBigUIntExpr_<br>
An expression tree node that computes the remainder when evaluated.

## Bitwise and shift operators

`GpuBigUInt` (generated by `[GenerateGpuNumberOperators]`) and `GpuBigUIntExpr` define the bitwise operators on unsigned values. `GpuBigIntExpr` defines `~` (the value `-a - 1`, as for a C# signed integer) and unary `-`.

```csharp
public static GpuBigUIntExpr operator &(GpuBigUInt left, GpuBigUInt right);
public static GpuBigUIntExpr operator |(GpuBigUInt left, GpuBigUInt right);
public static GpuBigUIntExpr operator ^(GpuBigUInt left, GpuBigUInt right);
public static GpuBigUIntExpr operator ~(GpuBigUInt leftOperand);
public static GpuBigUIntExpr operator <<(GpuBigUInt left, int shift);
public static GpuBigUIntExpr operator >>(GpuBigUInt left, int shift);
public static GpuBigIntExpr operator ~(GpuBigIntExpr left);
```

The width of each result follows the bounds in [Result Widths and Domain Errors](../00_index.md#result-widths-and-domain-errors): `&` keeps the narrower operand, `|` and `^` the wider one, unsigned `~` inverts every bit of the words of its operand, and a negative shift count shifts the other way.

## Examples

### Chained Arithmetic (Lazy Evaluation)

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// Execution Context: Host (CPU)
using var workspace = new GpuWorkspace(maxWords: 1024);

using var a = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, new BigInteger(100)), isNegative: false);
using var b = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, new BigInteger(30)), isNegative: true);
using var c = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, new BigInteger(7)), isNegative: false);

// Build the AST: No GPU execution happens here!
GpuBigIntExpr expr = (a + b) * c;

// Evaluate the entire tree in one shot
using GpuBigInt result = expr.Evaluate(workspace);

Console.WriteLine(result.Value.ToBigInteger(result.IsNegative)); // 490
```

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
- [GpuBigInt Struct (G#)](GpuBigInt.md)

### Guides

- [4. Practical Guides and Best Practices](../../../04_guides/00_index.md)
- [4. BigNum Expression Trees Guide](../../../04_guides/04_bignum_expression_trees_guide.md)
