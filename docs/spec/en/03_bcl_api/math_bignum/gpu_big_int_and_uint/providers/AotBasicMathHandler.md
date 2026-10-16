---
title: "AotBasicMathHandler Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Dispatches elementary GPU BigInteger arithmetic, bitwise logic, and comparison operations to compiled GSharp device kernels."
---

## AotBasicMathHandler Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Implements [`IGpuBasicMathProvider`](IGpuExecutionProvider.md) by dispatching elementary multi-precision addition, subtraction, scalar addition/subtraction, bit-shifts, bitwise logic, and comparison kernels to compiled G# CUDA kernels.

```csharp
public sealed class AotBasicMathHandler : IGpuBasicMathProvider
```

## Methods

| Name | Description |
| --- | --- |
| [Add(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#addgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Launches `GpuBigIntKernels.ParallelAddKernel` for single-block Kogge-Stone parallel addition. |
| [Sub(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#subgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Launches `GpuBigIntKernels.ParallelSubKernel` for single-block Kogge-Stone parallel subtraction. |
| [Add(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)](#addgpuworkspace-gpubiguint-uint32-gpubiguint) | Launches `GpuBigIntScalarKernels.ScalarAddKernel` for parallel scalar addition. |
| [Sub(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)](#subgpuworkspace-gpubiguint-uint32-gpubiguint) | Launches `GpuBigIntScalarKernels.ScalarSubKernel` for parallel scalar subtraction. |
| [BitwiseAnd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwiseandgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Launches `GpuBigIntKernels.BitwiseAndKernel` across a 1D grid. |
| [BitwiseOr(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwiseorgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Launches `GpuBigIntKernels.BitwiseOrKernel` across a 1D grid. |
| [BitwiseXor(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwisexorgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Launches `GpuBigIntKernels.BitwiseXorKernel` across a 1D grid. |
| [BitwiseNot(GpuWorkspace, GpuBigUInt, GpuBigUInt)](#bitwisenotgpuworkspace-gpubiguint-gpubiguint) | Launches `GpuBigIntKernels.BitwiseNotKernel` across a 1D grid. |
| [ShiftLeft(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)](#shiftleftgpuworkspace-gpubiguint-int32-gpubiguint) | Launches `GpuBigIntKernels.ShiftLeftKernel` with decomposed word and bit offsets. |
| [ShiftRight(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)](#shiftrightgpuworkspace-gpubiguint-int32-gpubiguint) | Launches `GpuBigIntKernels.ShiftRightKernel` with decomposed word and bit offsets. |
| [Square(GpuWorkspace, IntPtr, Int32)](#squaregpuworkspace-intptr-int32) | Computes in-place squaring by delegating to `GpuBigIntMath.Mul`. |
| [IsZero(GpuWorkspace, GpuBigUInt)](#iszerogpuworkspace-gpubiguint) | Evaluates `GpuBigIntCmpKernels.IsZeroKernel` and returns `true` if all words are zero. |
| [IsOne(GpuWorkspace, GpuBigUInt)](#isonegpuworkspace-gpubiguint) | Evaluates `GpuBigIntCmpKernels.IsOneKernel` and returns `true` if the operand equals `1`. |
| [GetBitLength(GpuWorkspace, GpuBigUInt)](#getbitlengthgpuworkspace-gpubiguint) | Evaluates `GpuBigIntCmpKernels.GetBitLengthKernel` and returns the significant bit length. |
| [CompareTo(GpuWorkspace, GpuBigUInt, GpuBigUInt)](#comparetogpuworkspace-gpubiguint-gpubiguint) | Evaluates `GpuBigIntCmpKernels.CompareToKernel` (zero-padding mismatched word counts if necessary) and returns `-1`, `0`, or `1`. |

## Add(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Launches `GpuBigIntKernels.ParallelAddKernel` on a single 256-thread block (`gridDim = 1, blockDim = 256`) using a Kogge-Stone parallel prefix carry lookahead.

```csharp
public void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing temporary buffers and the active CUDA stream.

`leftOperand` _GpuBigUInt_<br>
The left unsigned big integer operand.

`rightOperand` _GpuBigUInt_<br>
The right unsigned big integer operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer receiving `leftOperand + rightOperand`.

#### Exceptions

`CudaException`<br>
Thrown when the kernel launch fails.

## Sub(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Launches `GpuBigIntKernels.ParallelSubKernel` on a single 256-thread block (`gridDim = 1, blockDim = 256`) using a Kogge-Stone parallel prefix borrow lookahead.

```csharp
public void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing temporary buffers and the active CUDA stream.

`leftOperand` _GpuBigUInt_<br>
The minuend unsigned big integer operand.

`rightOperand` _GpuBigUInt_<br>
The subtrahend unsigned big integer operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer receiving `leftOperand - rightOperand`.

#### Exceptions

`CudaException`<br>
Thrown when the kernel launch fails.

## Add(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)

Launches `GpuBigIntScalarKernels.ScalarAddKernel` on a single 256-thread block to add a 32-bit unsigned scalar to a multi-word operand.

```csharp
public void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing temporary buffers and the active CUDA stream.

`leftOperand` _GpuBigUInt_<br>
The multi-word unsigned big integer operand.

`rightOperand` _UInt32_<br>
The 32-bit unsigned scalar addend.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer receiving `leftOperand + rightOperand`.

#### Exceptions

`CudaException`<br>
Thrown when the kernel launch fails.

## Sub(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)

Launches `GpuBigIntScalarKernels.ScalarSubKernel` on a single 256-thread block to subtract a 32-bit unsigned scalar from a multi-word operand.

```csharp
public void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing temporary buffers and the active CUDA stream.

`leftOperand` _GpuBigUInt_<br>
The multi-word unsigned big integer minuend.

`rightOperand` _UInt32_<br>
The 32-bit unsigned scalar subtrahend.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer receiving `leftOperand - rightOperand`.

#### Exceptions

`CudaException`<br>
Thrown when the kernel launch fails.

## BitwiseAnd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Launches `GpuBigIntKernels.BitwiseAndKernel` across a 1D grid computed via `AotGraphHandler.CalculateGridDim`.

```csharp
public void BitwiseAnd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing the active CUDA stream.

`leftOperand` _GpuBigUInt_<br>
The left unsigned big integer operand.

`rightOperand` _GpuBigUInt_<br>
The right unsigned big integer operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer receiving `leftOperand & rightOperand`.

#### Exceptions

`CudaException`<br>
Thrown when the kernel launch fails.

## BitwiseOr(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Launches `GpuBigIntKernels.BitwiseOrKernel` across a 1D grid computed via `AotGraphHandler.CalculateGridDim`.

```csharp
public void BitwiseOr(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing the active CUDA stream.

`leftOperand` _GpuBigUInt_<br>
The left unsigned big integer operand.

`rightOperand` _GpuBigUInt_<br>
The right unsigned big integer operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer receiving `leftOperand | rightOperand`.

#### Exceptions

`CudaException`<br>
Thrown when the kernel launch fails.

## BitwiseXor(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Launches `GpuBigIntKernels.BitwiseXorKernel` across a 1D grid computed via `AotGraphHandler.CalculateGridDim`.

```csharp
public void BitwiseXor(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing the active CUDA stream.

`leftOperand` _GpuBigUInt_<br>
The left unsigned big integer operand.

`rightOperand` _GpuBigUInt_<br>
The right unsigned big integer operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer receiving `leftOperand ^ rightOperand`.

#### Exceptions

`CudaException`<br>
Thrown when the kernel launch fails.

## BitwiseNot(GpuWorkspace, GpuBigUInt, GpuBigUInt)

Launches `GpuBigIntKernels.BitwiseNotKernel` across a 1D grid computed via `AotGraphHandler.CalculateGridDim`.

```csharp
public void BitwiseNot(GpuWorkspace workspace, GpuBigUInt targetOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing the active CUDA stream.

`targetOperand` _GpuBigUInt_<br>
The source unsigned big integer operand to invert.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer receiving `~targetOperand`.

#### Exceptions

`CudaException`<br>
Thrown when the kernel launch fails.

## ShiftLeft(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)

Launches `GpuBigIntKernels.ShiftLeftKernel` with decomposed word offset (`shiftBits / 32`) and intra-word bit offset (`shiftBits % 32`).

```csharp
public void ShiftLeft(GpuWorkspace workspace, GpuBigUInt targetOperand, int shiftBits, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing temporary buffers and the active CUDA stream.

`targetOperand` _GpuBigUInt_<br>
The source unsigned big integer operand. If `targetOperand` and `resultBuffer` alias the same device pointer, `targetOperand` is first copied to a temporary buffer.

`shiftBits` _Int32_<br>
The number of bits to shift left.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer receiving `targetOperand << shiftBits`.

#### Exceptions

`CudaException`<br>
Thrown when the kernel launch or temporary copy fails.

## ShiftRight(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)

Launches `GpuBigIntKernels.ShiftRightKernel` with decomposed word offset (`shiftBits / 32`) and intra-word bit offset (`shiftBits % 32`).

```csharp
public void ShiftRight(GpuWorkspace workspace, GpuBigUInt targetOperand, int shiftBits, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing temporary buffers and the active CUDA stream.

`targetOperand` _GpuBigUInt_<br>
The source unsigned big integer operand. If `targetOperand` and `resultBuffer` alias the same device pointer, `targetOperand` is first copied to a temporary buffer.

`shiftBits` _Int32_<br>
The number of bits to shift right.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer receiving `targetOperand >> shiftBits`.

#### Exceptions

`CudaException`<br>
Thrown when the kernel launch or temporary copy fails.

## Square(GpuWorkspace, IntPtr, Int32)

Computes in-place squaring of a multi-word buffer by wrapping `dataPtr` in a temporary `GpuBigUInt` view and delegating to `GpuBigIntMath.Mul`.

```csharp
public void Square(GpuWorkspace workspace, IntPtr dataPtr, int numWords);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing temporary buffers and the active CUDA stream.

`dataPtr` _IntPtr_<br>
The device pointer to the buffer to square in place.

`numWords` _Int32_<br>
The word capacity of the operand buffer.

#### Exceptions

`CudaException`<br>
Thrown when kernel execution or memory copying fails.

## IsZero(GpuWorkspace, GpuBigUInt)

Evaluates `GpuBigIntCmpKernels.IsZeroKernel` and returns `true` if all words in `targetOperand` are zero.

```csharp
public bool IsZero(GpuWorkspace workspace, GpuBigUInt targetOperand);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing temporary device scalars and the active CUDA stream.

`targetOperand` _GpuBigUInt_<br>
The unsigned big integer operand to test.

#### Returns

_Boolean_<br>
`true` if all words in `targetOperand` are `0`; otherwise, `false`.

#### Exceptions

`CudaException`<br>
Thrown when the kernel launch or device scalar readback fails.

## IsOne(GpuWorkspace, GpuBigUInt)

Evaluates `GpuBigIntCmpKernels.IsOneKernel` and returns `true` if `targetOperand` equals `1`.

```csharp
public bool IsOne(GpuWorkspace workspace, GpuBigUInt targetOperand);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing temporary device scalars and the active CUDA stream.

`targetOperand` _GpuBigUInt_<br>
The unsigned big integer operand to test.

#### Returns

_Boolean_<br>
`true` if the least significant word is `1` and all higher words are `0`; otherwise, `false`.

#### Exceptions

`CudaException`<br>
Thrown when the kernel launch or device scalar readback fails.

## GetBitLength(GpuWorkspace, GpuBigUInt)

Evaluates `GpuBigIntCmpKernels.GetBitLengthKernel` and returns the position of the most significant set bit (`0` when the value is zero).

```csharp
public int GetBitLength(GpuWorkspace workspace, GpuBigUInt targetOperand);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing temporary device scalars and the active CUDA stream.

`targetOperand` _GpuBigUInt_<br>
The unsigned big integer operand to inspect.

#### Returns

_Int32_<br>
The minimum number of bits required to represent `targetOperand`, or `0` if `targetOperand` is zero.

#### Exceptions

`CudaException`<br>
Thrown when the kernel launch or device scalar readback fails.

## CompareTo(GpuWorkspace, GpuBigUInt, GpuBigUInt)

Evaluates `GpuBigIntCmpKernels.CompareToKernel` (zero-padding mismatched word counts if necessary) and returns `-1`, `0`, or `1`.

```csharp
public int CompareTo(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing temporary buffers and the active CUDA stream.

`leftOperand` _GpuBigUInt_<br>
The left unsigned big integer operand.

`rightOperand` _GpuBigUInt_<br>
The right unsigned big integer operand.

#### Returns

_Int32_<br>
`-1` if `leftOperand < rightOperand`, `0` if `leftOperand == rightOperand`, or `1` if `leftOperand > rightOperand`.

#### Exceptions

`CudaException`<br>
Thrown when the kernel launch or device scalar readback fails.

## Remarks

`AotBasicMathHandler` is used internally by [`GSharpAotProvider`](GSharpAotProvider.md) to separate elementary arithmetic kernel launches from memory management and CUDA Graph orchestration.

## Examples

### Instantiating AotBasicMathHandler

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
// Execution Context: Host (CPU)
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

IGpuBasicMathProvider handler = new AotBasicMathHandler();
Console.WriteLine($"Initialized handler: {handler.GetType().Name}");
```

## See also

### BCL API > Math (BigNum)

- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
- [GSharpAotProvider](GSharpAotProvider.md)
- [IGpuExecutionProvider](IGpuExecutionProvider.md)
