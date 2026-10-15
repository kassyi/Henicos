---
title: "IGpuBoolExpr and VectorCompareOperator (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Defines deferred scalar boolean expressions and vector element comparison operators on the GPU."
---

## IGpuBoolExpr and VectorCompareOperator

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Defines the `IGpuBoolExpr` interface for deferred scalar boolean reductions (such as `Any` and `All` on GPU boolean vectors) and the `VectorCompareOperator` enumeration specifying element-wise comparison operators across `GpuBigInt` vectors.

```csharp
public interface IGpuBoolExpr : IDisposable
{
    bool Evaluate(GpuWorkspace workspace);
}

public enum VectorCompareOperator
{
    Equal = 0,
    NotEqual = 1,
    GreaterThan = 2,
    GreaterThanOrEqual = 3,
    LessThan = 4,
    LessThanOrEqual = 5
}
```

## Types

| Name | Description |
| --- | --- |
| [IGpuBoolExpr](#igpuboolexpr-interface) | Represents a deferred scalar boolean expression on the GPU. |
| [VectorCompareOperator](#vectorcompareoperator-enum) | Specifies the type of comparison operation between GPU vector elements. |

## IGpuBoolExpr Interface

Represents a deferred scalar boolean expression on the GPU.

```csharp
public interface IGpuBoolExpr : IDisposable
```

### Methods

| Name | Description |
| --- | --- |
| [Evaluate(GpuWorkspace)](#evaluategpuworkspace) | Evaluates the boolean expression on the GPU and copies the scalar boolean result to the host. |

### Evaluate(GpuWorkspace)

Evaluates the boolean expression on the GPU and copies the scalar boolean result to the host.

```csharp
bool Evaluate(GpuWorkspace workspace);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace memory context.

#### Returns

_Boolean_<br>
`true` if the condition evaluated to true; otherwise, `false`.

## VectorCompareOperator Enum

Specifies the type of comparison operation between GPU vector elements.

```csharp
public enum VectorCompareOperator
{
    Equal = 0,
    NotEqual = 1,
    GreaterThan = 2,
    GreaterThanOrEqual = 3,
    LessThan = 4,
    LessThanOrEqual = 5
}
```

## Examples

### Selecting a Vector Comparison Operator

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// Execution Context: Host (CPU)
Console.WriteLine($"Compare op: {VectorCompareOperator.GreaterThanOrEqual}");
```

## See also

### BCL API > Math Bignum

- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
- [IGpuBigIntVectorExpression Interface](IGpuBigIntVectorExpression.md)
- [GpuBigIntVectorLinqExtensions Class](GpuBigIntVectorLinqExtensions.md)
