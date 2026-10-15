---
title: "IGpuBigIntArithmetic Interface (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Defines the low-level GPU device memory contract for arbitrary-precision integer arithmetic operations."
---

## IGpuBigIntArithmetic Interface

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Defines the low-level GPU device memory contract for arbitrary-precision integer arithmetic operations. Implementations operate directly on `GpuBigIntMemoryHandle` descriptors without allocating host-side objects.

```csharp
public interface IGpuBigIntArithmetic
```

## Methods

| Name | Description |
| --- | --- |
| [Add(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle)](#addref-gpubigintexecutioncontext-gpubigintmemoryhandle-gpubigintmemoryhandle-gpubigintmemoryhandle) | Adds two big integers in GPU device memory (`result = left + right`). |
| [BitwiseAnd(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle)](#bitwiseandref-gpubigintexecutioncontext-gpubigintmemoryhandle-gpubigintmemoryhandle-gpubigintmemoryhandle) | Performs bitwise AND on two big integers in GPU device memory (`result = left & right`). |
| [BitwiseNot(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle)](#bitwisenotref-gpubigintexecutioncontext-gpubigintmemoryhandle-gpubigintmemoryhandle) | Performs bitwise NOT on a big integer in GPU device memory (`result = ~target`). |
| [BitwiseOr(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle)](#bitwiseorref-gpubigintexecutioncontext-gpubigintmemoryhandle-gpubigintmemoryhandle-gpubigintmemoryhandle) | Performs bitwise OR on two big integers in GPU device memory (`result = left \| right`). |
| [BitwiseXor(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle)](#bitwisexorref-gpubigintexecutioncontext-gpubigintmemoryhandle-gpubigintmemoryhandle-gpubigintmemoryhandle) | Performs bitwise XOR on two big integers in GPU device memory (`result = left ^ right`). |
| [Div(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle)](#divref-gpubigintexecutioncontext-gpubigintmemoryhandle-gpubigintmemoryhandle-gpubigintmemoryhandle) | Divides one big integer by another in GPU device memory (`result = left / right`). |
| [Mod(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle)](#modref-gpubigintexecutioncontext-gpubigintmemoryhandle-gpubigintmemoryhandle-gpubigintmemoryhandle) | Computes the remainder of two big integers in GPU device memory (`result = left % right`). |
| [Mul(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle)](#mulref-gpubigintexecutioncontext-gpubigintmemoryhandle-gpubigintmemoryhandle-gpubigintmemoryhandle) | Multiplies two big integers in GPU device memory (`result = left * right`). |
| [ShiftLeft(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, GpuBigIntMemoryHandle)](#shiftleftref-gpubigintexecutioncontext-gpubigintmemoryhandle-int-gpubigintmemoryhandle) | Shifts a big integer left by the specified bit count in GPU device memory (`result = target << shiftBits`). |
| [ShiftRight(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, GpuBigIntMemoryHandle)](#shiftrightref-gpubigintexecutioncontext-gpubigintmemoryhandle-int-gpubigintmemoryhandle) | Shifts a big integer right by the specified bit count in GPU device memory (`result = target >> shiftBits`). |
| [Sqrt(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle)](#sqrtref-gpubigintexecutioncontext-gpubigintmemoryhandle-gpubigintmemoryhandle) | Computes the integer square root ($\lfloor\sqrt{\text{target}}\rfloor$) in GPU device memory. |
| [Sub(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle)](#subref-gpubigintexecutioncontext-gpubigintmemoryhandle-gpubigintmemoryhandle-gpubigintmemoryhandle) | Subtracts one big integer from another in GPU device memory (`result = left - right`). |

## Add(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle)

Adds two big integers in GPU device memory (`result = left + right`).

```csharp
void Add(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle left,
    GpuBigIntMemoryHandle right,
    GpuBigIntMemoryHandle result);
```

## BitwiseAnd(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle)

Performs bitwise AND on two big integers in GPU device memory (`result = left & right`).

```csharp
void BitwiseAnd(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle left,
    GpuBigIntMemoryHandle right,
    GpuBigIntMemoryHandle result);
```

## BitwiseNot(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle)

Performs bitwise NOT on a big integer in GPU device memory (`result = ~target`).

```csharp
void BitwiseNot(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle target,
    GpuBigIntMemoryHandle result);
```

## BitwiseOr(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle)

Performs bitwise OR on two big integers in GPU device memory (`result = left | right`).

```csharp
void BitwiseOr(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle left,
    GpuBigIntMemoryHandle right,
    GpuBigIntMemoryHandle result);
```

## BitwiseXor(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle)

Performs bitwise XOR on two big integers in GPU device memory (`result = left ^ right`).

```csharp
void BitwiseXor(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle left,
    GpuBigIntMemoryHandle right,
    GpuBigIntMemoryHandle result);
```

## Div(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle)

Divides one big integer by another in GPU device memory (`result = left / right`).

```csharp
void Div(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle left,
    GpuBigIntMemoryHandle right,
    GpuBigIntMemoryHandle result);
```

## Mod(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle)

Computes the remainder of two big integers in GPU device memory (`result = left % right`).

```csharp
void Mod(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle left,
    GpuBigIntMemoryHandle right,
    GpuBigIntMemoryHandle result);
```

## Mul(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle)

Multiplies two big integers in GPU device memory (`result = left * right`).

```csharp
void Mul(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle left,
    GpuBigIntMemoryHandle right,
    GpuBigIntMemoryHandle result);
```

## ShiftLeft(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, GpuBigIntMemoryHandle)

Shifts a big integer left by the specified bit count in GPU device memory (`result = target << shiftBits`).

```csharp
void ShiftLeft(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle target,
    int shiftBits,
    GpuBigIntMemoryHandle result);
```

## ShiftRight(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, GpuBigIntMemoryHandle)

Shifts a big integer right by the specified bit count in GPU device memory (`result = target >> shiftBits`).

```csharp
void ShiftRight(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle target,
    int shiftBits,
    GpuBigIntMemoryHandle result);
```

## Sqrt(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle)

Computes the integer square root ($\lfloor\sqrt{\text{target}}\rfloor$) in GPU device memory.

```csharp
void Sqrt(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle target,
    GpuBigIntMemoryHandle result);
```

## Sub(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle, GpuBigIntMemoryHandle)

Subtracts one big integer from another in GPU device memory (`result = left - right`).

```csharp
void Sub(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle left,
    GpuBigIntMemoryHandle right,
    GpuBigIntMemoryHandle result);
```

## Examples

### Dispatching Handle-Based Arithmetic via IGpuBigIntArithmetic

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);

using var a = GpuBigUInt.FromBigInteger(workspace, new BigInteger(150), minWords: 4);
using var b = GpuBigUInt.FromBigInteger(workspace, new BigInteger(250), minWords: 4);
using var result = new GpuBigUInt(workspace, 4);

var context = new GpuBigIntExecutionContext(workspace, workspace.MemoryManager, workspace.Stream);
IGpuBigIntArithmetic arithmetic = context.Arithmetic;

arithmetic.Add(ref context, a.Handle, b.Handle, result.Handle);
Console.WriteLine($"Sum: {result.ToBigInteger()}");
```

## See also

### BCL API > Math Bignum
- [GpuBigIntExecutionContext Struct](GpuBigIntExecutionContext.md)
- [IGpuMemoryManager Interface](IGpuMemoryManager.md)
- [BigIntExpressionVisitor Struct](expressions/BigIntExpressionVisitor.md)
- [Arbitrary-Precision Integers (GpuBigInt)](00_index.md)
