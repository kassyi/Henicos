---
title: "NativeProvider Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Execution provider implementing arbitrary-precision arithmetic via pre-compiled native CUDA C++ dynamic libraries."
---

## NativeProvider Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt.Providers`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Execution provider implementing arbitrary-precision arithmetic via pre-compiled native CUDA C++ dynamic libraries.

```csharp
public class NativeProvider : IGpuExecutionProvider
```

## Methods

| Name | Description |
| ---- | ----------- |
| [AllocateMemory(Int32)](#allocatememoryint32) | Allocates unmanaged global device memory for the specified word count. |
| [FreeMemory(IntPtr)](#freememoryintptr) | Frees the allocated unmanaged global device memory. |
| [CopyHostToDevice(IntPtr, UInt32[], Int32)](#copyhosttodeviceintptr-uint32-int32) | Copies limb data from host managed memory to GPU device memory. |
| [CopyDeviceToHost(UInt32[], IntPtr, Int32)](#copydevicetohostuint32-intptr-int32) | Copies limb data from GPU device memory to host managed memory. |
| [CopyDeviceToDevice(IntPtr, IntPtr, Int32)](#copydevicetodeviceintptr-intptr-int32) | Copies limb data between two GPU device memory locations. |
| [Add(GpuWorkspace, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#addgpuworkspace-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Computes addition on GPU. |
| [Sub(GpuWorkspace, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#subgpuworkspace-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Computes subtraction on GPU. |
| [Mul(GpuWorkspace, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#mulgpuworkspace-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Computes multiplication on GPU. |
| [Add(GpuWorkspace, GpuUIntBigInteger, UInt32, GpuUIntBigInteger)](#addgpuworkspace-gpuuintbiginteger-uint32-gpuuintbiginteger) | Computes scalar addition on GPU. |
| [Sub(GpuWorkspace, GpuUIntBigInteger, UInt32, GpuUIntBigInteger)](#subgpuworkspace-gpuuintbiginteger-uint32-gpuuintbiginteger) | Computes scalar subtraction on GPU. |
| [Mul(GpuWorkspace, GpuUIntBigInteger, UInt32, GpuUIntBigInteger)](#mulgpuworkspace-gpuuintbiginteger-uint32-gpuuintbiginteger) | Computes scalar multiplication on GPU. |
| [BitwiseAnd(GpuWorkspace, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#bitwiseandgpuworkspace-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Computes bitwise AND on GPU. |
| [BitwiseOr(GpuWorkspace, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#bitwiseorgpuworkspace-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Computes bitwise OR on GPU. |
| [BitwiseXor(GpuWorkspace, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#bitwisexorgpuworkspace-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Computes bitwise XOR on GPU. |
| [BitwiseNot(GpuWorkspace, GpuUIntBigInteger, GpuUIntBigInteger)](#bitwisenotgpuworkspace-gpuuintbiginteger-gpuuintbiginteger) | Computes bitwise NOT on GPU. |
| [ShiftLeft(GpuWorkspace, GpuUIntBigInteger, Int32, GpuUIntBigInteger)](#shiftleftgpuworkspace-gpuuintbiginteger-int32-gpuuintbiginteger) | Computes left bit shift on GPU. |
| [ShiftRight(GpuWorkspace, GpuUIntBigInteger, Int32, GpuUIntBigInteger)](#shiftrightgpuworkspace-gpuuintbiginteger-int32-gpuuintbiginteger) | Computes right bit shift on GPU. |
| [Square(GpuWorkspace, IntPtr, Int32)](#squaregpuworkspace-intptr-int32) | Computes in-place self squaring on GPU. |
| [Gcd(GpuWorkspace, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#gcdgpuworkspace-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Computes Greatest Common Divisor on GPU. |
| [IsZero(GpuWorkspace, GpuUIntBigInteger)](#iszerogpuworkspace-gpuuintbiginteger) | Checks whether operand equals zero on GPU. |
| [IsOne(GpuWorkspace, GpuUIntBigInteger)](#isonegpuworkspace-gpuuintbiginteger) | Checks whether operand equals one on GPU. |
| [GetBitLength(GpuWorkspace, GpuUIntBigInteger)](#getbitlengthgpuworkspace-gpuuintbiginteger) | Gets the bit length of the operand on GPU. |
| [CompareTo(GpuWorkspace, GpuUIntBigInteger, GpuUIntBigInteger)](#comparetogpuworkspace-gpuuintbiginteger-gpuuintbiginteger) | Compares two operands on GPU. |

## AllocateMemory(Int32)

Allocates unmanaged global device memory for the specified word count.

```csharp
IntPtr AllocateMemory(int numWords);
```

#### Parameters

`numWords` _Int32_<br>
The number of 32-bit words to allocate.

#### Returns

_IntPtr_<br>
A pointer to the allocated device memory.

## FreeMemory(IntPtr)

Frees the allocated unmanaged global device memory.

```csharp
void FreeMemory(IntPtr devicePointer);
```

#### Parameters

`devicePointer` _IntPtr_<br>
The pointer to the device memory to free.

## CopyHostToDevice(IntPtr, UInt32[], Int32)

Copies limb data from host managed memory to GPU device memory.

```csharp
void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords);
```

#### Parameters

`devicePointer` _IntPtr_<br>
The destination pointer on the device.

`hostData` _UInt32[]_<br>
The source data array on the host.

`numWords` _Int32_<br>
The number of 32-bit words to copy.

#### Exceptions

`InvalidOperationException`<br>
Thrown when copying data from host managed memory to GPU device memory fails.

## CopyDeviceToHost(UInt32[], IntPtr, Int32)

Copies limb data from GPU device memory to host managed memory.

```csharp
void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords);
```

#### Parameters

`hostData` _UInt32[]_<br>
The destination array on the host.

`devicePointer` _IntPtr_<br>
The source pointer on the device.

`numWords` _Int32_<br>
The number of 32-bit words to copy.

#### Exceptions

`InvalidOperationException`<br>
Thrown when copying data from GPU device memory to host managed memory fails.

## CopyDeviceToDevice(IntPtr, IntPtr, Int32)

Copies limb data between two GPU device memory locations.

```csharp
void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords);
```

#### Parameters

`destPointer` _IntPtr_<br>
The destination pointer on the device.

`srcPointer` _IntPtr_<br>
The source pointer on the device.

`numWords` _Int32_<br>
The number of 32-bit words to copy.

#### Exceptions

`InvalidOperationException`<br>
Thrown when copying data between device memory locations fails.

## Add(GpuWorkspace, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Computes addition on GPU.

```csharp
void Add(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## Sub(GpuWorkspace, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Computes subtraction on GPU.

```csharp
void Sub(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## Mul(GpuWorkspace, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Computes multiplication on GPU.

```csharp
void Mul(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## Add(GpuWorkspace, GpuUIntBigInteger, UInt32, GpuUIntBigInteger)

Computes scalar addition on GPU.

```csharp
void Add(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, uint rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _UInt32_<br>
The scalar second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## Sub(GpuWorkspace, GpuUIntBigInteger, UInt32, GpuUIntBigInteger)

Computes scalar subtraction on GPU.

```csharp
void Sub(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, uint rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _UInt32_<br>
The scalar second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## Mul(GpuWorkspace, GpuUIntBigInteger, UInt32, GpuUIntBigInteger)

Computes scalar multiplication on GPU.

```csharp
void Mul(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, uint rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _UInt32_<br>
The scalar second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## BitwiseAnd(GpuWorkspace, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Computes bitwise AND on GPU.

```csharp
void BitwiseAnd(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## BitwiseOr(GpuWorkspace, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Computes bitwise OR on GPU.

```csharp
void BitwiseOr(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## BitwiseXor(GpuWorkspace, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Computes bitwise XOR on GPU.

```csharp
void BitwiseXor(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## BitwiseNot(GpuWorkspace, GpuUIntBigInteger, GpuUIntBigInteger)

Computes bitwise NOT on GPU.

```csharp
void BitwiseNot(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuUIntBigInteger_<br>
The operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## ShiftLeft(GpuWorkspace, GpuUIntBigInteger, Int32, GpuUIntBigInteger)

Computes left bit shift on GPU.

```csharp
void ShiftLeft(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, int shiftBits, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuUIntBigInteger_<br>
The operand to shift.

`shiftBits` _Int32_<br>
The number of bits to shift.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## ShiftRight(GpuWorkspace, GpuUIntBigInteger, Int32, GpuUIntBigInteger)

Computes right bit shift on GPU.

```csharp
void ShiftRight(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, int shiftBits, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuUIntBigInteger_<br>
The operand to shift.

`shiftBits` _Int32_<br>
The number of bits to shift.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## Square(GpuWorkspace, IntPtr, Int32)

Computes in-place self squaring on GPU.

```csharp
bool Square(GpuWorkspace workspace, IntPtr devicePointer, int logicalWords);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`devicePointer` _IntPtr_<br>
The pointer to the device memory containing the operand.

`logicalWords` _Int32_<br>
The logical number of words of the operand.

#### Returns

_Boolean_<br>
`true` if successful; otherwise, `false`.

## Gcd(GpuWorkspace, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Computes Greatest Common Divisor on GPU.

```csharp
void Gcd(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## IsZero(GpuWorkspace, GpuUIntBigInteger)

Checks whether operand equals zero on GPU.

```csharp
bool IsZero(GpuWorkspace workspace, GpuUIntBigInteger leftOperand);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuUIntBigInteger_<br>
The operand to check.

#### Returns

_Boolean_<br>
`true` if the operand is zero; otherwise, `false`.

## IsOne(GpuWorkspace, GpuUIntBigInteger)

Checks whether operand equals one on GPU.

```csharp
bool IsOne(GpuWorkspace workspace, GpuUIntBigInteger leftOperand);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuUIntBigInteger_<br>
The operand to check.

#### Returns

_Boolean_<br>
`true` if the operand is one; otherwise, `false`.

## GetBitLength(GpuWorkspace, GpuUIntBigInteger)

Gets the bit length of the operand on GPU.

```csharp
int GetBitLength(GpuWorkspace workspace, GpuUIntBigInteger leftOperand);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuUIntBigInteger_<br>
The operand.

#### Returns

_Int32_<br>
The bit length.

## CompareTo(GpuWorkspace, GpuUIntBigInteger, GpuUIntBigInteger)

Compares two operands on GPU.

```csharp
int CompareTo(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

#### Returns

_Int32_<br>
A signed integer that indicates the relative values of the operands.

## Remarks

No remarks available.

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuWorkspace](../../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)

### Architecture

- [2. Compiler and Runtime Architecture](../../../../02_architecture/00_index.md)
- [1. Generator Architecture (`Kassyi.GSharp.Compiler.Generators`)](../../../../02_architecture/01_aot_transpiler.md)


## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Runtime, Kassyi.GSharp.Stdlib.Math

```csharp
// Execution Context: Host (CPU)
using System;
using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.BigInt;
using Kassyi.GSharp.Stdlib.Math.BigInt.Providers;

using var workspace = new GpuWorkspace(maxWords: 1024, engine: GpuExecutionEngine.LegacyNative);
IGpuExecutionProvider provider = workspace.Provider;

using var a = new GpuUIntBigInteger(workspace, new uint[] { 100 });
using var b = new GpuUIntBigInteger(workspace, new uint[] { 50 });
using var result = new GpuUIntBigInteger(workspace, 1);

provider.BitwiseOr(workspace, a, b, result);

uint[] hostResult = new uint[1];
provider.CopyDeviceToHost(hostResult, result.DevicePointer, 1);

// 100 = 0x64, 50 = 0x32, 100 | 50 = 118 (0x76)
Console.WriteLine($"100 | 50 = {hostResult[0]}");
```
