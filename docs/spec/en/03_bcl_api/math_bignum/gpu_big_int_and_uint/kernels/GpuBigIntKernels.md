---
title: "GpuBigIntKernels Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "CUDA kernel implementations for fundamental large integer operations including addition, subtraction, and bitwise shifts."
---

## GpuBigIntKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigInt.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Contains the native G# (CUDA) kernels used for fundamental large integer operations such as chunked addition, subtraction, shifting, and bitwise logic on the GPU.

```csharp
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static class GpuBigIntKernels
```

## Methods

| Name | Description |
| --- | --- |
| [ChunkAddKernel(uint[], uint[], uint[], long[], int, int)](#chunkaddkerneluint-uint-uint-long-int-int) | A GPU kernel (`[GpuKernel]`) that performs addition on large integers in chunks. |
| [ApplyAddCarryKernel(uint[], long[], int, int, int)](#applyaddcarrykerneluint-long-int-int-int) | A GPU kernel (`[GpuKernel]`) that applies carry values across chunks for addition. |
| [ChunkSubKernel(uint[], uint[], uint[], long[], int, int)](#chunksubkerneluint-uint-uint-long-int-int) | A GPU kernel (`[GpuKernel]`) that performs subtraction on large integers in chunks. |
| [ApplySubBorrowKernel(uint[], long[], int, int, int)](#applysubborrowkerneluint-long-int-int-int) | A GPU kernel (`[GpuKernel]`) that applies borrow values across chunks for subtraction. |
| [ShiftLeftKernel(uint[], int, int, uint[], int)](#shiftleftkerneluint-int-int-uint-int) | A GPU kernel (`[GpuKernel]`) that performs a left bit-shift operation. |
| [ShiftRightKernel(uint[], int, int, uint[], int)](#shiftrightkerneluint-int-int-uint-int) | A GPU kernel (`[GpuKernel]`) that performs a right bit-shift operation. |
| [BitwiseAndKernel(uint[], uint[], uint[], int)](#bitwiseandkerneluint-uint-uint-int) | A GPU kernel (`[GpuKernel]`) that performs a bitwise AND operation. |
| [BitwiseOrKernel(uint[], uint[], uint[], int)](#bitwiseorkerneluint-uint-uint-int) | A GPU kernel (`[GpuKernel]`) that performs a bitwise OR operation. |
| [BitwiseXorKernel(uint[], uint[], uint[], int)](#bitwisexorkerneluint-uint-uint-int) | A GPU kernel (`[GpuKernel]`) that performs a bitwise XOR operation. |
| [BitwiseNotKernel(uint[], uint[], int)](#bitwisenotkerneluint-uint-int) | A GPU kernel (`[GpuKernel]`) that performs a bitwise NOT operation. |
| [PackCarryState(CarryState)](#packcarrystatedcarrystated) | A GPU device function (`[GpuDeviceFunction]`) that packs a CarryState into a uint. |
| [UnpackCarryState(uint)](#unpackcarrystateuint) | A GPU device function (`[GpuDeviceFunction]`) that unpacks a uint into a CarryState. |
| [WarpScanGp(CarryState, int)](#warpscangpcarrystate-int) | A GPU device function (`[GpuDeviceFunction]`) that computes the warp-wide prefix sum of carry generation and propagation. |
| [ApplyCarry(CarryState, uint)](#applycarrycarrystate-uint) | A GPU device function (`[GpuDeviceFunction]`) that applies an incoming carry to the carry state. |

## CarryState Struct

Represents the carry generation (G) and propagation (P) bits for prefix sum operations.

```csharp
public struct CarryState
{
    public uint Generate;
    public uint Propagate;

    public CarryState(uint generate, uint propagate);
}
```

#### Fields

`Generate` _uint_<br>
The carry generation bit. Typically `1` if the current element generates a carry (e.g., sum > max), otherwise `0`.

`Propagate` _uint_<br>
The carry propagation bit. Typically `1` if the current element propagates an incoming carry (e.g., sum == max), otherwise `0`.

#### Constructors

##### CarryState(uint, uint)

Initializes a new instance of the `CarryState` struct.

```csharp
public CarryState(uint generate, uint propagate);
```

###### Parameters

`generate` _uint_<br>
The initial generation bit value.

`propagate` _uint_<br>
The initial propagation bit value.

## PackCarryState(CarryState)

A GPU device function (`[GpuDeviceFunction]`) that packs a `CarryState` into a single `uint` for shared memory storage.

```csharp
[GpuDeviceFunction]
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

## ChunkAddKernel(uint[], uint[], uint[], long[], int, int)

A GPU kernel (`[GpuKernel]`) that performs addition on large integers in chunks.

```csharp
[GpuKernel]
public static void ChunkAddKernel(uint[] dA, uint[] dB, uint[] dResult, long[] carryBuffer, int numWords, int chunkSize);
```

#### Parameters

`dA` _uint[]_<br>
The first operand buffer.

`dB` _uint[]_<br>
The second operand buffer.

`dResult` _uint[]_<br>
The result buffer.

`carryBuffer` _long[]_<br>
The buffer to store chunk carries.

`numWords` _int_<br>
The total number of words.

`chunkSize` _int_<br>
The number of words processed per block.

## ApplyAddCarryKernel(uint[], long[], int, int, int)

A GPU kernel (`[GpuKernel]`) that applies carry values across chunks for addition.

```csharp
[GpuKernel]
public static void ApplyAddCarryKernel(uint[] dResult, long[] carryBuffer, int numChunks, int numWords, int chunkSize);
```

#### Parameters

`dResult` _uint[]_<br>
The result buffer to which carries are applied.

`carryBuffer` _long[]_<br>
The buffer containing chunk carries.

`numChunks` _int_<br>
The total number of chunks.

`numWords` _int_<br>
The total number of words.

`chunkSize` _int_<br>
The number of words processed per chunk.

## ChunkSubKernel(uint[], uint[], uint[], long[], int, int)

A GPU kernel (`[GpuKernel]`) that performs subtraction on large integers in chunks.

```csharp
[GpuKernel]
public static void ChunkSubKernel(uint[] dA, uint[] dB, uint[] dResult, long[] carryBuffer, int numWords, int chunkSize);
```

#### Parameters

`dA` _uint[]_<br>
The first operand buffer.

`dB` _uint[]_<br>
The second operand buffer.

`dResult` _uint[]_<br>
The result buffer.

`carryBuffer` _long[]_<br>
The buffer to store chunk borrows.

`numWords` _int_<br>
The total number of words.

`chunkSize` _int_<br>
The number of words processed per chunk.

## ApplySubBorrowKernel(uint[], long[], int, int, int)

A GPU kernel (`[GpuKernel]`) that applies borrow values across chunks for subtraction.

```csharp
[GpuKernel]
public static void ApplySubBorrowKernel(uint[] dResult, long[] carryBuffer, int numChunks, int numWords, int chunkSize);
```

#### Parameters

`dResult` _uint[]_<br>
The result buffer to which borrows are applied.

`carryBuffer` _long[]_<br>
The buffer containing chunk borrows.

`numChunks` _int_<br>
The total number of chunks.

`numWords` _int_<br>
The total number of words.

`chunkSize` _int_<br>
The number of words processed per chunk.

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

## Examples

### Internal Kernel Usage

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Runtime, Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigInt.Kernels;

// Execution Context: G# Kernel (GPU)
// This is an internal Kernel class and its methods are launched via the G# execution pipeline.
Console.WriteLine("GpuBigIntKernels contains static kernel endpoints.");
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
