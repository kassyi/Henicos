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

`GpuBigFloat<TSize>` provides standard C# operator overloads (`+`, `-`). These evaluate **immediately** and do not construct an Abstract Syntax Tree (AST), making them suitable for zero-allocation execution inside GPU kernels.

Additionally, for host-side (CPU) code, the non-generic `GpuBigFloat` class (currently tracked in Issue #194) provides a **Lazy Evaluation** architecture similar to `GpuBigInt`, constructing an AST to optimize execution on the GPU.

> [!WARNING]
> **Host-Side Allocations Occur Here**
> Although GSharp enforces a strict zero-allocation policy on the GPU device, constructing the Abstract Syntax Tree (AST) via CPU operator overloads _does_ allocate lightweight `class` nodes on the managed .NET heap.

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

### Immediate Evaluation (GPU)

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath, Kassyi.GSharp.Core

```csharp
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

// Execution Context: Host (CPU)
Example.ComputeFloatOperators();

public class Example
{
    // Execution Context: G# Kernel (GPU)
    [GpuKernel]
    public static void ComputeFloatOperators()
    {
        GpuUInt128 sigA = [150];
        var floatA = new GpuFloat128(sigA, exponent: 0, isNegative: false);
        
        GpuUInt128 sigB = [50];
        var floatB = new GpuFloat128(sigB, exponent: 0, isNegative: false);
        
        // Evaluates immediately without AST on the GPU
        var sum = floatA + floatB;
        var diff = floatA - floatB;
        
        Console.WriteLine($"Sum Significand[0]={sum.Value.UFloat.Significand.Limbs[0]}");
        Console.WriteLine($"Diff Significand[0]={diff.Value.UFloat.Significand.Limbs[0]}");
    }
}
```

### Fractal Deep Zooming (Mandelbrot, CPU Lazy Evaluation)

> [!NOTE]
> CPU-side `GpuBigFloat` AST generation allows complex equations to be compiled into a single optimized GPU kernel at runtime.

```csharp
/* 
// TODO: This feature is currently tracked in Issue #194
// Execution Context: Host (CPU)

using var workspace = new GpuWorkspace(maxWords: 16); // 512-bit precision

var cReal = new GpuBigFloat(1.234);
var cImag = new GpuBigFloat(5.678);

var zReal = new GpuBigFloat(0.0);
var zImag = new GpuBigFloat(0.0);

// Build AST for Mandelbrot step: Z = Z^2 + C
// Real: Z_real^2 - Z_imag^2 + C_real
var zRealNewExpr = (zReal * zReal) - (zImag * zImag) + cReal;

// Evaluate dynamically
using var zRealNew = zRealNewExpr.Node.Evaluate(workspace, null);
*/
```

## Remarks

Floating-point addition and subtraction require exponent alignment. The AST orchestrator automatically dispatches bit-shift kernels to align the mantissas before performing the addition or subtraction on the GPU.

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
- [GpuBigFloat Struct (G#)](GpuBigFloat.md)
