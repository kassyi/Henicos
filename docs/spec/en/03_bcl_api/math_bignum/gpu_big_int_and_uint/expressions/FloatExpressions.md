---
title: "FloatExpressions and GpuBigFloatExpressionVisitor (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Zero-allocation generic binary expressions and stack-scoped visitor for arbitrary-precision floating-point arithmetic."
---

## FloatExpressions and GpuBigFloatExpressionVisitor

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Provides zero-allocation generic binary expression nodes (`BinaryExpr<T, TOp>`), static abstract operator tags (`IFloatOperator<T>`, `AddOp`, `SubOp`, `MulOp`), and the stack-scoped visitor (`GpuBigFloatExpressionVisitor`) that evaluates arbitrary-precision floating-point AST expressions (`GpuBigFloat`, `GpuBigUFloat`) across GPU and CPU backends.

```csharp
public interface IFloatOperator<T>
public readonly struct AddOp : IFloatOperator<GpuBigFloat>, IFloatOperator<GpuBigUFloat>
public readonly struct SubOp : IFloatOperator<GpuBigFloat>, IFloatOperator<GpuBigUFloat>
public readonly struct MulOp : IFloatOperator<GpuBigFloat>
public readonly struct BinaryExpr<T, TOp>(T left, T right) where TOp : struct, IFloatOperator<T>
public ref struct GpuBigFloatExpressionVisitor(GpuWorkspace workspace, IGpuBigFloatArithmetic? arithmetic = null)
```

## Types

| Name | Description |
| --- | --- |
| [IFloatOperator\<T\>](#ifloatoperatort-interface) | Defines an arithmetic operator capable of executing operations on arbitrary-precision float operands via static abstract polymorphism. |
| [AddOp](#addop-struct) | Static addition operator tag for `GpuBigFloat` and `GpuBigUFloat`. |
| [SubOp](#subop-struct) | Static subtraction operator tag for `GpuBigFloat` and `GpuBigUFloat`. |
| [MulOp](#mulop-struct) | Static multiplication operator tag for `GpuBigFloat`. |
| [BinaryExpr\<T, TOp\>](#binaryexprt-top-struct) | A zero-allocation generic binary expression node representing a deferred arbitrary-precision floating-point operation. |
| [GpuBigFloatExpressionVisitor](#gpubigfloatexpressionvisitor-struct) | A stack-scoped `ref struct` visitor that evaluates arbitrary-precision floating-point AST expressions across GPU and CPU backends. |

## IFloatOperator\<T\> Interface

Defines an arithmetic operator capable of executing operations on arbitrary-precision float operands via static abstract polymorphism.

```csharp
public interface IFloatOperator<T>
{
    static abstract T Execute(T left, T right, ref GpuBigFloatExpressionVisitor visitor);
}
```

## AddOp Struct

Static addition operator tag for arbitrary-precision floating-point operands.

```csharp
public readonly struct AddOp : IFloatOperator<GpuBigFloat>, IFloatOperator<GpuBigUFloat>
{
    public static GpuBigFloat Execute(GpuBigFloat left, GpuBigFloat right, ref GpuBigFloatExpressionVisitor visitor);
    public static GpuBigUFloat Execute(GpuBigUFloat left, GpuBigUFloat right, ref GpuBigFloatExpressionVisitor visitor);
}
```

## SubOp Struct

Static subtraction operator tag for arbitrary-precision floating-point operands.

```csharp
public readonly struct SubOp : IFloatOperator<GpuBigFloat>, IFloatOperator<GpuBigUFloat>
{
    public static GpuBigFloat Execute(GpuBigFloat left, GpuBigFloat right, ref GpuBigFloatExpressionVisitor visitor);
    public static GpuBigUFloat Execute(GpuBigUFloat left, GpuBigUFloat right, ref GpuBigFloatExpressionVisitor visitor);
}
```

## MulOp Struct

Static multiplication operator tag for arbitrary-precision floating-point operands.

```csharp
public readonly struct MulOp : IFloatOperator<GpuBigFloat>
{
    public static GpuBigFloat Execute(GpuBigFloat left, GpuBigFloat right, ref GpuBigFloatExpressionVisitor visitor);
}
```

## BinaryExpr\<T, TOp\> Struct

A zero-allocation generic binary expression node representing a deferred arbitrary-precision floating-point operation.

```csharp
public readonly struct BinaryExpr<T, TOp>(T left, T right)
    where TOp : struct, IFloatOperator<T>
{
    public readonly T Left;
    public readonly T Right;
    public T Evaluate(ref GpuBigFloatExpressionVisitor visitor);
}
```

## GpuBigFloatExpressionVisitor Struct

A stack-scoped visitor (`ref struct`) that evaluates arbitrary-precision floating-point AST expressions across GPU and CPU backends.

```csharp
public ref struct GpuBigFloatExpressionVisitor(
    GpuWorkspace workspace,
    IGpuBigFloatArithmetic? arithmetic = null)
{
    public GpuWorkspace Workspace;
    public IGpuBigFloatArithmetic Arithmetic { get; }

    public GpuBigFloat VisitAdd(GpuBigFloat left, GpuBigFloat right);
    public GpuBigFloat VisitSub(GpuBigFloat left, GpuBigFloat right);
    public GpuBigFloat VisitMul(GpuBigFloat left, GpuBigFloat right);
    public GpuBigUFloat VisitAdd(GpuBigUFloat left, GpuBigUFloat right);
    public GpuBigUFloat VisitSub(GpuBigUFloat left, GpuBigUFloat right);
    public GpuBigFloat Visit<TOp>(BinaryExpr<GpuBigFloat, TOp> expr) where TOp : struct, IFloatOperator<GpuBigFloat>;
    public GpuBigUFloat Visit<TOp>(BinaryExpr<GpuBigUFloat, TOp> expr) where TOp : struct, IFloatOperator<GpuBigUFloat>;
}
```

`VisitSub` on `GpuBigUFloat` throws `OverflowException` when `right` is greater than `left`: an unsigned value has no negative difference, as with a `checked` `uint` subtraction. A difference of zero is positive zero.

## Examples

### Evaluating a Deferred GpuBigFloat Binary Expression

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// Execution Context: Host (CPU)
using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

using var sigA = GpuBigUInt.FromBigInteger(workspace, 150, minWords: 4);
using var sigB = GpuBigUInt.FromBigInteger(workspace, 50, minWords: 4);
var floatA = new GpuBigFloat(new GpuBigUFloat(sigA, exponent: 0), isNegative: false);
var floatB = new GpuBigFloat(new GpuBigUFloat(sigB, exponent: 0), isNegative: false);

BinaryExpr<GpuBigFloat, AddOp> addExpr = floatA + floatB;
var visitor = new GpuBigFloatExpressionVisitor(workspace);
using GpuBigFloat sumFloat = visitor.Visit(addExpr);

Console.WriteLine($"Float sum negative: {sumFloat.IsNegative}");
```

## See also

### BCL API > Math Bignum

- [Arbitrary-Precision Floats (GpuBigFloat)](../../gpu_big_float/00_index.md)
- [GpuBigFloat Operators](../../gpu_big_float/Operators.md)
- [BigIntExpressionVisitor Struct](BigIntExpressionVisitor.md)
