---
title: "GpuBigFloat Operators (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Reference for standard arithmetic operators on GpuBigFloat."
---

## GpuBigFloat Operators

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigFloat`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

`GpuBigFloat` provides standard C# operator overloads (`+`, `-`, `*`, `/`).
Just like `GpuBigInteger`, these operators rely on the **Lazy Evaluation** architecture. They return an Abstract Syntax Tree (AST) node (`GpuBigFloatExpr`) rather than executing immediately.

> [!WARNING]
> **Host-Side Allocations Occur Here**
> Although GSharp enforces a strict zero-allocation policy on the GPU device and avoids `cudaMalloc` by using `GpuContext` pooling, constructing this Abstract Syntax Tree (AST) via operator overloads _does_ allocate lightweight `class` nodes on the managed .NET heap. If you are operating in an extreme high-frequency loop on the CPU, building the expression tree itself will generate garbage.

## Overloads

| Operator                                | Description                      |
| --------------------------------------- | -------------------------------- |
| [Addition (+)](#addition--)             | Adds two numbers.                |
| [Subtraction (-)](#subtraction--)       | Subtracts a number from another. |
| [Multiplication (*)](#multiplication--) | Multiplies two numbers.          |
| [Division (/)](#division--)             | Divides a number by another.     |

## Addition (+)

Returns a lazy-evaluated expression tree node for addition.

```csharp
public static GpuBigFloatExpr operator +(GpuBigFloat left, GpuBigFloat right);
```

#### Parameters

`left` _GpuBigFloat_<br>
The first value.

`right` _GpuBigFloat_<br>
The second value.

#### Returns

_GpuBigFloatExpr_<br>
An expression tree node that computes the sum when evaluated.

## Subtraction (-)

Returns a lazy-evaluated expression tree node for subtraction.

```csharp
public static GpuBigFloatExpr operator -(GpuBigFloat left, GpuBigFloat right);
```

#### Parameters

`left` _GpuBigFloat_<br>
The number to subtract from.

`right` _GpuBigFloat_<br>
The number to subtract.

#### Returns

_GpuBigFloatExpr_<br>
An expression tree node that computes the difference when evaluated.

## Multiplication (*)

Returns a lazy-evaluated expression tree node for multiplication.

```csharp
public static GpuBigFloatExpr operator *(GpuBigFloat left, GpuBigFloat right);
```

#### Parameters

`left` _GpuBigFloat_<br>
The first value.

`right` _GpuBigFloat_<br>
The second value.

#### Returns

_GpuBigFloatExpr_<br>
An expression tree node that computes the product when evaluated.

## Division (/)

Returns a lazy-evaluated expression tree node for division.

```csharp
public static GpuBigFloatExpr operator /(GpuBigFloat dividend, GpuBigFloat divisor);
```

#### Parameters

`dividend` _GpuBigFloat_<br>
The number to be divided.

`divisor` _GpuBigFloat_<br>
The number to divide by.

#### Returns

_GpuBigFloatExpr_<br>
An expression tree node that computes the quotient when evaluated.

## Examples

### Fractal Deep Zooming (Mandelbrot)

```csharp
// Execution Context: Host (CPU)

using var context = new GpuContext(maxWords: 16); // 512-bit precision

var cReal = new GpuBigFloat(...);
var cImag = new GpuBigFloat(...);

var zReal = new GpuBigFloat(...);
var zImag = new GpuBigFloat(...);

// Build AST for Mandelbrot step: Z = Z^2 + C
// Real: Z_real^2 - Z_imag^2 + C_real
var zRealNewExpr = (zReal * zReal) - (zImag * zImag) + cReal;

// Evaluate dynamically
using var zRealNew = zRealNewExpr.Node.Evaluate(context, null);
```

## Remarks

Floating-point addition and subtraction require exponent alignment. The AST orchestrator automatically dispatches bit-shift kernels to align the mantissas before performing the addition or subtraction on the GPU.

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
- [GpuBigFloat Struct (G#)](GpuBigFloat.md)
