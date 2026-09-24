---
title: "IGpuExecutionProvider Interface (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Defines the contract for executing GPU-accelerated BigInteger arithmetic algorithms and managing device memory."
---

## IGpuExecutionProvider Interface

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt.Providers`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Defines the contract for executing GPU-accelerated BigInteger arithmetic algorithms and managing device memory.

```csharp
public interface IGpuExecutionProvider
```

## Methods

| Name                                                                                                                                                           | Description                                                            |
| -------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------- |
| [AllocateMemory(Int32)](#allocatememoryint32)                                                                                                                  | Allocates unmanaged global device memory for the specified word count. |
| [FreeMemory(IntPtr)](#freememoryintptr)                                                                                                                        | Frees the allocated unmanaged global device memory.                    |
| [CopyHostToDevice(IntPtr, UInt32[], Int32)](#copyhosttodeviceintptr-uint32-int32)                                                                              | Copies limb data from host managed memory to GPU device memory.        |
| [CopyDeviceToHost(UInt32[], IntPtr, Int32)](#copydevicetohostuint32-intptr-int32)                                                                              | Copies limb data from GPU device memory to host managed memory.        |
| [CopyDeviceToDevice(IntPtr, IntPtr, Int32)](#copydevicetodeviceintptr-intptr-int32)                                                                            | Copies limb data between two GPU device memory locations.              |
| [Add(GpuContext, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#addgpucontext-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger)               | Computes addition on GPU.                                              |
| [Sub(GpuContext, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#subgpucontext-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger)               | Computes subtraction on GPU.                                           |
| [Mul(GpuContext, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#mulgpucontext-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger)               | Computes multiplication on GPU.                                        |
| [Add(GpuContext, GpuUIntBigInteger, UInt32, GpuUIntBigInteger)](#addgpucontext-gpuuintbiginteger-uint32-gpuuintbiginteger)                                     | Computes scalar addition on GPU.                                       |
| [Sub(GpuContext, GpuUIntBigInteger, UInt32, GpuUIntBigInteger)](#subgpucontext-gpuuintbiginteger-uint32-gpuuintbiginteger)                                     | Computes scalar subtraction on GPU.                                    |
| [Mul(GpuContext, GpuUIntBigInteger, UInt32, GpuUIntBigInteger)](#mulgpucontext-gpuuintbiginteger-uint32-gpuuintbiginteger)                                     | Computes scalar multiplication on GPU.                                 |
| [BitwiseAnd(GpuContext, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#bitwiseandgpucontext-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Computes bitwise AND on GPU.                                           |
| [BitwiseOr(GpuContext, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#bitwiseorgpucontext-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger)   | Computes bitwise OR on GPU.                                            |
| [BitwiseXor(GpuContext, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#bitwisexorgpucontext-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Computes bitwise XOR on GPU.                                           |
| [BitwiseNot(GpuContext, GpuUIntBigInteger, GpuUIntBigInteger)](#bitwisenotgpucontext-gpuuintbiginteger-gpuuintbiginteger)                                      | Computes bitwise NOT on GPU.                                           |
| [ShiftLeft(GpuContext, GpuUIntBigInteger, Int32, GpuUIntBigInteger)](#shiftleftgpucontext-gpuuintbiginteger-int32-gpuuintbiginteger)                           | Computes left bit shift on GPU.                                        |
| [ShiftRight(GpuContext, GpuUIntBigInteger, Int32, GpuUIntBigInteger)](#shiftrightgpucontext-gpuuintbiginteger-int32-gpuuintbiginteger)                         | Computes right bit shift on GPU.                                       |
| [Square(GpuContext, IntPtr, Int32)](#squaregpucontext-intptr-int32)                                                                                            | Computes in-place self squaring on GPU.                                |
| [Gcd(GpuContext, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#gcdgpucontext-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger)               | Computes Greatest Common Divisor on GPU.                               |
| [IsZero(GpuContext, GpuUIntBigInteger)](#iszerogpucontext-gpuuintbiginteger)                                                                                   | Checks whether operand equals zero on GPU.                             |
| [IsOne(GpuContext, GpuUIntBigInteger)](#isonegpucontext-gpuuintbiginteger)                                                                                     | Checks whether operand equals one on GPU.                              |
| [GetBitLength(GpuContext, GpuUIntBigInteger)](#getbitlengthgpucontext-gpuuintbiginteger)                                                                       | Gets the bit length of the operand on GPU.                             |
| [CompareTo(GpuContext, GpuUIntBigInteger, GpuUIntBigInteger)](#comparetogpucontext-gpuuintbiginteger-gpuuintbiginteger)                                        | Compares two operands on GPU.                                          |

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

## Add(GpuContext, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Computes addition on GPU.

```csharp
void Add(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`context` _GpuContext_<br>
The GPU execution context.

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## Sub(GpuContext, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Computes subtraction on GPU.

```csharp
void Sub(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`context` _GpuContext_<br>
The GPU execution context.

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## Mul(GpuContext, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Computes multiplication on GPU.

```csharp
void Mul(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`context` _GpuContext_<br>
The GPU execution context.

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## Add(GpuContext, GpuUIntBigInteger, UInt32, GpuUIntBigInteger)

Computes scalar addition on GPU.

```csharp
void Add(GpuContext context, GpuUIntBigInteger leftOperand, uint rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`context` _GpuContext_<br>
The GPU execution context.

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _UInt32_<br>
The scalar second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## Sub(GpuContext, GpuUIntBigInteger, UInt32, GpuUIntBigInteger)

Computes scalar subtraction on GPU.

```csharp
void Sub(GpuContext context, GpuUIntBigInteger leftOperand, uint rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`context` _GpuContext_<br>
The GPU execution context.

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _UInt32_<br>
The scalar second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## Mul(GpuContext, GpuUIntBigInteger, UInt32, GpuUIntBigInteger)

Computes scalar multiplication on GPU.

```csharp
void Mul(GpuContext context, GpuUIntBigInteger leftOperand, uint rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`context` _GpuContext_<br>
The GPU execution context.

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _UInt32_<br>
The scalar second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## BitwiseAnd(GpuContext, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Computes bitwise AND on GPU.

```csharp
void BitwiseAnd(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`context` _GpuContext_<br>
The GPU execution context.

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## BitwiseOr(GpuContext, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Computes bitwise OR on GPU.

```csharp
void BitwiseOr(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`context` _GpuContext_<br>
The GPU execution context.

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## BitwiseXor(GpuContext, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Computes bitwise XOR on GPU.

```csharp
void BitwiseXor(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`context` _GpuContext_<br>
The GPU execution context.

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## BitwiseNot(GpuContext, GpuUIntBigInteger, GpuUIntBigInteger)

Computes bitwise NOT on GPU.

```csharp
void BitwiseNot(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`context` _GpuContext_<br>
The GPU execution context.

`leftOperand` _GpuUIntBigInteger_<br>
The operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## ShiftLeft(GpuContext, GpuUIntBigInteger, Int32, GpuUIntBigInteger)

Computes left bit shift on GPU.

```csharp
void ShiftLeft(GpuContext context, GpuUIntBigInteger leftOperand, int shiftBits, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`context` _GpuContext_<br>
The GPU execution context.

`leftOperand` _GpuUIntBigInteger_<br>
The operand to shift.

`shiftBits` _Int32_<br>
The number of bits to shift.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## ShiftRight(GpuContext, GpuUIntBigInteger, Int32, GpuUIntBigInteger)

Computes right bit shift on GPU.

```csharp
void ShiftRight(GpuContext context, GpuUIntBigInteger leftOperand, int shiftBits, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`context` _GpuContext_<br>
The GPU execution context.

`leftOperand` _GpuUIntBigInteger_<br>
The operand to shift.

`shiftBits` _Int32_<br>
The number of bits to shift.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## Square(GpuContext, IntPtr, Int32)

Computes in-place self squaring on GPU.

```csharp
bool Square(GpuContext context, IntPtr devicePointer, int logicalWords);
```

#### Parameters

`context` _GpuContext_<br>
The GPU execution context.

`devicePointer` _IntPtr_<br>
The pointer to the device memory containing the operand.

`logicalWords` _Int32_<br>
The logical number of words of the operand.

#### Returns

_Boolean_<br>
`true` if successful; otherwise, `false`.

## Gcd(GpuContext, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Computes Greatest Common Divisor on GPU.

```csharp
void Gcd(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`context` _GpuContext_<br>
The GPU execution context.

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## IsZero(GpuContext, GpuUIntBigInteger)

Checks whether operand equals zero on GPU.

```csharp
bool IsZero(GpuContext context, GpuUIntBigInteger leftOperand);
```

#### Parameters

`context` _GpuContext_<br>
The GPU execution context.

`leftOperand` _GpuUIntBigInteger_<br>
The operand to check.

#### Returns

_Boolean_<br>
`true` if the operand is zero; otherwise, `false`.

## IsOne(GpuContext, GpuUIntBigInteger)

Checks whether operand equals one on GPU.

```csharp
bool IsOne(GpuContext context, GpuUIntBigInteger leftOperand);
```

#### Parameters

`context` _GpuContext_<br>
The GPU execution context.

`leftOperand` _GpuUIntBigInteger_<br>
The operand to check.

#### Returns

_Boolean_<br>
`true` if the operand is one; otherwise, `false`.

## GetBitLength(GpuContext, GpuUIntBigInteger)

Gets the bit length of the operand on GPU.

```csharp
int GetBitLength(GpuContext context, GpuUIntBigInteger leftOperand);
```

#### Parameters

`context` _GpuContext_<br>
The GPU execution context.

`leftOperand` _GpuUIntBigInteger_<br>
The operand.

#### Returns

_Int32_<br>
The bit length.

## CompareTo(GpuContext, GpuUIntBigInteger, GpuUIntBigInteger)

Compares two operands on GPU.

```csharp
int CompareTo(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand);
```

#### Parameters

`context` _GpuContext_<br>
The GPU execution context.

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

#### Returns

_Int32_<br>
A signed integer that indicates the relative values of the operands.

## See also

### BCL API > Math (BigNum)

- [GPU Big Integer and Unsigned Integer](../00_index.md)
