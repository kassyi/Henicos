---
title: "GpuBigIntKernels Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "CUDA kernel implementations for fundamental large integer operations including parallel addition, subtraction, bitwise logic, and shifts."
---

## GpuBigIntKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Contains the native G# (CUDA) kernels and device functions used for fundamental large integer operations such as parallel Kogge-Stone addition, subtraction, bit-shifting, and bitwise logic on the GPU.

```csharp
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntKernels
```

## Nested Types

| Name | Description |
| --- | --- |
| [CarryState](#carrystate-struct) | Represents the carry generation (G) and propagation (P) bits for prefix sum operations. |

## Methods

| Name | Description |
| --- | --- |
| [PackCarryState(CarryState)](#packcarrystatecarrystate) | A GPU device function (`[GpuDeviceFunction]`) that packs a `CarryState` into a `uint`. |
| [UnpackCarryState(uint)](#unpackcarrystateuint) | A GPU device function (`[GpuDeviceFunction]`) that unpacks a `uint` into a `CarryState`. |
| [WarpScanGp(CarryState, int)](#warpscangpcarrystate-int) | A GPU device function (`[GpuDeviceFunction]`) that computes the warp-wide prefix sum of carry generation and propagation. |
| [ApplyCarry(CarryState, uint)](#applycarrycarrystate-uint) | A GPU device function (`[GpuDeviceFunction]`) that applies an incoming carry to the carry state. |
| [DeviceAddSubParallel(uint[], uint[], uint[], int, int, bool)](#deviceaddsubparalleluint-uint-uint-int-int-bool) | A GPU device function (`[GpuDeviceFunction]`) that performs block-parallel addition or subtraction using warp/block Kogge-Stone carry lookahead. |
| [ParallelAddKernel(uint[], uint[], uint[], int)](#paralleladdkerneluint-uint-uint-int) | A GPU kernel (`[GpuKernel]`) that performs parallel addition on large integers. |
| [ParallelSubKernel(uint[], uint[], uint[], int)](#parallelsubkerneluint-uint-uint-int) | A GPU kernel (`[GpuKernel]`) that performs parallel subtraction on large integers. |
| [ShiftLeftKernel(uint[], int, int, uint[], int)](#shiftleftkerneluint-int-int-uint-int) | A GPU kernel (`[GpuKernel]`) that performs a left bit-shift operation. |
| [ShiftRightKernel(uint[], int, int, uint[], int)](#shiftrightkerneluint-int-int-uint-int) | A GPU kernel (`[GpuKernel]`) that performs a right bit-shift operation. |
| [BitwiseAndKernel(uint[], uint[], uint[], int)](#bitwiseandkerneluint-uint-uint-int) | A GPU kernel (`[GpuKernel]`) that performs a bitwise AND operation. |
| [BitwiseOrKernel(uint[], uint[], uint[], int)](#bitwiseorkerneluint-uint-uint-int) | A GPU kernel (`[GpuKernel]`) that performs a bitwise OR operation. |
| [BitwiseXorKernel(uint[], uint[], uint[], int)](#bitwisexorkerneluint-uint-uint-int) | A GPU kernel (`[GpuKernel]`) that performs a bitwise XOR operation. |
| [BitwiseNotKernel(uint[], uint[], int)](#bitwisenotkerneluint-uint-int) | A GPU kernel (`[GpuKernel]`) that performs a bitwise NOT operation. |
| [ExecuteShiftLeft(GpuBigIntContext, int, int)](#executeshiftleftgpubigintcontext-int-int) | A GPU device function (`[GpuDeviceFunction]`) that performs a grid-stride left bit-shift using `GpuBigIntContext`. |
| [ExecuteShiftRight(GpuBigIntContext, int, int)](#executeshiftrightgpubigintcontext-int-int) | A GPU device function (`[GpuDeviceFunction]`) that performs a grid-stride right bit-shift using `GpuBigIntContext`. |
| [ExecuteBitwiseAnd(ref GpuBigIntContext)](#executebitwiseandref-gpubigintcontext) | A GPU device function (`[GpuDeviceFunction]`) that performs a grid-stride bitwise AND using `GpuBigIntContext`. |
| [ExecuteBitwiseOr(ref GpuBigIntContext)](#executebitwiseorref-gpubigintcontext) | A GPU device function (`[GpuDeviceFunction]`) that performs a grid-stride bitwise OR using `GpuBigIntContext`. |
| [ExecuteBitwiseXor(ref GpuBigIntContext)](#executebitwisexorref-gpubigintcontext) | A GPU device function (`[GpuDeviceFunction]`) that performs a grid-stride bitwise XOR using `GpuBigIntContext`. |
| [ExecuteBitwiseNot(ref GpuBigIntContext)](#executebitwisenotref-gpubigintcontext) | A GPU device function (`[GpuDeviceFunction]`) that performs a grid-stride bitwise NOT using `GpuBigIntContext`. |
| [ExecuteAdd(ref GpuBigIntContext, uint[], uint[], uint[])](#executeaddref-gpubigintcontext-uint-uint-uint) | A GPU device function (`[GpuDeviceFunction]`) that performs sequential multi-word addition with carry propagation for fused expression kernels. |
| [ExecuteSub(ref GpuBigIntContext, uint[], uint[], uint[])](#executesubref-gpubigintcontext-uint-uint-uint) | A GPU device function (`[GpuDeviceFunction]`) that performs sequential multi-word subtraction with borrow propagation for fused expression kernels. |
| [ExecuteShiftLeft(ref GpuBigIntContext, uint[], uint[], int)](#executeshiftleftref-gpubigintcontext-uint-uint-int) | A GPU device function (`[GpuDeviceFunction]`) that performs a sequential multi-word left bit-shift for fused expression kernels. |
| [ExecuteShiftRight(ref GpuBigIntContext, uint[], uint[], int)](#executeshiftrightref-gpubigintcontext-uint-uint-int) | A GPU device function (`[GpuDeviceFunction]`) that performs a sequential multi-word right bit-shift for fused expression kernels. |
| [ExecuteBitwiseAnd(ref GpuBigIntContext, uint[], uint[], uint[])](#executebitwiseandref-gpubigintcontext-uint-uint-uint) | A GPU device function (`[GpuDeviceFunction]`) that performs a sequential multi-word bitwise AND for fused expression kernels. |
| [ExecuteBitwiseOr(ref GpuBigIntContext, uint[], uint[], uint[])](#executebitwiseorref-gpubigintcontext-uint-uint-uint) | A GPU device function (`[GpuDeviceFunction]`) that performs a sequential multi-word bitwise OR for fused expression kernels. |
| [ExecuteBitwiseXor(ref GpuBigIntContext, uint[], uint[], uint[])](#executebitwisexorref-gpubigintcontext-uint-uint-uint) | A GPU device function (`[GpuDeviceFunction]`) that performs a sequential multi-word bitwise XOR for fused expression kernels. |
| [ExecuteBitwiseNot(ref GpuBigIntContext, uint[], uint[])](#executebitwisenotref-gpubigintcontext-uint-uint) | A GPU device function (`[GpuDeviceFunction]`) that performs a sequential multi-word bitwise NOT for fused expression kernels. |

## CarryState Struct

Represents the carry generation (G) and propagation (P) bits for prefix sum operations.

```csharp
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public struct CarryState(uint generate, uint propagate)
{
    public uint Generate = generate;
    public uint Propagate = propagate;
}
```

#### Fields

`Generate` _uint_<br>
The carry generation bit. Typically `1` if the current element generates a carry (e.g., sum > max), otherwise `0`.

`Propagate` _uint_<br>
The carry propagation bit. Typically `1` if the current element propagates an incoming carry (e.g., sum == max), otherwise `0`.

## PackCarryState(CarryState)

A GPU device function (`[GpuDeviceFunction]`) that packs a `CarryState` into a single `uint` for shared memory storage.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static uint PackCarryState(CarryState state);
```

#### Parameters

`state` _CarryState_<br>
The carry state to pack.

#### Returns
_uint_<br>
The bit-packed representation of the carry state.

## UnpackCarryState(uint)

A GPU device function (`[GpuDeviceFunction]`) that unpacks a `uint` back into a `CarryState`.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static CarryState UnpackCarryState(uint packed);
```

#### Parameters

`packed` _uint_<br>
The bit-packed carry state previously created by `PackCarryState`.

#### Returns
_CarryState_<br>
The unpacked `CarryState` struct.

## WarpScanGp(CarryState, int)

A GPU device function (`[GpuDeviceFunction]`) that computes the warp-wide prefix sum of carry generation (G) and propagation (P) bits using the Kogge-Stone algorithm.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static CarryState WarpScanGp(CarryState state, int laneId);
```

#### Parameters

`state` _CarryState_<br>
The carry state containing Generate and Propagate bits for the current thread.

`laneId` _int_<br>
The thread's lane index within the warp (0 to 31).

#### Returns
_CarryState_<br>
The warp-wide inclusive prefix sum result containing the accumulated Generate and Propagate bits.

## ApplyCarry(CarryState, uint)

A GPU device function (`[GpuDeviceFunction]`) that applies an incoming carry to the carry state.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static uint ApplyCarry(CarryState state, uint carryIn);
```

#### Parameters

`state` _CarryState_<br>
The carry state containing Generate and Propagate bits, typically produced by `WarpScanGp`.

`carryIn` _uint_<br>
The carry-in bit to propagate through the P bit.

#### Returns
_uint_<br>
The final resolved carry bit (0 or 1) for the current segment.

## DeviceAddSubParallel(uint[], uint[], uint[], int, int, bool)

A GPU device function (`[GpuDeviceFunction]`) that performs block-parallel addition or subtraction using warp/block Kogge-Stone carry lookahead.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceAddSubParallel(
    [GpuReadOnly] uint[] a,
    [GpuReadOnly] uint[] b,
    uint[] result,
    int numWords,
    int sign = 1,
    bool syncThreads = true);
```

#### Parameters

`a` _uint[]_<br>
The first operand buffer.

`b` _uint[]_<br>
The second operand buffer.

`result` _uint[]_<br>
The result buffer.

`numWords` _int_<br>
The total number of words.

`sign` _int_<br>
`1` for addition (`a + b`), or `-1` for subtraction (`a - b`).

`syncThreads` _bool_<br>
If `true`, synchronizes threads within the block upon completion.

## ParallelAddKernel(uint[], uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that performs parallel addition on large integers.

```csharp
[GpuKernel]
public static void ParallelAddKernel([GpuReadOnly] uint[] dA, [GpuReadOnly] uint[] dB, uint[] dResult, int numWords);
```

#### Parameters

`dA` _uint[]_<br>
The first operand buffer.

`dB` _uint[]_<br>
The second operand buffer.

`dResult` _uint[]_<br>
The result buffer.

`numWords` _int_<br>
The total number of words.

## ParallelSubKernel(uint[], uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that performs parallel subtraction on large integers.

```csharp
[GpuKernel]
public static void ParallelSubKernel([GpuReadOnly] uint[] dA, [GpuReadOnly] uint[] dB, uint[] dResult, int numWords);
```

#### Parameters

`dA` _uint[]_<br>
The first operand buffer.

`dB` _uint[]_<br>
The second operand buffer.

`dResult` _uint[]_<br>
The result buffer.

`numWords` _int_<br>
The total number of words.

## ShiftLeftKernel(uint[], int, int, uint[], int)

A GPU kernel (`[GpuKernel]`) that performs a left bit-shift operation.

```csharp
[GpuKernel]
public static void ShiftLeftKernel(uint[] dA, int wordShift, int bitShift, uint[] dResult, int numWords);
```

#### Parameters

`dA` _uint[]_<br>
The input buffer.

`wordShift` _int_<br>
The number of whole words to shift.

`bitShift` _int_<br>
The number of bits (0-31) to shift.

`dResult` _uint[]_<br>
The result buffer.

`numWords` _int_<br>
The total number of words.

## ShiftRightKernel(uint[], int, int, uint[], int)

A GPU kernel (`[GpuKernel]`) that performs a right bit-shift operation.

```csharp
[GpuKernel]
public static void ShiftRightKernel(uint[] dA, int wordShift, int bitShift, uint[] dResult, int numWords);
```

#### Parameters

`dA` _uint[]_<br>
The input buffer.

`wordShift` _int_<br>
The number of whole words to shift.

`bitShift` _int_<br>
The number of bits (0-31) to shift.

`dResult` _uint[]_<br>
The result buffer.

`numWords` _int_<br>
The total number of words.

## BitwiseAndKernel(uint[], uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that performs a bitwise AND operation.

```csharp
[GpuKernel]
public static void BitwiseAndKernel(uint[] dA, uint[] dB, uint[] dResult, int numWords);
```

#### Parameters

`dA` _uint[]_<br>
The first input buffer.

`dB` _uint[]_<br>
The second input buffer.

`dResult` _uint[]_<br>
The result buffer.

`numWords` _int_<br>
The total number of words.

## BitwiseOrKernel(uint[], uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that performs a bitwise OR operation.

```csharp
[GpuKernel]
public static void BitwiseOrKernel(uint[] dA, uint[] dB, uint[] dResult, int numWords);
```

#### Parameters

`dA` _uint[]_<br>
The first input buffer.

`dB` _uint[]_<br>
The second input buffer.

`dResult` _uint[]_<br>
The result buffer.

`numWords` _int_<br>
The total number of words.

## BitwiseXorKernel(uint[], uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that performs a bitwise XOR operation.

```csharp
[GpuKernel]
public static void BitwiseXorKernel(uint[] dA, uint[] dB, uint[] dResult, int numWords);
```

#### Parameters

`dA` _uint[]_<br>
The first input buffer.

`dB` _uint[]_<br>
The second input buffer.

`dResult` _uint[]_<br>
The result buffer.

`numWords` _int_<br>
The total number of words.

## BitwiseNotKernel(uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that performs a bitwise NOT operation.

```csharp
[GpuKernel]
public static void BitwiseNotKernel(uint[] dA, uint[] dResult, int numWords);
```

#### Parameters

`dA` _uint[]_<br>
The input buffer.

`dResult` _uint[]_<br>
The result buffer.

`numWords` _int_<br>
The total number of words.

## ExecuteShiftLeft(GpuBigIntContext, int, int)

A GPU device function (`[GpuDeviceFunction]`) that performs a grid-stride left bit-shift using `GpuBigIntContext`.

```csharp
[GpuDeviceFunction]
public static void ExecuteShiftLeft(GpuBigIntContext ctx, int wordShift, int bitShift);
```

## ExecuteShiftRight(GpuBigIntContext, int, int)

A GPU device function (`[GpuDeviceFunction]`) that performs a grid-stride right bit-shift using `GpuBigIntContext`.

```csharp
[GpuDeviceFunction]
public static void ExecuteShiftRight(GpuBigIntContext ctx, int wordShift, int bitShift);
```

## ExecuteBitwiseAnd(ref GpuBigIntContext)

A GPU device function (`[GpuDeviceFunction]`) that performs a grid-stride bitwise AND using `GpuBigIntContext`.

```csharp
[GpuDeviceFunction]
public static void ExecuteBitwiseAnd(ref GpuBigIntContext ctx);
```

## ExecuteBitwiseOr(ref GpuBigIntContext)

A GPU device function (`[GpuDeviceFunction]`) that performs a grid-stride bitwise OR using `GpuBigIntContext`.

```csharp
[GpuDeviceFunction]
public static void ExecuteBitwiseOr(ref GpuBigIntContext ctx);
```

## ExecuteBitwiseXor(ref GpuBigIntContext)

A GPU device function (`[GpuDeviceFunction]`) that performs a grid-stride bitwise XOR using `GpuBigIntContext`.

```csharp
[GpuDeviceFunction]
public static void ExecuteBitwiseXor(ref GpuBigIntContext ctx);
```

## ExecuteBitwiseNot(ref GpuBigIntContext)

A GPU device function (`[GpuDeviceFunction]`) that performs a grid-stride bitwise NOT using `GpuBigIntContext`.

```csharp
[GpuDeviceFunction]
public static void ExecuteBitwiseNot(ref GpuBigIntContext ctx);
```

## ExecuteAdd(ref GpuBigIntContext, uint[], uint[], uint[])

A GPU device function (`[GpuDeviceFunction]`) that performs sequential multi-word addition with carry propagation for fused expression kernels.

```csharp
[GpuDeviceFunction]
public static void ExecuteAdd(ref GpuBigIntContext ctx, uint[] result, uint[] left, uint[] right);
```

## ExecuteSub(ref GpuBigIntContext, uint[], uint[], uint[])

A GPU device function (`[GpuDeviceFunction]`) that performs sequential multi-word subtraction with borrow propagation for fused expression kernels.

```csharp
[GpuDeviceFunction]
public static void ExecuteSub(ref GpuBigIntContext ctx, uint[] result, uint[] left, uint[] right);
```

## ExecuteShiftLeft(ref GpuBigIntContext, uint[], uint[], int)

A GPU device function (`[GpuDeviceFunction]`) that performs a sequential multi-word left bit-shift for fused expression kernels.

```csharp
[GpuDeviceFunction]
public static void ExecuteShiftLeft(ref GpuBigIntContext ctx, uint[] result, uint[] left, int shiftBits);
```

## ExecuteShiftRight(ref GpuBigIntContext, uint[], uint[], int)

A GPU device function (`[GpuDeviceFunction]`) that performs a sequential multi-word right bit-shift for fused expression kernels.

```csharp
[GpuDeviceFunction]
public static void ExecuteShiftRight(ref GpuBigIntContext ctx, uint[] result, uint[] left, int shiftBits);
```

## ExecuteBitwiseAnd(ref GpuBigIntContext, uint[], uint[], uint[])

A GPU device function (`[GpuDeviceFunction]`) that performs a sequential multi-word bitwise AND for fused expression kernels.

```csharp
[GpuDeviceFunction]
public static void ExecuteBitwiseAnd(ref GpuBigIntContext ctx, uint[] result, uint[] left, uint[] right);
```

## ExecuteBitwiseOr(ref GpuBigIntContext, uint[], uint[], uint[])

A GPU device function (`[GpuDeviceFunction]`) that performs a sequential multi-word bitwise OR for fused expression kernels.

```csharp
[GpuDeviceFunction]
public static void ExecuteBitwiseOr(ref GpuBigIntContext ctx, uint[] result, uint[] left, uint[] right);
```

## ExecuteBitwiseXor(ref GpuBigIntContext, uint[], uint[], uint[])

A GPU device function (`[GpuDeviceFunction]`) that performs a sequential multi-word bitwise XOR for fused expression kernels.

```csharp
[GpuDeviceFunction]
public static void ExecuteBitwiseXor(ref GpuBigIntContext ctx, uint[] result, uint[] left, uint[] right);
```

## ExecuteBitwiseNot(ref GpuBigIntContext, uint[], uint[])

A GPU device function (`[GpuDeviceFunction]`) that performs a sequential multi-word bitwise NOT for fused expression kernels.

```csharp
[GpuDeviceFunction]
public static void ExecuteBitwiseNot(ref GpuBigIntContext ctx, uint[] result, uint[] operand);
```

## Examples

### Internal Kernel Usage

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// Execution Context: G# Kernel (GPU)
// Inspect CarryState packing and unpacking used by Kogge-Stone carry lookahead kernels.
var state = new GpuBigIntKernels.CarryState(generate: 1, propagate: 0);
uint packed = GpuBigIntKernels.PackCarryState(state);
var unpacked = GpuBigIntKernels.UnpackCarryState(packed);
Console.WriteLine($"CarryState Generate={unpacked.Generate}, Propagate={unpacked.Propagate}");
```

## Remarks

This class contains `[GpuKernel]` and `[GpuDeviceFunction]` attributed methods. These methods cannot be executed directly on the CPU (Host). They are transpiled to CUDA PTX/SASS by the G# Roslyn Source Generator and launched via the `GpuWorkspace`.

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuWorkspace](../../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)

### BCL API > GPU Class

- [Gpu Class](../../../../03_bcl_api/gpu_class/00_index.md)
- [InlinePtx](../../../../03_bcl_api/gpu_class/InlinePtx.md)

### Guides

- [4. Practical Guides and Best Practices](../../../../04_guides/00_index.md)
- [Advanced: Extending GpuBigInt with Custom Algorithms](../../../../04_guides/08_advanced_custom_math_kernels.md)
- [7. Stdlib Math and Lazy Evaluation](../../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
