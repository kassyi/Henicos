---
title: "GpuBigIntMatrix2x2 Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Represents a 2x2 transition matrix of arbitrary-precision integers allocated in GPU device memory."
---

## GpuBigIntMatrix2x2 Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Represents a 2x2 transition matrix of arbitrary-precision integers (`GpuUIntBigInteger`) allocated in GPU device memory.

```csharp
public class GpuBigIntMatrix2x2 : IDisposable
```

## Remarks

This matrix structure is used to accelerate the Half-GCD (HGCD), Lehmer's GCD, and Extended Euclidean Algorithm (EEA) pipelines on NVIDIA GPUs by compounding 2x2 quotient transition matrices into cumulative matrix products.

**Thread Safety:** Static methods are thread-safe. Instance methods and properties are not thread-safe and must not be accessed concurrently across multiple threads without external synchronization.

**Memory Management:** Implements `IDisposable` to ensure deterministic deallocation of GPU global memory allocated for all four matrix elements (`M00`, `M01`, `M10`, and `M11`).

**Execution Target:** Host-only (CPU) control API that dispatches arithmetic operations and memory transfers to the GPU.

## Constructors

| Name | Description |
| --- | --- |
| [GpuBigIntMatrix2x2(GpuContext, int, bool)](#gpubigintmatrix2x2gpucontext-int-bool) | Initializes a new instance of the `GpuBigIntMatrix2x2` class with uninitialized memory for each matrix element. |

## GpuBigIntMatrix2x2(GpuContext, int, bool)

Initializes a new instance of the `GpuBigIntMatrix2x2` class with uninitialized memory for each matrix element.

```csharp
public GpuBigIntMatrix2x2(GpuContext context, int numWords, bool isOddStep);
```

#### Parameters

`context` _GpuContext_<br>
The parent GPU execution context. Cannot be `null`.

`numWords` _int_<br>
The number of 32-bit limbs (words) to allocate for each matrix element. Must be greater than zero.

`isOddStep` _bool_<br>
A value indicating whether this transition matrix represents an odd number of reduction steps.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `context` is `null`.

`ArgumentOutOfRangeException`<br>
Thrown when `numWords` is less than or equal to zero.

`OutOfMemoryException`<br>
Thrown when GPU global memory allocation fails for any of the matrix elements.

## Properties

| Name | Description |
| --- | --- |
| [Context](#context) | Gets the parent `GpuContext` managing the GPU device memory and execution provider for this matrix. |
| [IsOddStep](#isoddstep) | Gets a value indicating whether this matrix represents an odd number of reduction steps in the Euclidean algorithm. |
| [M00](#m00) | Gets the `GpuUIntBigInteger` element located at row 0, column 0 in GPU device memory. |
| [M01](#m01) | Gets the `GpuUIntBigInteger` element located at row 0, column 1 in GPU device memory. |
| [M10](#m10) | Gets the `GpuUIntBigInteger` element located at row 1, column 0 in GPU device memory. |
| [M11](#m11) | Gets the `GpuUIntBigInteger` element located at row 1, column 1 in GPU device memory. |

## Context

Gets the parent `GpuContext` managing the GPU device memory and execution provider for this matrix.

```csharp
public GpuContext Context { get; }
```

## IsOddStep

Gets a value indicating whether this matrix represents an odd number of reduction steps in the Euclidean algorithm.

```csharp
public bool IsOddStep { get; }
```

## M00

Gets the `GpuUIntBigInteger` element located at row 0, column 0 ($M_{00}$) in GPU device memory.

```csharp
public GpuUIntBigInteger M00 { get; }
```

## M01

Gets the `GpuUIntBigInteger` element located at row 0, column 1 ($M_{01}$) in GPU device memory.

```csharp
public GpuUIntBigInteger M01 { get; }
```

## M10

Gets the `GpuUIntBigInteger` element located at row 1, column 0 ($M_{10}$) in GPU device memory.

```csharp
public GpuUIntBigInteger M10 { get; }
```

## M11

Gets the `GpuUIntBigInteger` element located at row 1, column 1 ($M_{11}$) in GPU device memory.

```csharp
public GpuUIntBigInteger M11 { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [Identity(GpuContext, int)](#identitygpucontext-int) | Creates and initializes a 2x2 identity matrix in GPU device memory. |
| [Multiply(GpuBigIntMatrix2x2, GpuBigIntMatrix2x2)](#multiplygpubigintmatrix2x2-gpubigintmatrix2x2) | Multiplies two 2x2 GPU BigInteger matrices and returns their product. |
| [Apply(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#applygpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Applies this 2x2 reduction matrix to a pair of GPU integers. |
| [Dispose()](#dispose) | Disposes all four constituent instances and releases GPU memory. |

## Identity(GpuContext, int)

Creates and initializes a 2x2 identity matrix ($I = \begin{pmatrix} 1 & 0 \\ 0 & 1 \end{pmatrix}$) in GPU device memory.

```csharp
public static GpuBigIntMatrix2x2 Identity(GpuContext context, int numWords);
```

#### Parameters

`context` _GpuContext_<br>
The parent GPU execution context. Cannot be `null`.

`numWords` _int_<br>
The number of 32-bit limbs (words) to allocate for each matrix element. Must be greater than zero.

#### Returns

_GpuBigIntMatrix2x2_<br>
A new `GpuBigIntMatrix2x2` instance representing the identity matrix.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `context` is `null`.

`ArgumentOutOfRangeException`<br>
Thrown when `numWords` is less than or equal to zero.

`OutOfMemoryException`<br>
Thrown when GPU global memory allocation fails.

## Multiply(GpuBigIntMatrix2x2, GpuBigIntMatrix2x2)

Multiplies two 2x2 GPU BigInteger matrices ($v$ and $u$) and returns their product ($R = V \times U$).

```csharp
public static GpuBigIntMatrix2x2 Multiply(GpuBigIntMatrix2x2 v, GpuBigIntMatrix2x2 u);
```

#### Parameters

`v` _GpuBigIntMatrix2x2_<br>
The left-hand operand matrix $V$. Cannot be `null`.

`u` _GpuBigIntMatrix2x2_<br>
The right-hand operand matrix $U$. Cannot be `null`.

#### Returns

_GpuBigIntMatrix2x2_<br>
A new `GpuBigIntMatrix2x2` instance containing the product of `v` and `u`.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `v` or `u` is `null`.

`ArgumentException`<br>
Thrown when `v` and `u` belong to different `GpuContext` instances.

## Apply(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Applies this 2x2 reduction matrix to a pair of GPU integers (`a` and `b`), storing the reduced outputs in `outA` and `outB`.

```csharp
public void Apply(GpuUIntBigInteger a, GpuUIntBigInteger b, GpuUIntBigInteger outA, GpuUIntBigInteger outB);
```

#### Parameters

`a` _GpuUIntBigInteger_<br>
The first operand `GpuUIntBigInteger`. Cannot be `null`.

`b` _GpuUIntBigInteger_<br>
The second operand `GpuUIntBigInteger`. Cannot be `null`.

`outA` _GpuUIntBigInteger_<br>
The destination to receive the transformed first element. Cannot be `null`.

`outB` _GpuUIntBigInteger_<br>
The destination to receive the transformed second element. Cannot be `null`.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `a`, `b`, `outA`, or `outB` is `null`.

`ArgumentException`<br>
Thrown when any argument does not belong to the same `GpuContext` as this matrix.

## Dispose()

Disposes all four constituent `GpuUIntBigInteger` instances and releases their GPU device memory allocations.

```csharp
public void Dispose();
```

## Examples

### Using Matrix in HGCD Step

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Runtime, Kassyi.GSharp.Stdlib.Math

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.BigInt;

// Execution Context: Host (CPU)
using var context = new GpuContext(1024, GpuExecutionEngine.GSharpAot);

using var mat1 = GpuBigIntMatrix2x2.Identity(context, 4);
using var mat2 = GpuBigIntMatrix2x2.Identity(context, 4);
using var matResult = GpuBigIntMatrix2x2.Multiply(mat1, mat2);

using var a = GpuUIntBigInteger.FromBigInteger(context, new BigInteger(100), minWords: 4);
using var b = GpuUIntBigInteger.FromBigInteger(context, new BigInteger(20), minWords: 4);
using var outA = new GpuUIntBigInteger(context, 4);
using var outB = new GpuUIntBigInteger(context, 4);

matResult.Apply(a, b, outA, outB);
Console.WriteLine($"outA = {outA.ToBigInteger(false)}");
Console.WriteLine($"outB = {outB.ToBigInteger(false)}");
```

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
- [GpuUIntBigInteger](GpuUIntBigInteger.md)
