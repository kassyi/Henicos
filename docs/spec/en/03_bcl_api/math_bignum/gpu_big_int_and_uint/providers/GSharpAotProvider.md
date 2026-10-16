---
title: "GSharpAotProvider Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Execution provider implementing arbitrary-precision BigInteger arithmetic via GSharp AOT/JIT transpiled GPU kernels."
---

## GSharpAotProvider Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Execution provider implementing [`IGpuExecutionProvider`](IGpuExecutionProvider.md) for arbitrary-precision BigInteger arithmetic via GSharp AOT/JIT transpiled GPU kernels. Acts as a unified facade delegating to [`AotMemoryHandler`](AotMemoryHandler.md), [`AotBasicMathHandler`](AotBasicMathHandler.md), [`AotAdvancedMathHandler`](AotAdvancedMathHandler.md), and [`AotGraphHandler`](AotGraphHandler.md), while automatically routing to [`CSharpCpuBigIntProvider`](CSharpCpuBigIntProvider.md) when `GpuKernelLoader.IsCpuFallbackMode` is active.

```csharp
public sealed class GSharpAotProvider : IGpuExecutionProvider
```

## Fields

| Name | Description |
| --- | --- |
| [Instance](#instance) | Gets the singleton instance of `GSharpAotProvider`. |

## Instance

Gets the singleton instance of the `GSharpAotProvider`.

```csharp
public static readonly GSharpAotProvider Instance;
```

## Methods

| Name | Description |
| --- | --- |
| [AllocateMemory(Int32)](#allocatememoryint32) | Allocates unmanaged global device memory for the specified word count. |
| [FreeMemory(IntPtr, IntPtr)](#freememoryintptr-intptr) | Frees the allocated unmanaged global device memory synchronously or stream-ordered. |
| [ClearMemoryAsync(IntPtr, Int32, IntPtr)](#clearmemoryasyncintptr-int32-intptr) | Asynchronously zeroes out `numWords` 32-bit words on the specified CUDA stream. |
| [CopyHostToDevice(IntPtr, UInt32[], Int32, IntPtr)](#copyhosttodeviceintptr-uint32-int32-intptr) | Copies limb data from host managed memory to GPU device memory. |
| [CopyDeviceToHost(UInt32[], IntPtr, Int32, IntPtr)](#copydevicetohostuint32-intptr-int32-intptr) | Copies limb data from GPU device memory to host managed memory. |
| [CopyDeviceToHostAsync(Memory&lt;UInt32&gt;, IntPtr, Int32, IntPtr, CancellationToken)](#copydevicetohostasyncmemoryuint32-intptr-int32-intptr-cancellationtoken) | Asynchronously copies limb data from GPU device memory to host memory without blocking the calling thread. |
| [CopyDeviceToDevice(IntPtr, IntPtr, Int32, IntPtr)](#copydevicetodeviceintptr-intptr-int32-intptr) | Copies limb data between two GPU device memory locations. |
| [ReadWord(IntPtr, Int32, IntPtr)](#readwordintptr-int32-intptr) | Reads a single 32-bit word at `wordOffset` from device memory. |
| [WriteWord(IntPtr, Int32, UInt32, IntPtr)](#writewordintptr-int32-uint32-intptr) | Writes a single 32-bit word at `wordOffset` to device memory. |
| [SynchronizeStream(IntPtr)](#synchronizestreamintptr) | Synchronizes the specified CUDA stream. |
| [SynchronizeStreamAsync(IntPtr, CancellationToken)](#synchronizestreamasyncintptr-cancellationtoken) | Asynchronously waits for all preceding work on the specified CUDA stream to complete. |
| [Add(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#addgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes multi-word addition on the GPU. |
| [Sub(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#subgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes multi-word subtraction on the GPU. |
| [Add(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)](#addgpuworkspace-gpubiguint-uint32-gpubiguint) | Computes scalar addition on the GPU. |
| [Sub(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)](#subgpuworkspace-gpubiguint-uint32-gpubiguint) | Computes scalar subtraction on the GPU. |
| [BitwiseAnd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwiseandgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes bitwise AND on the GPU. |
| [BitwiseOr(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwiseorgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes bitwise OR on the GPU. |
| [BitwiseXor(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwisexorgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes bitwise XOR on the GPU. |
| [BitwiseNot(GpuWorkspace, GpuBigUInt, GpuBigUInt)](#bitwisenotgpuworkspace-gpubiguint-gpubiguint) | Computes bitwise NOT on the GPU. |
| [ShiftLeft(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)](#shiftleftgpuworkspace-gpubiguint-int32-gpubiguint) | Computes left bit-shift on the GPU. |
| [ShiftRight(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)](#shiftrightgpuworkspace-gpubiguint-int32-gpubiguint) | Computes right bit-shift on the GPU. |
| [Square(GpuWorkspace, IntPtr, Int32)](#squaregpuworkspace-intptr-int32) | Computes in-place self-squaring on the GPU. |
| [IsZero(GpuWorkspace, GpuBigUInt)](#iszerogpuworkspace-gpubiguint) | Checks whether the operand equals zero on the GPU. |
| [IsOne(GpuWorkspace, GpuBigUInt)](#isonegpuworkspace-gpubiguint) | Checks whether the operand equals one on the GPU. |
| [GetBitLength(GpuWorkspace, GpuBigUInt)](#getbitlengthgpuworkspace-gpubiguint) | Gets the bit length of the operand on the GPU. |
| [CompareTo(GpuWorkspace, GpuBigUInt, GpuBigUInt)](#comparetogpuworkspace-gpubiguint-gpubiguint) | Compares two operands on the GPU. |
| [Mul(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#mulgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes multi-word NTT multiplication on the GPU. |
| [Mul(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)](#mulgpuworkspace-gpubiguint-uint32-gpubiguint) | Computes scalar multiplication on the GPU. |
| [InverseSqrtMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, Int32, Int32, Int32)](#inversesqrtmegagpuworkspace-gpubiguint-gpubiguint-int32-int32-int32) | Executes the cooperative Newton-Raphson inverse square root mega-kernel on the GPU. |
| [ReciprocalMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, Int32, Int32, Int32, Int32, Int32)](#reciprocalmegagpuworkspace-gpubiguint-gpubiguint-int32-int32-int32-int32-int32) | Executes the cooperative Newton-Raphson reciprocal mega-kernel on the GPU. |
| [Gcd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#gcdgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes the Greatest Common Divisor on the GPU using Lehmer's GCD algorithm. |
| [ModInverse(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#modinversegpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes the modular multiplicative inverse on the GPU using Extended Binary GCD or host-orchestrated Euclidean fallback. |
| [BeginCapture(IntPtr)](#begincaptureintptr) | Begins CUDA Graph capture on the specified stream. |
| [EndCapture(IntPtr)](#endcaptureintptr) | Ends CUDA Graph capture on the specified stream and returns the captured graph handle. |
| [InstantiateGraph(IntPtr)](#instantiategraphintptr) | Instantiates an executable CUDA Graph from a captured graph handle. |
| [LaunchGraph(IntPtr, IntPtr)](#launchgraphintptr-intptr) | Launches an instantiated executable CUDA Graph on the specified stream. |
| [DestroyGraph(IntPtr, IntPtr)](#destroygraphintptr-intptr) | Destroys the captured graph and executable graph handles. |

## AllocateMemory(Int32)

Allocates unmanaged global device memory for the specified word count.

```csharp
public IntPtr AllocateMemory(int numWords);
```

#### Parameters

`numWords` _Int32_<br>
The number of 32-bit words (limbs) to allocate.

#### Returns

_IntPtr_<br>
A pointer (`CUdeviceptr`) to the allocated device memory.

#### Exceptions

`CudaException`<br>
Thrown when CUDA driver device memory allocation (`cuMemAlloc_v2`) fails.

## FreeMemory(IntPtr, IntPtr)

Frees the allocated unmanaged global device memory synchronously (`cuMemFree_v2` when `stream` is `IntPtr.Zero`) or stream-ordered (`cuMemFreeAsync` when `stream` is non-zero).

```csharp
public void FreeMemory(IntPtr devicePointer, IntPtr stream = 0);
```

#### Parameters

`devicePointer` _IntPtr_<br>
The pointer to the device memory segment to free.

`stream` _IntPtr_<br>
The stream that orders the release, or `IntPtr.Zero` for an immediate release.

#### Exceptions

`CudaException`<br>
Thrown when releasing device memory fails.

## ClearMemoryAsync(IntPtr, Int32, IntPtr)

Asynchronously zeroes out `numWords` 32-bit words on the specified CUDA stream via `cuMemsetD8Async`.

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

#### Exceptions

`CudaException`<br>
Thrown when the asynchronous memory clear operation fails.

## CopyHostToDevice(IntPtr, UInt32[], Int32, IntPtr)

Copies limb data from host managed memory to GPU device memory via `cuMemcpyHtoDAsync_v2`.

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
The optional CUDA stream handle for asynchronous copy operations.

#### Exceptions

`CudaException`<br>
Thrown when copying data to device memory fails.

## CopyDeviceToHost(UInt32[], IntPtr, Int32, IntPtr)

Copies limb data from GPU device memory to host managed memory and synchronizes `stream` before returning.

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
The stream the copy is ordered on; the copy observes all work enqueued on it before the call.

#### Exceptions

`CudaException`<br>
Thrown when copying data from device memory or synchronizing the stream fails.

## CopyDeviceToHostAsync(Memory&lt;UInt32&gt;, IntPtr, Int32, IntPtr, CancellationToken)

Asynchronously copies limb data from GPU device memory to host memory without blocking the calling thread.

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

`CudaException`<br>
Thrown when staging the device-to-host transfer fails.

## CopyDeviceToDevice(IntPtr, IntPtr, Int32, IntPtr)

Copies limb data between two GPU device memory locations via `cuMemcpyDtoDAsync_v2`.

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
The optional CUDA stream handle for asynchronous copy operations.

#### Exceptions

`CudaException`<br>
Thrown when the device-to-device copy fails.

## ReadWord(IntPtr, Int32, IntPtr)

Reads a single 32-bit word at `wordOffset` from device memory.

```csharp
public uint ReadWord(IntPtr devicePointer, int wordOffset = 0, IntPtr stream = 0);
```

#### Parameters

`devicePointer` _IntPtr_<br>
The base device memory pointer.

`wordOffset` _Int32_<br>
The zero-based 32-bit word offset.

`stream` _IntPtr_<br>
The stream the read is ordered on, or `IntPtr.Zero` for the legacy null stream.

#### Returns

_UInt32_<br>
The 32-bit unsigned integer read from the device.

#### Exceptions

`CudaException`<br>
Thrown when reading the word from device memory fails.

## WriteWord(IntPtr, Int32, UInt32, IntPtr)

Writes a single 32-bit word at `wordOffset` to device memory (using `cuMemsetD32Async` when `stream` is non-zero so the payload travels inline without host buffer pinning).

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
The stream the write is ordered on, or `IntPtr.Zero` for the legacy null stream.

#### Exceptions

`CudaException`<br>
Thrown when writing the word to device memory fails.

## SynchronizeStream(IntPtr)

Blocks the calling host thread until all preceding work in the specified CUDA stream completes.

```csharp
public void SynchronizeStream(IntPtr stream);
```

#### Parameters

`stream` _IntPtr_<br>
The unmanaged stream handle to synchronize.

#### Exceptions

`CudaException`<br>
Thrown when synchronizing the CUDA stream fails.

## SynchronizeStreamAsync(IntPtr, CancellationToken)

Asynchronously waits for all preceding work on the specified CUDA stream to complete without blocking the calling thread.

```csharp
public ValueTask SynchronizeStreamAsync(IntPtr stream, CancellationToken cancellationToken = default);
```

#### Parameters

`stream` _IntPtr_<br>
The unmanaged stream handle to wait for. `IntPtr.Zero` completes immediately.

`cancellationToken` _CancellationToken_<br>
A token that detaches the awaiter; the stream keeps executing and remains usable.

#### Returns

_ValueTask_<br>
A task that completes when the stream has drained.

#### Exceptions

`OperationCanceledException`<br>
`cancellationToken` was canceled.

`CudaException`<br>
Thrown when enqueuing the stream completion callback fails.

## Add(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes multi-word addition on the GPU.

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

Computes multi-word subtraction on the GPU.

```csharp
public void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuBigUInt_<br>
The minuend operand.

`rightOperand` _GpuBigUInt_<br>
The subtrahend operand.

`resultBuffer` _GpuBigUInt_<br>
The buffer to store the result.

## Add(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)

Computes scalar addition on the GPU.

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

## Sub(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)

Computes scalar subtraction on the GPU.

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

## BitwiseAnd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes bitwise AND on the GPU.

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

Computes bitwise OR on the GPU.

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

## BitwiseXor(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes bitwise XOR on the GPU.

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

## BitwiseNot(GpuWorkspace, GpuBigUInt, GpuBigUInt)

Computes bitwise NOT on the GPU.

```csharp
public void BitwiseNot(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuBigUInt_<br>
The source operand.

`resultBuffer` _GpuBigUInt_<br>
The buffer to store the result.

## ShiftLeft(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)

Computes left bit-shift on the GPU.

```csharp
public void ShiftLeft(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuBigUInt_<br>
The operand to shift.

`shiftBits` _Int32_<br>
The number of bit positions to shift left.

`resultBuffer` _GpuBigUInt_<br>
The buffer to store the result.

## ShiftRight(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)

Computes right bit-shift on the GPU.

```csharp
public void ShiftRight(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuBigUInt_<br>
The operand to shift.

`shiftBits` _Int32_<br>
The number of bit positions to shift right.

`resultBuffer` _GpuBigUInt_<br>
The buffer to store the result.

## Square(GpuWorkspace, IntPtr, Int32)

Computes in-place self-squaring on the GPU.

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

Checks whether the operand equals zero on the GPU.

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

## IsOne(GpuWorkspace, GpuBigUInt)

Checks whether the operand equals one on the GPU.

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
`true` if the operand is one; otherwise, `false`.

## GetBitLength(GpuWorkspace, GpuBigUInt)

Gets the active bit length of the operand on the GPU.

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

## CompareTo(GpuWorkspace, GpuBigUInt, GpuBigUInt)

Compares two operands on the GPU.

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
A signed integer that indicates the relative values of the operands (`-1`, `0`, or `1`).

## Mul(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes multi-word NTT multiplication on the GPU.

```csharp
public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`leftOperand` _GpuBigUInt_<br>
The multiplicand.

`rightOperand` _GpuBigUInt_<br>
The multiplier.

`resultBuffer` _GpuBigUInt_<br>
The buffer to store the result.

## Mul(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)

Computes scalar multiplication on the GPU.

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

## InverseSqrtMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, Int32, Int32, Int32)

Executes the cooperative Newton-Raphson inverse square root mega-kernel on the GPU.

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
The destination buffer for the computed reciprocal square root.

`marginWords` _Int32_<br>
The safety limb margin allocated for intermediate expansion.

`actualBits` _Int32_<br>
The target precision in bits.

`iterations` _Int32_<br>
The number of Newton-Raphson refinement iterations.

## ReciprocalMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, Int32, Int32, Int32, Int32, Int32)

Executes the cooperative Newton-Raphson reciprocal mega-kernel on the GPU.

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
The destination buffer for the reciprocal result.

`marginWords` _Int32_<br>
The margin limbs allocated for internal scratch precision.

`numBits` _Int32_<br>
The base bit precision.

`marginBits` _Int32_<br>
Additional precision guard bits.

`iterations` _Int32_<br>
The number of Newton-Raphson iterations.

`targetBits` _Int32_<br>
The final desired precision in bits.

## Gcd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes the Greatest Common Divisor on the GPU using Lehmer's GCD algorithm.

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

## ModInverse(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes the modular multiplicative inverse on the GPU using Extended Binary GCD or host-orchestrated Euclidean fallback.

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
The destination buffer for the modular inverse.

#### Exceptions

`ArithmeticException`<br>
Thrown when `value` and `modulus` are not coprime ($\gcd \ne 1$).

## BeginCapture(IntPtr)

Begins CUDA Graph capture on the specified stream.

```csharp
public void BeginCapture(IntPtr stream);
```

#### Parameters

`stream` _IntPtr_<br>
The CUDA stream to capture.

#### Exceptions

`CudaException`<br>
Thrown when `cuStreamBeginCapture` fails.

## EndCapture(IntPtr)

Ends CUDA Graph capture on the specified stream and returns the captured graph handle.

```csharp
public IntPtr EndCapture(IntPtr stream);
```

#### Parameters

`stream` _IntPtr_<br>
The CUDA stream being captured.

#### Returns

_IntPtr_<br>
An unmanaged handle to the captured `CUgraph`.

#### Exceptions

`CudaException`<br>
Thrown when `cuStreamEndCapture` fails.

## InstantiateGraph(IntPtr)

Instantiates an executable CUDA Graph from a captured graph handle.

```csharp
public IntPtr InstantiateGraph(IntPtr graph);
```

#### Parameters

`graph` _IntPtr_<br>
The captured `CUgraph` handle.

#### Returns

_IntPtr_<br>
An unmanaged handle to the instantiated `CUgraphExec`.

#### Exceptions

`CudaException`<br>
Thrown when `cuGraphInstantiate` fails.

## LaunchGraph(IntPtr, IntPtr)

Launches an instantiated executable CUDA Graph on the specified stream.

```csharp
public void LaunchGraph(IntPtr graphExec, IntPtr stream);
```

#### Parameters

`graphExec` _IntPtr_<br>
The instantiated `CUgraphExec` handle.

`stream` _IntPtr_<br>
The stream on which the graph should be queued.

#### Exceptions

`CudaException`<br>
Thrown when `cuGraphLaunch` fails.

## DestroyGraph(IntPtr, IntPtr)

Destroys the captured graph and executable graph handles.

```csharp
public void DestroyGraph(IntPtr graph, IntPtr graphExec);
```

#### Parameters

`graph` _IntPtr_<br>
The `CUgraph` handle to destroy.

`graphExec` _IntPtr_<br>
The `CUgraphExec` handle to destroy.

## Remarks

- **Zero-Allocation:** Relies on pooled workspace memory and direct kernel launches with zero managed heap allocations in the critical execution loop.
- **Thread Safety:** Thread-safe across concurrent stream executions and kernel dispatches.
- **Automatic CPU Fallback:** When `GpuKernelLoader.IsCpuFallbackMode` is active, all memory, math, and graph operations transparently route to [`CSharpCpuBigIntProvider.Instance`](CSharpCpuBigIntProvider.md).

## Examples

### Executing Bitwise Operations on GSharpAotProvider

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
// Execution Context: Host (CPU)
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

using var workspace = new GpuWorkspace(maxWords: 1024, engine: GpuExecutionEngine.GSharpAot);
IGpuExecutionProvider provider = GSharpAotProvider.Instance;

using var a = new GpuBigUInt(workspace, new uint[] { 100 });
using var b = new GpuBigUInt(workspace, new uint[] { 50 });
using var result = new GpuBigUInt(workspace, 1);

provider.BitwiseOr(workspace, a, b, result);

uint[] hostResult = new uint[1];
provider.CopyDeviceToHost(hostResult, result.DevicePointer, 1, workspace.Stream);

// 100 = 0x64, 50 = 0x32, 100 | 50 = 118 (0x76)
Console.WriteLine($"100 | 50 = {hostResult[0]}");
```

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [IGpuExecutionProvider](IGpuExecutionProvider.md)
- [AotMemoryHandler](AotMemoryHandler.md)
- [AotBasicMathHandler](AotBasicMathHandler.md)
- [AotAdvancedMathHandler](AotAdvancedMathHandler.md)
- [AotGraphHandler](AotGraphHandler.md)
- [GpuWorkspace](../../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)
