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

`GpuBigInt` (Signed) and `GpuBigUInt` (Unsigned) provide standard C# operator overloads (`+`, `-`, `*`, `/`, `%`).
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

## Addition (+)

Returns a lazy-evaluated expression tree node for addition.

```csharp
public static GpuBigIntExpr operator +(GpuBigInt left, GpuBigInt right);
public static GpuBigUIntExpr operator +(GpuBigUInt left, GpuBigUInt right);
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
public static GpuBigIntExpr operator -(GpuBigInt left, GpuBigInt right);
public static GpuBigUIntExpr operator -(GpuBigUInt left, GpuBigUInt right);
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
public static GpuBigIntExpr operator *(GpuBigInt left, GpuBigInt right);
public static GpuBigUIntExpr operator *(GpuBigUInt left, GpuBigUInt right);
```

#### Parameters

`left` _GpuBigInteger_ | _GpuUIntBigInteger_<br>
The first value.

`right` _GpuBigInteger_ | _GpuUIntBigInteger_<br>
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

`dividend` _GpuBigInteger_ | _GpuUIntBigInteger_<br>
The number to be divided.

`divisor` _GpuBigInteger_ | _GpuUIntBigInteger_<br>
The number to divide by.

#### Returns

_GpuBigIntExpr_ | _GpuBigUIntExpr_<br>
An expression tree node that computes the quotient when evaluated.

## Examples

### Chained Arithmetic (Lazy Evaluation)

```csharp
// Execution Context: Host (CPU)

using var workspace = new GpuWorkspace(maxWords: 1024);

var a = new GpuBigInt(...);
var b = new GpuBigInt(...);
var c = new GpuBigInt(...);

// Build the AST: No GPU execution happens here!
var expr = (a + b) * c;

// Evaluate the entire tree in one shot
using var result = expr.Node.Evaluate(workspace, null);

Console.WriteLine(result.ToBigInteger());
```

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
- [GpuBigInt Struct (G#)](GpuBigInt.md)

### Guides

- [4. Practical Guides and Best Practices](../../../04_guides/00_index.md)
- [4. BigNum Expression Trees Guide](../../../04_guides/04_bignum_expression_trees_guide.md)
