---
title: "GpuBigIntMatrix2x2 Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Reference for 2x2 transition matrix operations for multi-precision integers."
---

## GpuBigIntMatrix2x2 Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Represents a $2 \times 2$ transition matrix of arbitrary-precision unsigned integers (`GpuBigUInt`) allocated in GPU device memory.

```csharp
public class GpuBigIntMatrix2x2 : IDisposable
```

This matrix structure is used to accelerate the Half-GCD (HGCD), Lehmer's GCD, and Extended Euclidean Algorithm (EEA) pipelines on NVIDIA GPUs by compounding $2 \times 2$ quotient transition matrices into cumulative matrix products.

## Constructors

| Name | Description |
| --- | --- |
| [GpuBigIntMatrix2x2(GpuWorkspace, Int32, Boolean)](#gpubigintmatrix2x2gpuworkspace-int32-boolean) | Initializes a new instance of the `GpuBigIntMatrix2x2` class with uninitialized memory for each matrix element. |

## GpuBigIntMatrix2x2(GpuWorkspace, Int32, Boolean)

Initializes a new instance of the `GpuBigIntMatrix2x2` class with uninitialized memory for each matrix element.

```csharp
public GpuBigIntMatrix2x2(GpuWorkspace workspace, int numWords, bool isOddStep);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The parent GPU execution context. Cannot be `null`.

`numWords` _Int32_<br>
The number of 32-bit limbs (words) to allocate for each matrix element. Must be greater than zero.

`isOddStep` _Boolean_<br>
A value indicating whether this transition matrix represents an odd number of reduction steps.

## Properties

| Name | Description |
| --- | --- |
| `IsOddStep` | Gets a value indicating whether this matrix represents an odd number of reduction steps in the Euclidean algorithm. |
| `M00` | Gets the `GpuBigUInt` element located at row 0, column 0 ($M_{00}$) in GPU device memory. |
| `M01` | Gets the `GpuBigUInt` element located at row 0, column 1 ($M_{01}$) in GPU device memory. |
| `M10` | Gets the `GpuBigUInt` element located at row 1, column 0 ($M_{10}$) in GPU device memory. |
| `M11` | Gets the `GpuBigUInt` element located at row 1, column 1 ($M_{11}$) in GPU device memory. |
| `Workspace` | Gets the parent `GpuWorkspace` managing the GPU device memory and execution provider for this matrix. |

## Methods

| Name | Description |
| --- | --- |
| [Apply(GpuBigUInt, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#applygpubiguint-gpubiguint-gpubiguint-gpubiguint) | Applies this 2x2 reduction matrix to a pair of GPU integers. |
| [Dispose()](#dispose) | Disposes all four constituent instances and releases GPU memory. |
| [Identity(GpuWorkspace, Int32)](#identitygpuworkspace-int32) | Creates and initializes a 2x2 identity matrix in GPU device memory. |
| [Multiply(GpuBigIntMatrix2x2, GpuBigIntMatrix2x2)](#multiplygpubigintmatrix2x2-gpubigintmatrix2x2) | Multiplies two 2x2 GPU BigInteger matrices and returns their product. |

## Apply(GpuBigUInt, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Applies this $2 \times 2$ reduction matrix to a pair of GPU integers (`a` and `b`), storing the reduced outputs in `outA` and `outB`.

```csharp
public void Apply(GpuBigUInt a, GpuBigUInt b, GpuBigUInt outA, GpuBigUInt outB);
```

## Dispose()

Disposes all four constituent `GpuBigUInt` instances (`M00`, `M01`, `M10`, and `M11`) and releases their GPU device memory allocations.

```csharp
public void Dispose();
```

## Identity(GpuWorkspace, Int32)

Creates and initializes a $2 \times 2$ identity matrix in GPU device memory.

```csharp
public static GpuBigIntMatrix2x2 Identity(GpuWorkspace workspace, int numWords);
```

## Multiply(GpuBigIntMatrix2x2, GpuBigIntMatrix2x2)

Multiplies two $2 \times 2$ GPU BigInteger matrices (`v` and `u`) and returns their product ($R = V \times U$).

```csharp
public static GpuBigIntMatrix2x2 Multiply(GpuBigIntMatrix2x2 v, GpuBigIntMatrix2x2 u);
```

## Examples

### Multiplying and Applying a 2x2 Transition Matrix

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

// Execution Context: Host (CPU)
using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

using var mat1 = GpuBigIntMatrix2x2.Identity(workspace, 4);
using var mat2 = GpuBigIntMatrix2x2.Identity(workspace, 4);
using var matResult = GpuBigIntMatrix2x2.Multiply(mat1, mat2);

using var a = GpuBigUInt.FromBigInteger(workspace, new BigInteger(100), minWords: 4);
using var b = GpuBigUInt.FromBigInteger(workspace, new BigInteger(20), minWords: 4);
using var outA = new GpuBigUInt(workspace, 4);
using var outB = new GpuBigUInt(workspace, 4);

matResult.Apply(a, b, outA, outB);
Console.WriteLine($"outA = {outA.ToBigInteger(false)}");
Console.WriteLine($"outB = {outB.ToBigInteger(false)}");
```

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../../00_index.md)
- [GpuBigIntMatrix2x2 Reference](../GpuBigIntMatrix2x2.md)
- [GpuBigInt and GpuBigUInt Operators (G#)](../Operators.md)
