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

## FreeMemory(IntPtr, IntPtr)

Synchronously frees the allocated unmanaged global device memory (`stream` is ignored).

```csharp
public void FreeMemory(IntPtr devicePointer, IntPtr stream = 0);
```

## ClearMemoryAsync(IntPtr, Int32, IntPtr)

No-op on the legacy native provider.

```csharp
public void ClearMemoryAsync(IntPtr devicePointer, int numWords, IntPtr stream);
```

## CopyHostToDevice(IntPtr, UInt32[], Int32, IntPtr)

Synchronously copies limb data from host managed memory to GPU device memory (`stream` is ignored).

```csharp
public void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords, IntPtr stream = 0);
```

#### Exceptions

`InvalidOperationException`<br>
Thrown when copying data from host managed memory to GPU device memory fails.

## CopyDeviceToHost(UInt32[], IntPtr, Int32, IntPtr)

Synchronously copies limb data from GPU device memory to host managed memory (`stream` is ignored).

```csharp
public void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords, IntPtr stream = 0);
```

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

#### Exceptions

`ArgumentOutOfRangeException`<br>
`numWords` exceeds `hostData.Length`.

## CopyDeviceToDevice(IntPtr, IntPtr, Int32, IntPtr)

Synchronously copies limb data between two GPU device memory locations (`stream` is ignored).

```csharp
public void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords, IntPtr stream = 0);
```

#### Exceptions

`InvalidOperationException`<br>
Thrown when copying data between device memory locations fails.

## ReadWord(IntPtr, Int32, IntPtr)

Synchronously reads a single 32-bit word from device memory at `wordOffset`.

```csharp
public uint ReadWord(IntPtr devicePointer, int wordOffset = 0, IntPtr stream = 0);
```

## WriteWord(IntPtr, Int32, UInt32, IntPtr)

Synchronously writes a single 32-bit word to device memory at `wordOffset`.

```csharp
public void WriteWord(IntPtr devicePointer, int wordOffset, uint value, IntPtr stream = 0);
```

## SynchronizeStream(IntPtr)

No-op because legacy native calls execute synchronously.

```csharp
public void SynchronizeStream(IntPtr stream);
```

## SynchronizeStreamAsync(IntPtr, CancellationToken)

Returns `ValueTask.CompletedTask` unless `cancellationToken.IsCancellationRequested` is `true`.

```csharp
public ValueTask SynchronizeStreamAsync(IntPtr stream, CancellationToken cancellationToken = default);
```

## Add(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes addition on GPU via `NativeMethods.GpuBigInt_Add`.

```csharp
public void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

## Sub(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes subtraction on GPU via `NativeMethods.GpuBigInt_Sub`.

```csharp
public void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

## BitwiseAnd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes bitwise AND on GPU via `NativeMethods.GpuBigInt_BitwiseAnd`.

```csharp
public void BitwiseAnd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

## ShiftLeft(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)

Computes left bit shift on GPU via `NativeMethods.GpuBigInt_ShiftLeft`.

```csharp
public void ShiftLeft(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);
```

## ShiftRight(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)

Computes right bit shift on GPU via `NativeMethods.GpuBigInt_ShiftRight`.

```csharp
public void ShiftRight(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);
```

## Square(GpuWorkspace, IntPtr, Int32)

Computes in-place self squaring on GPU via `NativeMethods.GpuBigInt_Square`.

```csharp
public bool Square(GpuWorkspace workspace, IntPtr devicePointer, int logicalWords);
```

## IsZero(GpuWorkspace, GpuBigUInt)

Copies the operand limbs to host memory and checks whether all words equal zero.

```csharp
public bool IsZero(GpuWorkspace workspace, GpuBigUInt leftOperand);
```

## GetBitLength(GpuWorkspace, GpuBigUInt)

Copies the operand limbs to host memory and computes the active bit length.

```csharp
public int GetBitLength(GpuWorkspace workspace, GpuBigUInt leftOperand);
```

## Add / Sub / Mul / BitwiseOr / BitwiseXor / BitwiseNot / IsOne / CompareTo / Gcd / ModInverse / InverseSqrtMega / ReciprocalMega / Graph Capture Methods

These operations are not implemented on the legacy native provider and unconditionally throw `NotImplementedException`. Use [GSharpAotProvider](GSharpAotProvider.md) instead.

```csharp
public void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
public void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
public void BitwiseOr(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
public void BitwiseXor(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
public void BitwiseNot(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt resultBuffer);
public bool IsOne(GpuWorkspace workspace, GpuBigUInt leftOperand);
public int CompareTo(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand);
public void Gcd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
public void ModInverse(GpuWorkspace workspace, GpuBigUInt value, GpuBigUInt modulus, GpuBigUInt resultBuffer);
public void InverseSqrtMega(GpuWorkspace workspace, GpuBigUInt targetOperand, GpuBigUInt resultBuffer, int marginWords, int actualBits, int iterations);
public void ReciprocalMega(GpuWorkspace workspace, GpuBigUInt targetOperand, GpuBigUInt resultBuffer, int marginWords, int numBits, int marginBits, int iterations, int targetBits);
public void BeginCapture(IntPtr stream);
public IntPtr EndCapture(IntPtr stream);
public IntPtr InstantiateGraph(IntPtr graph);
public void LaunchGraph(IntPtr graphExec, IntPtr stream);
public void DestroyGraph(IntPtr graph, IntPtr graphExec);
```

## Remarks

Every operation in the legacy native library (`Kassyi.GSharp.GpuBigInt.Cuda`) completes synchronously before returning. Stream parameters are ignored, and `SynchronizeStreamAsync` / `CopyDeviceToHostAsync` return already-completed `ValueTask` instances. For full feature coverage (NTT multiplication, scalar operations, bitwise OR/XOR/NOT, Lehmer GCD, modular inverse, and CUDA Graphs), use [GSharpAotProvider](GSharpAotProvider.md).

## Examples

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

- [IGpuExecutionProvider Interface](IGpuExecutionProvider.md)
- [GSharpAotProvider Class](GSharpAotProvider.md)
- [GpuWorkspace Class](../../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)
