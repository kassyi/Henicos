---
title: "NativeProvider Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Execution provider implementing arbitrary-precision arithmetic via pre-compiled native CUDA C++ dynamic libraries."
---

## NativeProvider Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Execution provider implementing arbitrary-precision arithmetic via pre-compiled native CUDA C++ dynamic libraries (`Kassyi.GSharp.GpuBigInt.Cuda`).

```csharp
[ExcludeFromCodeCoverage]
public class NativeProvider : IGpuExecutionProvider
```

## Fields

| Name | Description |
| --- | --- |
| [Instance](#instance) | Gets the singleton instance of the `NativeProvider`. |

## Instance

Gets the singleton instance of the `NativeProvider`.

```csharp
public static readonly NativeProvider Instance;
```

## Methods

| Name | Description |
| --- | --- |
| [Add(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#addgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes addition via the legacy native library. |
| [Add(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)](#addgpuworkspace-gpubiguint-uint32-gpubiguint) | Throws `NotImplementedException` on the native backend. |
| [AllocateMemory(Int32)](#allocatememoryint32) | Allocates unmanaged global device memory for the specified word count. |
| [BeginCapture(IntPtr)](#begincaptureintptr) | Throws `NotImplementedException` on the native backend. |
| [BitwiseAnd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwiseandgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes bitwise AND via the legacy native library. |
| [BitwiseNot(GpuWorkspace, GpuBigUInt, GpuBigUInt)](#bitwisenotgpuworkspace-gpubiguint-gpubiguint) | Throws `NotImplementedException` on the native backend. |
| [BitwiseOr(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwiseorgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Throws `NotImplementedException` on the native backend. |
| [BitwiseXor(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwisexorgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Throws `NotImplementedException` on the native backend. |
| [ClearMemoryAsync(IntPtr, Int32, IntPtr)](#clearmemoryasyncintptr-int32-intptr) | No-op on the legacy native provider. |
| [CompareTo(GpuWorkspace, GpuBigUInt, GpuBigUInt)](#comparetogpuworkspace-gpubiguint-gpubiguint) | Throws `NotImplementedException` on the native backend. |
| [CopyDeviceToDevice(IntPtr, IntPtr, Int32, IntPtr)](#copydevicetodeviceintptr-intptr-int32-intptr) | Copies limb data between two GPU device memory locations. |
| [CopyDeviceToHost(UInt32[], IntPtr, Int32, IntPtr)](#copydevicetohostuint32-intptr-int32-intptr) | Synchronously copies limb data from GPU device memory to host managed memory. |
| [CopyDeviceToHostAsync(Memory&lt;UInt32&gt;, IntPtr, Int32, IntPtr, CancellationToken)](#copydevicetohostasyncmemoryuint32-intptr-int32-intptr-cancellationtoken) | Copies limb data from GPU device memory to host memory and returns a completed `ValueTask`. |
| [CopyHostToDevice(IntPtr, UInt32[], Int32, IntPtr)](#copyhosttodeviceintptr-uint32-int32-intptr) | Synchronously copies limb data from host managed memory to GPU device memory. |
| [DestroyGraph(IntPtr, IntPtr)](#destroygraphintptr-intptr) | Throws `NotImplementedException` on the native backend. |
| [EndCapture(IntPtr)](#endcaptureintptr) | Throws `NotImplementedException` on the native backend. |
| [FreeMemory(IntPtr, IntPtr)](#freememoryintptr-intptr) | Synchronously frees allocated unmanaged global device memory (`stream` is ignored). |
| [Gcd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#gcdgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Throws `NotImplementedException` on the native backend. |
| [GetBitLength(GpuWorkspace, GpuBigUInt)](#getbitlengthgpuworkspace-gpubiguint) | Copies limbs to host and computes the bit length of the operand. |
| [InstantiateGraph(IntPtr)](#instantiategraphintptr) | Throws `NotImplementedException` on the native backend. |
| [InverseSqrtMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, Int32, Int32, Int32)](#inversesqrtmegagpuworkspace-gpubiguint-gpubiguint-int32-int32-int32) | Throws `NotImplementedException` on the native backend. |
| [IsOne(GpuWorkspace, GpuBigUInt)](#isonegpuworkspace-gpubiguint) | Throws `NotImplementedException` on the native backend. |
| [IsZero(GpuWorkspace, GpuBigUInt)](#iszerogpuworkspace-gpubiguint) | Copies limbs to host and checks whether all words equal zero. |
| [LaunchGraph(IntPtr, IntPtr)](#launchgraphintptr-intptr) | Throws `NotImplementedException` on the native backend. |
| [ModInverse(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#modinversegpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Throws `NotImplementedException` on the native backend. |
| [Mul(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#mulgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Throws `NotImplementedException` on the native backend. |
| [Mul(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)](#mulgpuworkspace-gpubiguint-uint32-gpubiguint) | Throws `NotImplementedException` on the native backend. |
| [ReadWord(IntPtr, Int32, IntPtr)](#readwordintptr-int32-intptr) | Synchronously reads a single 32-bit word from device memory at `wordOffset`. |
| [ReciprocalMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, Int32, Int32, Int32, Int32, Int32)](#reciprocalmegagpuworkspace-gpubiguint-gpubiguint-int32-int32-int32-int32-int32) | Throws `NotImplementedException` on the native backend. |
| [ShiftLeft(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)](#shiftleftgpuworkspace-gpubiguint-int32-gpubiguint) | Computes left bit shift via the legacy native library. |
| [ShiftRight(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)](#shiftrightgpuworkspace-gpubiguint-int32-gpubiguint) | Computes right bit shift via the legacy native library. |
| [Square(GpuWorkspace, IntPtr, Int32)](#squaregpuworkspace-intptr-int32) | Computes in-place self squaring via the legacy native library. |
| [Sub(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#subgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes subtraction via the legacy native library. |
| [Sub(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)](#subgpuworkspace-gpubiguint-uint32-gpubiguint) | Throws `NotImplementedException` on the native backend. |
| [SynchronizeStream(IntPtr)](#synchronizestreamintptr) | No-op for legacy native synchronous calls. |
| [SynchronizeStreamAsync(IntPtr, CancellationToken)](#synchronizestreamasyncintptr-cancellationtoken) | Returns a completed `ValueTask` (or canceled if `cancellationToken` is canceled). |
| [WriteWord(IntPtr, Int32, UInt32, IntPtr)](#writewordintptr-int32-uint32-intptr) | Synchronously writes a single 32-bit word to device memory at `wordOffset`. |

## AllocateMemory(Int32)

Allocates unmanaged global device memory for the specified word count via `NativeMethods.GpuBigInt_Alloc`.

```csharp
public IntPtr AllocateMemory(int numWords);
```

#### Parameters

`numWords` _Int32_<br>
The number of 32-bit words to allocate.

#### Returns

_IntPtr_<br>
A pointer to the allocated device memory.

## FreeMemory(IntPtr, IntPtr)

Synchronously frees the allocated unmanaged global device memory (`stream` is ignored).

```csharp
public void FreeMemory(IntPtr devicePointer, IntPtr stream = 0);
```

#### Parameters

`devicePointer` _IntPtr_<br>
The pointer to the device memory to free.

`stream` _IntPtr_<br>
The CUDA stream handle (ignored by the legacy native library).

## ClearMemoryAsync(IntPtr, Int32, IntPtr)

No-op on the legacy native provider.

```csharp
public void ClearMemoryAsync(IntPtr devicePointer, int numWords, IntPtr stream);
```

#### Parameters

`devicePointer` _IntPtr_<br>
The unmanaged pointer to the starting device memory address.

`numWords` _Int32_<br>
The number of 32-bit words to clear.

`stream` _IntPtr_<br>
The unmanaged CUDA stream handle.

## CopyHostToDevice(IntPtr, UInt32[], Int32, IntPtr)

Synchronously copies limb data from host managed memory to GPU device memory (`stream` is ignored).

```csharp
public void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords, IntPtr stream = 0);
```

#### Parameters

`devicePointer` _IntPtr_<br>
The destination pointer on the device.

`hostData` _UInt32[]_<br>
The source data array on the host.

`numWords` _Int32_<br>
The number of 32-bit words to copy.

`stream` _IntPtr_<br>
The optional CUDA stream handle (ignored by the legacy native library).

#### Exceptions

`InvalidOperationException`<br>
Thrown when copying data from host managed memory to GPU device memory fails.

## CopyDeviceToHost(UInt32[], IntPtr, Int32, IntPtr)

Synchronously copies limb data from GPU device memory to host managed memory (`stream` is ignored).

```csharp
public void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords, IntPtr stream = 0);
```

#### Parameters

`hostData` _UInt32[]_<br>
The destination array on the host.

`devicePointer` _IntPtr_<br>
The source pointer on the device.

`numWords` _Int32_<br>
The number of 32-bit words to copy.

`stream` _IntPtr_<br>
The optional CUDA stream handle (ignored by the legacy native library).

#### Exceptions

`InvalidOperationException`<br>
Thrown when copying data from GPU device memory to host managed memory fails.

## CopyDeviceToHostAsync(Memory&lt;UInt32&gt;, IntPtr, Int32, IntPtr, CancellationToken)

Copies limb data from GPU device memory to `hostData` synchronously and returns a completed `ValueTask`.

```csharp
public ValueTask CopyDeviceToHostAsync(
    Memory<uint> hostData,
    IntPtr devicePointer,
    int numWords,
    IntPtr stream,
    CancellationToken cancellationToken = default);
```

#### Parameters

`hostData` _Memory&lt;UInt32&gt;_<br>
The destination host memory, at least `numWords` words long.

`devicePointer` _IntPtr_<br>
The source device memory pointer.

`numWords` _Int32_<br>
The number of 32-bit words to copy.

`stream` _IntPtr_<br>
The stream handle (ignored by the legacy native library).

`cancellationToken` _CancellationToken_<br>
A token checked before executing the synchronous copy.

#### Returns

_ValueTask_<br>
A completed `ValueTask` once `hostData` holds the data, or a canceled `ValueTask` if `cancellationToken` was already canceled.

#### Exceptions

`ArgumentOutOfRangeException`<br>
`numWords` exceeds `hostData.Length`.

`InvalidOperationException`<br>
Thrown when the underlying synchronous device-to-host copy fails.

## CopyDeviceToDevice(IntPtr, IntPtr, Int32, IntPtr)

Synchronously copies limb data between two GPU device memory locations (`stream` is ignored).

```csharp
public void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords, IntPtr stream = 0);
```

#### Parameters

`destPointer` _IntPtr_<br>
The destination pointer on the device.

`srcPointer` _IntPtr_<br>
The source pointer on the device.

`numWords` _Int32_<br>
The number of 32-bit words to copy.

`stream` _IntPtr_<br>
The optional CUDA stream handle (ignored by the legacy native library).

#### Exceptions

`InvalidOperationException`<br>
Thrown when copying data between device memory locations fails.

## ReadWord(IntPtr, Int32, IntPtr)

Synchronously reads a single 32-bit word from device memory at `wordOffset` (`stream` is ignored).

```csharp
public uint ReadWord(IntPtr devicePointer, int wordOffset = 0, IntPtr stream = 0);
```

#### Parameters

`devicePointer` _IntPtr_<br>
The base device memory pointer.

`wordOffset` _Int32_<br>
The zero-based 32-bit word offset.

`stream` _IntPtr_<br>
The optional CUDA stream handle (ignored by the legacy native library).

#### Returns

_UInt32_<br>
The 32-bit unsigned integer read from the device.

#### Exceptions

`InvalidOperationException`<br>
Thrown when copying the word from device memory fails.

## WriteWord(IntPtr, Int32, UInt32, IntPtr)

Synchronously writes a single 32-bit word to device memory at `wordOffset` (`stream` is ignored).

```csharp
public void WriteWord(IntPtr devicePointer, int wordOffset, uint value, IntPtr stream = 0);
```

#### Parameters

`devicePointer` _IntPtr_<br>
The base device memory pointer.

`wordOffset` _Int32_<br>
The zero-based 32-bit word offset.

`value` _UInt32_<br>
The 32-bit unsigned integer value to write.

`stream` _IntPtr_<br>
The optional CUDA stream handle (ignored by the legacy native library).

#### Exceptions

`InvalidOperationException`<br>
Thrown when copying the word to device memory fails.

## SynchronizeStream(IntPtr)

No-op because legacy native calls execute synchronously.

```csharp
public void SynchronizeStream(IntPtr stream);
```

#### Parameters

`stream` _IntPtr_<br>
The unmanaged stream handle (ignored).

## SynchronizeStreamAsync(IntPtr, CancellationToken)

Returns `ValueTask.CompletedTask` unless `cancellationToken.IsCancellationRequested` is `true`.

```csharp
public ValueTask SynchronizeStreamAsync(IntPtr stream, CancellationToken cancellationToken = default);
```

#### Parameters

`stream` _IntPtr_<br>
The unmanaged stream handle (ignored).

`cancellationToken` _CancellationToken_<br>
A cancellation token checked before returning.

#### Returns

_ValueTask_<br>
A completed `ValueTask`, or a canceled `ValueTask` if `cancellationToken` is canceled.

## Add(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes addition on GPU via `NativeMethods.GpuBigInt_Add`.

```csharp
public void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuBigUInt_<br>
The first operand.

`rightOperand` _GpuBigUInt_<br>
The second operand.

`resultBuffer` _GpuBigUInt_<br>
The buffer to store the result.

## Sub(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes subtraction on GPU via `NativeMethods.GpuBigInt_Sub`.

```csharp
public void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuBigUInt_<br>
The first operand.

`rightOperand` _GpuBigUInt_<br>
The second operand.

`resultBuffer` _GpuBigUInt_<br>
The buffer to store the result.

## Add(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)

Throws `NotImplementedException` on the native backend.

```csharp
public void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuBigUInt_<br>
The first operand.

`rightOperand` _UInt32_<br>
The scalar second operand.

`resultBuffer` _GpuBigUInt_<br>
The buffer to store the result.

#### Exceptions

`NotImplementedException`<br>
Always thrown. Not supported on the native backend.

## Sub(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)

Throws `NotImplementedException` on the native backend.

```csharp
public void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuBigUInt_<br>
The first operand.

`rightOperand` _UInt32_<br>
The scalar second operand.

`resultBuffer` _GpuBigUInt_<br>
The buffer to store the result.

#### Exceptions

`NotImplementedException`<br>
Always thrown. Not supported on the native backend.

## BitwiseAnd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes bitwise AND on GPU via `NativeMethods.GpuBigInt_BitwiseAnd`.

```csharp
public void BitwiseAnd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuBigUInt_<br>
The first operand.

`rightOperand` _GpuBigUInt_<br>
The second operand.

`resultBuffer` _GpuBigUInt_<br>
The buffer to store the result.

## BitwiseOr(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Throws `NotImplementedException` on the native backend.

```csharp
public void BitwiseOr(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuBigUInt_<br>
The first operand.

`rightOperand` _GpuBigUInt_<br>
The second operand.

`resultBuffer` _GpuBigUInt_<br>
The buffer to store the result.

#### Exceptions

`NotImplementedException`<br>
Always thrown. Not supported on the native backend.

## BitwiseXor(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Throws `NotImplementedException` on the native backend.

```csharp
public void BitwiseXor(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuBigUInt_<br>
The first operand.

`rightOperand` _GpuBigUInt_<br>
The second operand.

`resultBuffer` _GpuBigUInt_<br>
The buffer to store the result.

#### Exceptions

`NotImplementedException`<br>
Always thrown. Not supported on the native backend.

## BitwiseNot(GpuWorkspace, GpuBigUInt, GpuBigUInt)

Throws `NotImplementedException` on the native backend.

```csharp
public void BitwiseNot(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuBigUInt_<br>
The operand.

`resultBuffer` _GpuBigUInt_<br>
The buffer to store the result.

#### Exceptions

`NotImplementedException`<br>
Always thrown. Not supported on the native backend.

## ShiftLeft(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)

Computes left bit shift on GPU via `NativeMethods.GpuBigInt_ShiftLeft`.

```csharp
public void ShiftLeft(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuBigUInt_<br>
The operand to shift.

`shiftBits` _Int32_<br>
The number of bits to shift.

`resultBuffer` _GpuBigUInt_<br>
The buffer to store the result.

## ShiftRight(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)

Computes right bit shift on GPU via `NativeMethods.GpuBigInt_ShiftRight`.

```csharp
public void ShiftRight(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuBigUInt_<br>
The operand to shift.

`shiftBits` _Int32_<br>
The number of bits to shift.

`resultBuffer` _GpuBigUInt_<br>
The buffer to store the result.

## Square(GpuWorkspace, IntPtr, Int32)

Computes in-place self squaring on GPU via `NativeMethods.GpuBigInt_Square`.

```csharp
public bool Square(GpuWorkspace workspace, IntPtr devicePointer, int logicalWords);
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

## IsZero(GpuWorkspace, GpuBigUInt)

Copies the operand limbs to host memory and checks whether all words equal zero.

```csharp
public bool IsZero(GpuWorkspace workspace, GpuBigUInt leftOperand);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuBigUInt_<br>
The operand to check.

#### Returns

_Boolean_<br>
`true` if the operand is zero; otherwise, `false`.

#### Exceptions

`InvalidOperationException`<br>
Thrown when copying the operand from device memory fails.

## IsOne(GpuWorkspace, GpuBigUInt)

Throws `NotImplementedException` on the native backend.

```csharp
public bool IsOne(GpuWorkspace workspace, GpuBigUInt leftOperand);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuBigUInt_<br>
The operand to check.

#### Returns

_Boolean_<br>
Does not return; always throws `NotImplementedException`.

#### Exceptions

`NotImplementedException`<br>
Always thrown. Not supported on the native backend.

## GetBitLength(GpuWorkspace, GpuBigUInt)

Copies the operand limbs to host memory and computes the active bit length.

```csharp
public int GetBitLength(GpuWorkspace workspace, GpuBigUInt leftOperand);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuBigUInt_<br>
The operand.

#### Returns

_Int32_<br>
The number of significant bits required to represent the operand.

#### Exceptions

`InvalidOperationException`<br>
Thrown when copying the operand from device memory fails.

## CompareTo(GpuWorkspace, GpuBigUInt, GpuBigUInt)

Throws `NotImplementedException` on the native backend.

```csharp
public int CompareTo(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuBigUInt_<br>
The first operand.

`rightOperand` _GpuBigUInt_<br>
The second operand.

#### Returns

_Int32_<br>
Does not return; always throws `NotImplementedException`.

#### Exceptions

`NotImplementedException`<br>
Always thrown. Not supported on the native backend.

## Mul(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Throws `NotImplementedException` on the native backend.

```csharp
public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuBigUInt_<br>
The first operand.

`rightOperand` _GpuBigUInt_<br>
The second operand.

`resultBuffer` _GpuBigUInt_<br>
The buffer to store the result.

#### Exceptions

`NotImplementedException`<br>
Always thrown. Not supported on the native backend.

## Mul(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)

Throws `NotImplementedException` on the native backend.

```csharp
public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuBigUInt_<br>
The first operand.

`rightOperand` _UInt32_<br>
The scalar second operand.

`resultBuffer` _GpuBigUInt_<br>
The buffer to store the result.

#### Exceptions

`NotImplementedException`<br>
Always thrown. Not supported on the native backend.

## InverseSqrtMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, Int32, Int32, Int32)

Throws `NotImplementedException` on the native backend.

```csharp
public void InverseSqrtMega(
    GpuWorkspace workspace,
    GpuBigUInt targetOperand,
    GpuBigUInt resultBuffer,
    int marginWords,
    int actualBits,
    int iterations);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`targetOperand` _GpuBigUInt_<br>
The target input operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

`marginWords` _Int32_<br>
The safety limb margin.

`actualBits` _Int32_<br>
The target precision in bits.

`iterations` _Int32_<br>
The number of Newton-Raphson iterations.

#### Exceptions

`NotImplementedException`<br>
Always thrown. Not supported on the native backend.

## ReciprocalMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, Int32, Int32, Int32, Int32, Int32)

Throws `NotImplementedException` on the native backend.

```csharp
public void ReciprocalMega(
    GpuWorkspace workspace,
    GpuBigUInt targetOperand,
    GpuBigUInt resultBuffer,
    int marginWords,
    int numBits,
    int marginBits,
    int iterations,
    int targetBits);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`targetOperand` _GpuBigUInt_<br>
The target divisor operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

`marginWords` _Int32_<br>
The margin limbs.

`numBits` _Int32_<br>
The base bit precision.

`marginBits` _Int32_<br>
Additional precision guard bits.

`iterations` _Int32_<br>
The number of Newton-Raphson iterations.

`targetBits` _Int32_<br>
The final desired precision in bits.

#### Exceptions

`NotImplementedException`<br>
Always thrown. Not supported on the native backend.

## Gcd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Throws `NotImplementedException` on the native backend.

```csharp
public void Gcd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuBigUInt_<br>
The first operand.

`rightOperand` _GpuBigUInt_<br>
The second operand.

`resultBuffer` _GpuBigUInt_<br>
The buffer to store the result.

#### Exceptions

`NotImplementedException`<br>
Always thrown. Not supported on the native backend.

## ModInverse(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Throws `NotImplementedException` on the native backend.

```csharp
public void ModInverse(GpuWorkspace workspace, GpuBigUInt value, GpuBigUInt modulus, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`value` _GpuBigUInt_<br>
The value to invert.

`modulus` _GpuBigUInt_<br>
The modulus.

`resultBuffer` _GpuBigUInt_<br>
The buffer to store the result.

#### Exceptions

`NotImplementedException`<br>
Always thrown. Not supported on the native backend.

## BeginCapture(IntPtr)

Throws `NotImplementedException` on the native backend.

```csharp
public void BeginCapture(IntPtr stream);
```

#### Parameters

`stream` _IntPtr_<br>
The CUDA stream handle.

#### Exceptions

`NotImplementedException`<br>
Always thrown. Not supported on the native backend.

## EndCapture(IntPtr)

Throws `NotImplementedException` on the native backend.

```csharp
public IntPtr EndCapture(IntPtr stream);
```

#### Parameters

`stream` _IntPtr_<br>
The CUDA stream handle.

#### Returns

_IntPtr_<br>
Does not return; always throws `NotImplementedException`.

#### Exceptions

`NotImplementedException`<br>
Always thrown. Not supported on the native backend.

## InstantiateGraph(IntPtr)

Throws `NotImplementedException` on the native backend.

```csharp
public IntPtr InstantiateGraph(IntPtr graph);
```

#### Parameters

`graph` _IntPtr_<br>
The captured graph handle.

#### Returns

_IntPtr_<br>
Does not return; always throws `NotImplementedException`.

#### Exceptions

`NotImplementedException`<br>
Always thrown. Not supported on the native backend.

## LaunchGraph(IntPtr, IntPtr)

Throws `NotImplementedException` on the native backend.

```csharp
public void LaunchGraph(IntPtr graphExec, IntPtr stream);
```

#### Parameters

`graphExec` _IntPtr_<br>
The instantiated executable graph handle.

`stream` _IntPtr_<br>
The target CUDA stream handle.

#### Exceptions

`NotImplementedException`<br>
Always thrown. Not supported on the native backend.

## DestroyGraph(IntPtr, IntPtr)

Throws `NotImplementedException` on the native backend.

```csharp
public void DestroyGraph(IntPtr graph, IntPtr graphExec);
```

#### Parameters

`graph` _IntPtr_<br>
The graph handle to destroy.

`graphExec` _IntPtr_<br>
The executable graph handle to destroy.

#### Exceptions

`NotImplementedException`<br>
Always thrown. Not supported on the native backend.

## Remarks

- **Synchronous Execution:** Every operation in the legacy native library (`Kassyi.GSharp.GpuBigInt.Cuda`) completes synchronously before returning. Stream parameters are ignored, and `SynchronizeStreamAsync` / `CopyDeviceToHostAsync` return already-completed `ValueTask` instances.
- **Out-of-Core Architecture Rationale:** Attempting to execute monolithic multi-million-limb multiplication entirely inside a single legacy native call hits physical VRAM limits. G# is architected to handle arbitrary precision by orchestrating expression visitors, NTT block transformations, and chunking via [`GSharpKernelProvider`](GSharpKernelProvider.md). For full feature coverage (NTT multiplication, scalar operations, bitwise OR/XOR/NOT, Lehmer GCD, modular inverse, and CUDA Graphs), use [`GSharpKernelProvider`](GSharpKernelProvider.md).

## Examples

### Accessing the NativeProvider Singleton

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
// Execution Context: Host (CPU)
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

IGpuExecutionProvider provider = NativeProvider.Instance;
provider.SynchronizeStream(IntPtr.Zero);
Console.WriteLine($"Singleton provider: {provider.GetType().Name}");
```

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [IGpuExecutionProvider Interface](IGpuExecutionProvider.md)
- [GSharpKernelProvider Class](GSharpKernelProvider.md)
- [GpuWorkspace Class](../../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)
