---
title: "IGpuBigIntExpression Interface (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Represents a signed GPU BigInteger AST node."
---

## IGpuBigIntExpression Interface

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Defines the contract for an abstract syntax tree (AST) node representing a lazily evaluated signed GPU BigInteger expression.

```csharp
public interface IGpuBigIntExpression
```

## Properties

| Name | Description |
| --- | --- |
| [NumWords](#numwords) | Gets the number of words (32-bit limbs) required to store the absolute magnitude of this expression. |

## NumWords

Gets the number of words (32-bit limbs) required to store the absolute magnitude of this expression.

```csharp
int NumWords { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [Evaluate(GpuWorkspace, GpuBigUInt?)](#evaluategpuworkspace-gpubiguint) | Evaluates this AST node into a new or provided GPU buffer, returning a signed `GpuBigInt`. |

## Evaluate(GpuWorkspace, GpuBigUInt?)

Evaluates this AST node into a new or provided GPU buffer, returning a signed `GpuBigInt`.

```csharp
GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null);
```

#### Parameters

`workspace` _[GpuWorkspace](../GpuWorkspace.md)_<br>
The GPU workspace managing kernel execution and memory pools.

`targetResult` _[GpuBigUInt](../GpuBigUInt.md)?_<br>
An optional pre-allocated buffer for the absolute magnitude. If `null`, a temporary buffer is rented.

#### Returns

_[GpuBigInt](../GpuBigInt.md)_<br>
A `GpuBigInt` containing the evaluated magnitude and sign flag.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
#nullable enable
// Execution Context: Host (CPU)
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
using var aUint = new GpuBigUInt(workspace, new uint[] { 10 });
using var bUint = new GpuBigUInt(workspace, new uint[] { 25 });

var a = new GpuBigInt(aUint, isNegative: false);
var b = new GpuBigInt(bUint, isNegative: false);

IGpuBigIntExpression expression = new CustomSumNode(a, b);
var result = expression.Evaluate(workspace);

var resultWords = new uint[1];
workspace.Provider.CopyDeviceToHost(resultWords, result.Value.DevicePointer, 1);

Console.WriteLine($"Result: {(result.IsNegative ? "-" : "")}{resultWords[0]}");

public class CustomSumNode : IGpuBigIntExpression
{
    private readonly GpuBigInt _left;
    private readonly GpuBigInt _right;

    public CustomSumNode(GpuBigInt left, GpuBigInt right)
    {
        _left = left;
        _right = right;
    }

    public int NumWords => System.Math.Max(_left.Value.NumWords, _right.Value.NumWords) + 1;

    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
    {
        var resultBuf = targetResult ?? new GpuBigUInt(workspace, NumWords);
        using var leftPad = _left.Value.PadTo(NumWords);
        using var rightPad = _right.Value.PadTo(NumWords);
        GpuBigIntMath.Add(leftPad, rightPad, resultBuf);
        return new GpuBigInt(resultBuf, isNegative: true);
    }
}
```

## See also

### BCL API > Math Bignum

- [GpuBigIntExpr Struct](GpuBigIntExpr.md)
- [Standard Math Library (Stdlib.Math)](../../../00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
