---
title: "IGpuBigIntVectorExpression Interface (G#)"
site: "G# Documentation"
published: "2026-10-12T00:00:00.000Z"
language: "en"
description: "Defines an expression node that evaluates to a contiguous array of GPU big integers."
---

## IGpuBigIntVectorExpression Interface

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Defines an expression AST node that evaluates to a contiguous array of GPU big integers (`GpuBigIntArray`).

```csharp
public interface IGpuBigIntVectorExpression : IDisposable
```

## Properties

| Name                                  | Description                                           |
| ------------------------------------- | ----------------------------------------------------- |
| [Length](#length)                     | Gets the number of elements in the vector.            |
| [WordsPerElement](#wordsperelement)   | Gets the number of 32-bit limbs (words) per element.  |

## Length

Gets the number of elements in the vector.

```csharp
int Length { get; }
```

## WordsPerElement

Gets the number of 32-bit limbs (words) per element.

```csharp
int WordsPerElement { get; }
```

## Methods

| Name                                                                                        | Description                                                               |
| ------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------- |
| [Evaluate(GpuWorkspace, GpuBigUIntArray?)](#evaluategpuworkspace-gpubiguintarray)          | Evaluates the vectorized expression into a new or existing device array.  |

## Evaluate(GpuWorkspace, GpuBigUIntArray?)

Evaluates the vectorized expression into a new or existing device array.

```csharp
GpuBigIntArray Evaluate(GpuWorkspace workspace, GpuBigUIntArray? targetResult = null);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace used for memory allocation and CUDA stream execution.

`targetResult` _GpuBigUIntArray?_<br>
An optional destination array for the evaluated values.

#### Returns

_GpuBigIntArray_<br>
A `GpuBigIntArray` containing the evaluated vector results.

## See also

### BCL API > Math Bignum
- [GpuBigIntArray Struct](../GpuBigIntArray.md)
- [GpuBigIntVectorLinqExtensions Class](GpuBigIntVectorLinqExtensions.md)
