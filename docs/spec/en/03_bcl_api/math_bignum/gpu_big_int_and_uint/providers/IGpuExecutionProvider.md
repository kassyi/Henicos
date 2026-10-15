---
title: "IGpuExecutionProvider Interface (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Defines the contract for executing GPU-accelerated BigInteger arithmetic algorithms and managing device memory."
---

## IGpuExecutionProvider Interface

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

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
| [CopyDeviceToHost(UInt32[], IntPtr, Int32, IntPtr)](#copydevicetohostuint32-intptr-int32-intptr)                                                               | Copies limb data from GPU device memory to host managed memory and waits for it. |
| [CopyDeviceToHostAsync(Memory&lt;UInt32&gt;, IntPtr, Int32, IntPtr, CancellationToken)](#copydevicetohostasyncmemoryuint32-intptr-int32-intptr-cancellationtoken) | Asynchronously copies limb data to host memory without blocking the calling thread. |
| [SynchronizeStreamAsync(IntPtr, CancellationToken)](#synchronizestreamasyncintptr-cancellationtoken)                                                           | Asynchronously waits until all work on a CUDA stream has completed.    |
| [CopyDeviceToDevice(IntPtr, IntPtr, Int32)](#copydevicetodeviceintptr-intptr-int32)                                                                            | Copies limb data between two GPU device memory locations.              |
| [Add(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#addgpuworkspace-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger)               | Computes addition on GPU.                                              |
| [Sub(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#subgpuworkspace-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger)               | Computes subtraction on GPU.                                           |
| [Mul(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#mulgpuworkspace-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger)               | Computes multiplication on GPU.                                        |
| [Add(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)](#addgpuworkspace-gpuuintbiginteger-uint32-gpuuintbiginteger)                                     | Computes scalar addition on GPU.                                       |
| [Sub(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)](#subgpuworkspace-gpuuintbiginteger-uint32-gpuuintbiginteger)                                     | Computes scalar subtraction on GPU.                                    |
| [Mul(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)](#mulgpuworkspace-gpuuintbiginteger-uint32-gpuuintbiginteger)                                     | Computes scalar multiplication on GPU.                                 |
| [BitwiseAnd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwiseandgpuworkspace-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Computes bitwise AND on GPU.                                           |
| [BitwiseOr(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwiseorgpuworkspace-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger)   | Computes bitwise OR on GPU.                                            |
| [BitwiseXor(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwisexorgpuworkspace-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Computes bitwise XOR on GPU.                                           |
| [BitwiseNot(GpuWorkspace, GpuBigUInt, GpuBigUInt)](#bitwisenotgpuworkspace-gpuuintbiginteger-gpuuintbiginteger)                                      | Computes bitwise NOT on GPU.                                           |
| [ShiftLeft(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)](#shiftleftgpuworkspace-gpuuintbiginteger-int32-gpuuintbiginteger)                           | Computes left bit shift on GPU.                                        |
| [ShiftRight(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)](#shiftrightgpuworkspace-gpuuintbiginteger-int32-gpuuintbiginteger)                         | Computes right bit shift on GPU.                                       |
| [Square(GpuWorkspace, IntPtr, Int32)](#squaregpuworkspace-intptr-int32)                                                                                            | Computes in-place self squaring on GPU.                                |
| [Gcd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#gcdgpuworkspace-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger)               | Computes Greatest Common Divisor on GPU.                               |
| [IsZero(GpuWorkspace, GpuBigUInt)](#iszerogpuworkspace-gpuuintbiginteger)                                                                                   | Checks whether operand equals zero on GPU.                             |
| [IsOne(GpuWorkspace, GpuBigUInt)](#isonegpuworkspace-gpuuintbiginteger)                                                                                     | Checks whether operand equals one on GPU.                              |
| [GetBitLength(GpuWorkspace, GpuBigUInt)](#getbitlengthgpuworkspace-gpuuintbiginteger)                                                                       | Gets the bit length of the operand on GPU.                             |
| [CompareTo(GpuWorkspace, GpuBigUInt, GpuBigUInt)](#comparetogpuworkspace-gpuuintbiginteger-gpuuintbiginteger)                                        | Compares two operands on GPU.                                          |

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

> [!NOTE]
> Signatures on this page that omit the trailing `stream` parameter predate the stream-ordered memory work (Issue #317) and are pending a full resync. The three members below reflect the current interface (`IGpuMemoryProvider`, which `IGpuExecutionProvider` inherits).

## CopyDeviceToHost(UInt32[], IntPtr, Int32, IntPtr)

Copies limb data from GPU device memory to host managed memory and waits until the data has arrived.

```csharp
void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords, IntPtr stream = 0);
```

#### Parameters

`hostData` _UInt32[]_<br>
The destination array on the host.

`devicePointer` _IntPtr_<br>
The source pointer on the device.

`numWords` _Int32_<br>
The number of 32-bit words to copy.

`stream` _IntPtr_<br>
The stream the copy is ordered on; the copy observes all work enqueued on it before the call.

> [!IMPORTANT]
> This method is synchronous: `hostData` holds the data when it returns, whether the array is pageable or page-locked, so callers need no additional stream synchronization. It blocks the calling thread until the stream drains; use [CopyDeviceToHostAsync](#copydevicetohostasyncmemoryuint32-intptr-int32-intptr-cancellationtoken) to avoid that.

## CopyDeviceToHostAsync(Memory&lt;UInt32&gt;, IntPtr, Int32, IntPtr, CancellationToken)

Asynchronously copies limb data from GPU device memory to host memory without blocking the calling thread.

```csharp
ValueTask CopyDeviceToHostAsync(Memory<uint> hostData, IntPtr devicePointer, int numWords, IntPtr stream, CancellationToken cancellationToken = default);
```

#### Parameters

`hostData` _Memory&lt;UInt32&gt;_<br>
The destination host memory, at least `numWords` words long.

`devicePointer` _IntPtr_<br>
The source pointer on the device.

`numWords` _Int32_<br>
The number of 32-bit words to copy.

`stream` _IntPtr_<br>
The stream the copy is ordered on; the copy observes all work enqueued on it before the call.

`cancellationToken` _CancellationToken_<br>
A token that detaches the awaiter. Device work is not canceled, and `hostData` is not written after cancellation.

#### Returns

_ValueTask_<br>
A task that completes once `hostData` holds the data.

#### Exceptions

`ArgumentOutOfRangeException`<br>
`numWords` exceeds the length of `hostData`.

`OperationCanceledException`<br>
`cancellationToken` was canceled.

> [!NOTE]
> The GSharpAot provider stages the copy through a pooled page-locked buffer and signals completion from the driver without parking a thread. The CPU and native providers complete synchronously.

## SynchronizeStreamAsync(IntPtr, CancellationToken)

Asynchronously waits until all previously submitted work in the specified CUDA stream has completed, without blocking the calling thread.

```csharp
ValueTask SynchronizeStreamAsync(IntPtr stream, CancellationToken cancellationToken = default);
```

#### Parameters

`stream` _IntPtr_<br>
The unmanaged stream handle to wait for. `IntPtr.Zero` completes immediately, like `SynchronizeStream`.

`cancellationToken` _CancellationToken_<br>
A token that detaches the awaiter; the stream keeps executing and remains usable.

#### Returns

_ValueTask_<br>
A task that completes when the stream has drained.

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

## Add(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes addition on GPU.

```csharp
void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
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

## Sub(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes subtraction on GPU.

```csharp
void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
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

## Mul(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes multiplication on GPU.

```csharp
void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
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

## Add(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)

Computes scalar addition on GPU.

```csharp
void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
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

## Sub(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)

Computes scalar subtraction on GPU.

```csharp
void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
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

## Mul(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)

Computes scalar multiplication on GPU.

```csharp
void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
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

## BitwiseAnd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes bitwise AND on GPU.

```csharp
void BitwiseAnd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
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

## BitwiseOr(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes bitwise OR on GPU.

```csharp
void BitwiseOr(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
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

## BitwiseXor(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes bitwise XOR on GPU.

```csharp
void BitwiseXor(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
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

## BitwiseNot(GpuWorkspace, GpuBigUInt, GpuBigUInt)

Computes bitwise NOT on GPU.

```csharp
void BitwiseNot(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuUIntBigInteger_<br>
The operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## ShiftLeft(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)

Computes left bit shift on GPU.

```csharp
void ShiftLeft(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);
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

## ShiftRight(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)

Computes right bit shift on GPU.

```csharp
void ShiftRight(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);
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

## Gcd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes Greatest Common Divisor on GPU.

```csharp
void Gcd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
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

## IsZero(GpuWorkspace, GpuBigUInt)

Checks whether operand equals zero on GPU.

```csharp
bool IsZero(GpuWorkspace workspace, GpuBigUInt leftOperand);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuUIntBigInteger_<br>
The operand to check.

#### Returns

_Boolean_<br>
`true` if the operand is zero; otherwise, `false`.

## IsOne(GpuWorkspace, GpuBigUInt)

Checks whether operand equals one on GPU.

```csharp
bool IsOne(GpuWorkspace workspace, GpuBigUInt leftOperand);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuUIntBigInteger_<br>
The operand to check.

#### Returns

_Boolean_<br>
`true` if the operand is one; otherwise, `false`.

## GetBitLength(GpuWorkspace, GpuBigUInt)

Gets the bit length of the operand on GPU.

```csharp
int GetBitLength(GpuWorkspace workspace, GpuBigUInt leftOperand);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuUIntBigInteger_<br>
The operand.

#### Returns

_Int32_<br>
The bit length.

## CompareTo(GpuWorkspace, GpuBigUInt, GpuBigUInt)

Compares two operands on GPU.

```csharp
int CompareTo(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand);
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
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Runtime, Kassyi.GSharp.Stdlib.GpuMath

```csharp
// Execution Context: Host (CPU)
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

using var workspace = new GpuWorkspace(maxWords: 1024, engine: GpuExecutionEngine.GSharpAot);
IGpuExecutionProvider provider = workspace.Provider;

using var a = new GpuBigUInt(workspace, new uint[] { 100 });
using var b = new GpuBigUInt(workspace, new uint[] { 50 });
using var result = new GpuBigUInt(workspace, 1);

provider.BitwiseOr(workspace, a, b, result);

uint[] hostResult = new uint[1];
provider.CopyDeviceToHost(hostResult, result.DevicePointer, 1);

// 100 = 0x64, 50 = 0x32, 100 | 50 = 118 (0x76)
Console.WriteLine($"100 | 50 = {hostResult[0]}");
```
