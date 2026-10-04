---
title: "GpuBigIntMatrix2x2 Class (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Reference for 2x2 Matrix operations for multi-precision integers."
---

## GpuBigIntMatrix2x2 Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Provides specialized orchestration for a $2 \times 2$ matrix of `GpuBigInt` values.

The primary use case for this structure is computing linear recurrence relations in $O(\log N)$ time using **Matrix Exponentiation**. For example, calculating the $N$-th Fibonacci number for a massive $N$ (e.g., $N = 10,000,000$) can be done exponentially faster on the GPU by computing:

$$
\begin{pmatrix} F_{N+1} \\ F_N \end{pmatrix} = \begin{pmatrix} 1 & 1 \\ 1 & 0 \end{pmatrix}^N \begin{pmatrix} F_1 \\ F_0 \end{pmatrix}
$$

## Constructors

| Name                                                                                                                                                          | Description                                           |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------- |
| [GpuBigIntMatrix2x2(GpuBigInt, GpuBigInt, GpuBigInt, GpuBigInt)](#gpubigintmatrix2x2-gpubiginteger-gpubiginteger-gpubiginteger-gpubiginteger) | Initializes a new matrix with the specified elements. |

## GpuBigIntMatrix2x2(GpuBigInt, GpuBigInt, GpuBigInt, GpuBigInt)

Initializes a new instance of the 2x2 matrix.

```csharp
public GpuBigIntMatrix2x2(GpuWorkspace workspace, int wordCount, bool oddStep);
```

#### Parameters

`m00` _GpuBigInteger_<br>
Row 0, Column 0 element.

`m01` _GpuBigInteger_<br>
Row 0, Column 1 element.

`m10` _GpuBigInteger_<br>
Row 1, Column 0 element.

`m11` _GpuBigInteger_<br>
Row 1, Column 1 element.

## Methods

| Name                                                                                                | Description              |
| --------------------------------------------------------------------------------------------------- | ------------------------ |
| [Multiply(GpuBigIntMatrix2x2, GpuBigIntMatrix2x2)](#multiply-gpubigintmatrix2x2-gpubigintmatrix2x2) | Multiplies two matrices. |

## Multiply(GpuBigIntMatrix2x2, GpuBigIntMatrix2x2)

Returns a lazy-evaluated struct containing four expression trees corresponding to the matrix multiplication logic.

```csharp
public static GpuBigIntMatrix2x2 Multiply(GpuBigIntMatrix2x2 left, GpuBigIntMatrix2x2 right);
```

#### Parameters

`left` _GpuBigIntMatrix2x2_<br>
The left matrix.

`right` _GpuBigIntMatrix2x2_<br>
The right matrix.

#### Returns

_GpuBigIntMatrix2x2_<br>
A struct holding AST nodes for the resulting multiplication.

## Examples

### $O(\log N)$ Fibonacci Sequence Orchestration

```csharp
// Execution Context: Host (CPU)

using var workspace = new GpuWorkspace(maxWords: 1024);

// Initial transition matrix for Fibonacci: [[1, 1], [1, 0]]
var gpuOne = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, 1));
var gpuZero = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, 0));

var transitionMatrix = new GpuBigIntMatrix2x2(
    m00: gpuOne, m01: gpuOne,
    m10: gpuOne, m11: gpuZero
);

// To find F_N, we want to compute transitionMatrix^N.
// You would typically use an Exponentiation by Squaring algorithm on the Host:
var resultMatrix = MatrixModPow(transitionMatrix, N); // Custom host loop calling Matrix.Multiply

// Evaluate the final element (M10 contains F_N)
using var fibN = resultMatrix.M10.Node.Evaluate(workspace, null);
Console.WriteLine($"Fibonacci({N}): {fibN.ToBigInteger()}");
```

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../../00_index.md)
- [GpuBigInt and GpuBigUInt Operators (G#)](../Operators.md)
