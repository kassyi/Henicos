---
title: "IGpuNumber<TSelf> Interface & GenerateGpuNumberOperatorsAttribute (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Represents a numeric type executed on the GPU supporting deferred expression compilation and source-generated operator overloads."
---

## IGpuNumber&lt;TSelf&gt; Interface

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Represents a numeric type executed on the GPU, supporting deferred expression compilation via operator overloading.

```csharp
public interface IGpuNumber<TSelf> : IEquatable<TSelf>, IComparable<TSelf>
    where TSelf : struct, IGpuNumber<TSelf>
```

### Type Parameters

`TSelf`<br>
The concrete value type implementing this interface (e.g., [GpuBigUInt](GpuBigUInt.md)).

---

## GenerateGpuNumberOperatorsAttribute Class

Marks a struct to instruct the `GpuNumberOperatorGenerator` Roslyn Source Generator to emit arithmetic and comparison operator overloads (`+`, `-`, `==`, `!=`, `<`, `>`, `<=`, `>=`, and AST expression operators for `GpuBigUInt`).

```csharp
[AttributeUsage(AttributeTargets.Struct)]
public sealed class GenerateGpuNumberOperatorsAttribute : Attribute
```

## Examples

### Using IGpuNumber Constraint for Generic GPU Comparisons

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);

using var a = GpuBigUInt.FromBigInteger(workspace, new BigInteger(100), minWords: 4);
using var b = GpuBigUInt.FromBigInteger(workspace, new BigInteger(200), minWords: 4);

Console.WriteLine($"CompareResult = {CompareGpuNumbers(a, b)}");

static int CompareGpuNumbers<T>(T left, T right) where T : struct, IGpuNumber<T>
{
    return left.CompareTo(right);
}
```

## See also

### BCL API > Math Bignum
- [GpuBigUInt Struct](GpuBigUInt.md)
- [GpuUInt128 Struct](GpuUInt128.md)
- [GpuUInt256 Struct](GpuUInt256.md)
- [GpuUInt512 Struct](GpuUInt512.md)
- [Arbitrary-Precision Integers (GpuBigInt)](00_index.md)
