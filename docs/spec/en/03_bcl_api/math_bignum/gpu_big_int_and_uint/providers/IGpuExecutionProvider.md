---
title: "IGpuExecutionProvider Interface (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Defines the aggregated contract for executing GPU-accelerated BigInteger arithmetic algorithms, managing device memory, and capturing CUDA graphs."
---

## IGpuExecutionProvider Interface

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Defines the aggregated contract for executing GPU-accelerated BigInteger arithmetic algorithms, managing device memory, and capturing CUDA graphs. This file also defines the four segregated provider interfaces that `IGpuExecutionProvider` aggregates: `IGpuMemoryProvider`, `IGpuBasicMathProvider`, `IGpuAdvancedMathProvider`, and `IGpuGraphProvider`.

```csharp
public interface IGpuExecutionProvider :
    IGpuMemoryProvider,
    IGpuBasicMathProvider,
    IGpuAdvancedMathProvider,
    IGpuGraphProvider

public interface IGpuMemoryProvider
public interface IGpuBasicMathProvider
public interface IGpuAdvancedMathProvider
public interface IGpuGraphProvider
```

## Methods

### IGpuMemoryProvider Methods

| Name | Description |
| --- | --- |
| [AllocateMemory(Int32)](#allocatememoryint32) | Allocates unmanaged device global memory for the specified number of 32-bit words. |
| [ClearMemoryAsync(IntPtr, Int32, IntPtr)](#clearmemoryasyncintptr-int32-intptr) | Asynchronously clears the specified device memory region to zero on the given CUDA stream. |
| [CopyDeviceToDevice(IntPtr, IntPtr, Int32, IntPtr)](#copydevicetodeviceintptr-intptr-int32-intptr) | Copies data directly between two device memory allocations. |
| [CopyDeviceToHost(UInt32[], IntPtr, Int32, IntPtr)](#copydevicetohostuint32-intptr-int32-intptr) | Copies limb data from device global memory to host memory and waits until the data has arrived. |
| [CopyDeviceToHostAsync(Memory&lt;UInt32&gt;, IntPtr, Int32, IntPtr, CancellationToken)](#copydevicetohostasyncmemoryuint32-intptr-int32-intptr-cancellationtoken) | Asynchronously copies limb data from device global memory to host memory without blocking the calling thread. |
| [CopyHostToDevice(IntPtr, UInt32[], Int32, IntPtr)](#copyhosttodeviceintptr-uint32-int32-intptr) | Copies limb data synchronously or asynchronously from host memory to device global memory. |
| [FreeMemory(IntPtr, IntPtr)](#freememoryintptr-intptr) | Releases previously allocated device global memory, optionally ordered on a CUDA stream. |
| [ReadWord(IntPtr, Int32, IntPtr)](#readwordintptr-int32-intptr) | Reads a single 32-bit limb value directly from device memory at the given word offset. |
| [SynchronizeStream(IntPtr)](#synchronizestreamintptr) | Blocks the host thread until all previously submitted tasks in the specified CUDA stream have completed. |
| [SynchronizeStreamAsync(IntPtr, CancellationToken)](#synchronizestreamasyncintptr-cancellationtoken) | Asynchronously waits until all previously submitted work in the specified CUDA stream has completed. |
| [WriteWord(IntPtr, Int32, UInt32, IntPtr)](#writewordintptr-int32-uint32-intptr) | Writes a single 32-bit limb value directly to device memory at the given word offset. |

### IGpuBasicMathProvider Methods

| Name | Description |
| --- | --- |
| [Add(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#addgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Adds two arbitrary-precision unsigned integers on the GPU. |
| [Add(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)](#addgpuworkspace-gpubiguint-uint32-gpubiguint) | Adds a 32-bit unsigned integer scalar to an arbitrary-precision integer on the GPU. |
| [BitwiseAnd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwiseandgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes the bitwise AND of two arbitrary-precision integers on the GPU. |
| [BitwiseNot(GpuWorkspace, GpuBigUInt, GpuBigUInt)](#bitwisenotgpuworkspace-gpubiguint-gpubiguint) | Computes the bitwise NOT (one's complement) of an arbitrary-precision integer on the GPU. |
| [BitwiseOr(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwiseorgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes the bitwise OR of two arbitrary-precision integers on the GPU. |
| [BitwiseXor(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwisexorgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes the bitwise XOR of two arbitrary-precision integers on the GPU. |
| [CompareTo(GpuWorkspace, GpuBigUInt, GpuBigUInt)](#comparetogpuworkspace-gpubiguint-gpubiguint) | Compares two arbitrary-precision unsigned integers on the GPU. |
| [GetBitLength(GpuWorkspace, GpuBigUInt)](#getbitlengthgpuworkspace-gpubiguint) | Computes the active bit length of the given arbitrary-precision integer on the GPU. |
| [IsOne(GpuWorkspace, GpuBigUInt)](#isonegpuworkspace-gpubiguint) | Evaluates whether the given arbitrary-precision integer is equal to one on the GPU. |
| [IsZero(GpuWorkspace, GpuBigUInt)](#iszerogpuworkspace-gpubiguint) | Evaluates whether the given arbitrary-precision integer is equal to zero on the GPU. |
| [ShiftLeft(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)](#shiftleftgpuworkspace-gpubiguint-int32-gpubiguint) | Shifts an arbitrary-precision integer left by the specified bit count on the GPU. |
| [ShiftRight(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)](#shiftrightgpuworkspace-gpubiguint-int32-gpubiguint) | Shifts an arbitrary-precision integer right by the specified bit count on the GPU. |
| [Square(GpuWorkspace, IntPtr, Int32)](#squaregpuworkspace-intptr-int32) | Computes the square of a large integer in-place on the GPU. |
| [Sub(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#subgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Subtracts the right operand from the left operand on the GPU. |
| [Sub(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)](#subgpuworkspace-gpubiguint-uint32-gpubiguint) | Subtracts a 32-bit unsigned integer scalar from an arbitrary-precision integer on the GPU. |

### IGpuAdvancedMathProvider Methods

| Name | Description |
| --- | --- |
| [Gcd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#gcdgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes the greatest common divisor (GCD) of two arbitrary-precision integers on the GPU. |
| [InverseSqrtMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, Int32, Int32, Int32)](#inversesqrtmegagpuworkspace-gpubiguint-gpubiguint-int32-int32-int32) | Executes a high-precision inverse square root algorithm using iterative Newton-Raphson kernels on the GPU. |
| [ModInverse(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#modinversegpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes the modular multiplicative inverse (`value^-1 mod modulus`) on the GPU. |
| [Mul(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#mulgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Multiplies two arbitrary-precision unsigned integers using GPU NTT or Comba/Karatsuba algorithms. |
| [Mul(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)](#mulgpuworkspace-gpubiguint-uint32-gpubiguint) | Multiplies an arbitrary-precision integer by a 32-bit unsigned scalar on the GPU. |
| [ReciprocalMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, Int32, Int32, Int32, Int32, Int32)](#reciprocalmegagpuworkspace-gpubiguint-gpubiguint-int32-int32-int32-int32-int32) | Executes a high-precision reciprocal division algorithm using Newton-Raphson kernels on the GPU. |

### IGpuGraphProvider Methods

| Name | Description |
| --- | --- |
| [BeginCapture(IntPtr)](#begincaptureintptr) | Begins capturing CUDA kernel launches into a CUDA Graph on the specified stream. |
| [DestroyGraph(IntPtr, IntPtr)](#destroygraphintptr-intptr) | Releases and destroys the unmanaged CUDA Graph and executable graph instances. |
| [EndCapture(IntPtr)](#endcaptureintptr) | Ends capture on the specified stream and returns the resulting unmanaged CUDA Graph handle. |
| [InstantiateGraph(IntPtr)](#instantiategraphintptr) | Instantiates an executable CUDA Graph instance from a captured graph handle. |
| [LaunchGraph(IntPtr, IntPtr)](#launchgraphintptr-intptr) | Launches an instantiated executable CUDA Graph on the specified stream. |

## AllocateMemory(Int32)

Allocates unmanaged device global memory for the specified number of 32-bit words.

```csharp
IntPtr AllocateMemory(int numWords);
```

#### Parameters

`numWords` _Int32_<br>
The number of 32-bit words (limbs) to allocate.

#### Returns

_IntPtr_<br>
An unmanaged pointer (`CUdeviceptr`) to the allocated device memory segment.

## FreeMemory(IntPtr, IntPtr)

Releases previously allocated device global memory.

```csharp
void FreeMemory(IntPtr devicePointer, IntPtr stream = 0);
```

#### Parameters

`devicePointer` _IntPtr_<br>
The unmanaged pointer to the device memory segment to free.

`stream` _IntPtr_<br>
The stream that orders the release, or `IntPtr.Zero` for an immediate release:
- `IntPtr.Zero`: freed immediately. On CUDA this waits for all work on every stream of the device, so it is safe even when the memory was used on streams the caller does not know.
- A stream handle: freed once `stream` reaches this point, without blocking the calling thread. The caller guarantees that all work using the memory is ordered on `stream`, and that the stream is not being captured into a CUDA Graph.

## ClearMemoryAsync(IntPtr, Int32, IntPtr)

Asynchronously clears the specified device memory region to zero on the given CUDA stream.

```csharp
void ClearMemoryAsync(IntPtr devicePointer, int numWords, IntPtr stream);
```

#### Parameters

`devicePointer` _IntPtr_<br>
The unmanaged pointer to the starting device memory address.

`numWords` _Int32_<br>
The number of 32-bit words to clear.

`stream` _IntPtr_<br>
The unmanaged CUDA stream handle.

## CopyHostToDevice(IntPtr, UInt32[], Int32, IntPtr)

Copies limb data synchronously or asynchronously from host memory to device global memory.

```csharp
void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords, IntPtr stream = 0);
```

#### Parameters

`devicePointer` _IntPtr_<br>
The destination device memory pointer.

`hostData` _UInt32[]_<br>
The source managed 32-bit unsigned integer array.

`numWords` _Int32_<br>
The number of 32-bit words to copy.

`stream` _IntPtr_<br>
The optional CUDA stream handle for asynchronous copy operations.

## CopyDeviceToHost(UInt32[], IntPtr, Int32, IntPtr)

Copies limb data from device global memory to host memory and waits until the data has arrived.

```csharp
void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords, IntPtr stream = 0);
```

#### Parameters

`hostData` _UInt32[]_<br>
The destination managed 32-bit unsigned integer array.

`devicePointer` _IntPtr_<br>
The source device memory pointer.

`numWords` _Int32_<br>
The number of 32-bit words to copy.

`stream` _IntPtr_<br>
The stream the copy is ordered on; the copy observes all work enqueued on it before the call.

> [!IMPORTANT]
> This method is synchronous: `hostData` holds the data when it returns, whether the array is pageable or page-locked, so callers need no additional stream synchronization. It blocks the calling thread until the stream drains; use [CopyDeviceToHostAsync](#copydevicetohostasyncmemoryuint32-intptr-int32-intptr-cancellationtoken) to avoid blocking the calling thread while queued work completes.

## CopyDeviceToHostAsync(Memory&lt;UInt32&gt;, IntPtr, Int32, IntPtr, CancellationToken)

Asynchronously copies limb data from device global memory to host memory without blocking the calling thread.

```csharp
ValueTask CopyDeviceToHostAsync(
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

## CopyDeviceToDevice(IntPtr, IntPtr, Int32, IntPtr)

Copies data directly between two device memory allocations.

```csharp
void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords, IntPtr stream = 0);
```

#### Parameters

`destPointer` _IntPtr_<br>
The destination device memory pointer.

`srcPointer` _IntPtr_<br>
The source device memory pointer.

`numWords` _Int32_<br>
The number of 32-bit words to copy.

`stream` _IntPtr_<br>
The optional CUDA stream handle for asynchronous copy operations.

## ReadWord(IntPtr, Int32, IntPtr)

Reads a single 32-bit limb value directly from device memory at the given word offset.

```csharp
uint ReadWord(IntPtr devicePointer, int wordOffset = 0, IntPtr stream = 0);
```

#### Parameters

`devicePointer` _IntPtr_<br>
The base device memory pointer.

`wordOffset` _Int32_<br>
The zero-based 32-bit word offset.

`stream` _IntPtr_<br>
The stream the read is ordered on, or `IntPtr.Zero` for the legacy null stream. A stream observes exactly the work enqueued on it and does not wait for unrelated streams. The null stream additionally waits for all work on every blocking stream of the context.

#### Returns

_UInt32_<br>
The 32-bit unsigned integer read from the device. Synchronous: the value is available when the method returns.

## WriteWord(IntPtr, Int32, UInt32, IntPtr)

Writes a single 32-bit limb value directly to device memory at the given word offset.

```csharp
void WriteWord(IntPtr devicePointer, int wordOffset, uint value, IntPtr stream = 0);
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

> [!NOTE]
> With a stream the write is only enqueued and the host is not blocked; it is visible to later work enqueued on the same stream. Without a stream the write completes before the method returns.

## SynchronizeStream(IntPtr)

Blocks the host thread until all previously submitted tasks in the specified CUDA stream have completed.

```csharp
void SynchronizeStream(IntPtr stream);
```

#### Parameters

`stream` _IntPtr_<br>
The unmanaged stream handle to synchronize.

## SynchronizeStreamAsync(IntPtr, CancellationToken)

Asynchronously waits until all previously submitted work in the specified CUDA stream has completed, without blocking the calling thread.

```csharp
ValueTask SynchronizeStreamAsync(IntPtr stream, CancellationToken cancellationToken = default);
```

#### Parameters

`stream` _IntPtr_<br>
The unmanaged stream handle to wait for. `IntPtr.Zero` completes immediately, like [SynchronizeStream](#synchronizestreamintptr).

`cancellationToken` _CancellationToken_<br>
A token that detaches the awaiter; the stream keeps executing and remains usable.

#### Returns

_ValueTask_<br>
A task that completes when the stream has drained.

## Add(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Adds two arbitrary-precision unsigned integers on the GPU.

```csharp
void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing kernel parameters.

`leftOperand` _GpuBigUInt_<br>
The left unsigned integer operand.

`rightOperand` _GpuBigUInt_<br>
The right unsigned integer operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer where the sum will be written.

## Sub(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Subtracts the right operand from the left operand on the GPU.

```csharp
void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing kernel parameters.

`leftOperand` _GpuBigUInt_<br>
The minuend operand.

`rightOperand` _GpuBigUInt_<br>
The subtrahend operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer where the difference will be written.

## Add(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)

Adds a 32-bit unsigned integer scalar to an arbitrary-precision integer on the GPU.

```csharp
void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace.

`leftOperand` _GpuBigUInt_<br>
The big integer operand.

`rightOperand` _UInt32_<br>
The 32-bit unsigned scalar.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

## Sub(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)

Subtracts a 32-bit unsigned integer scalar from an arbitrary-precision integer on the GPU.

```csharp
void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace.

`leftOperand` _GpuBigUInt_<br>
The big integer operand.

`rightOperand` _UInt32_<br>
The 32-bit unsigned scalar.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

## BitwiseAnd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes the bitwise AND of two arbitrary-precision integers on the GPU.

```csharp
void BitwiseAnd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace.

`leftOperand` _GpuBigUInt_<br>
The left operand.

`rightOperand` _GpuBigUInt_<br>
The right operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

## BitwiseOr(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes the bitwise OR of two arbitrary-precision integers on the GPU.

```csharp
void BitwiseOr(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace.

`leftOperand` _GpuBigUInt_<br>
The left operand.

`rightOperand` _GpuBigUInt_<br>
The right operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

## BitwiseXor(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes the bitwise XOR of two arbitrary-precision integers on the GPU.

```csharp
void BitwiseXor(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace.

`leftOperand` _GpuBigUInt_<br>
The left operand.

`rightOperand` _GpuBigUInt_<br>
The right operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

## BitwiseNot(GpuWorkspace, GpuBigUInt, GpuBigUInt)

Computes the bitwise NOT (one's complement) of an arbitrary-precision integer on the GPU.

```csharp
void BitwiseNot(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace.

`leftOperand` _GpuBigUInt_<br>
The source operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

## ShiftLeft(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)

Shifts an arbitrary-precision integer left by the specified bit count on the GPU.

```csharp
void ShiftLeft(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace.

`leftOperand` _GpuBigUInt_<br>
The integer to shift.

`shiftBits` _Int32_<br>
The number of bit positions to shift left.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

## ShiftRight(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)

Shifts an arbitrary-precision integer right by the specified bit count on the GPU.

```csharp
void ShiftRight(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace.

`leftOperand` _GpuBigUInt_<br>
The integer to shift.

`shiftBits` _Int32_<br>
The number of bit positions to shift right.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

## Square(GpuWorkspace, IntPtr, Int32)

Computes the square of a large integer in-place or into the specified pointer on the GPU.

```csharp
bool Square(GpuWorkspace workspace, IntPtr devicePointer, int logicalWords);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace.

`devicePointer` _IntPtr_<br>
The pointer to the device memory containing the limbs.

`logicalWords` _Int32_<br>
The number of active limbs.

#### Returns

_Boolean_<br>
`true` if squaring succeeded; otherwise, `false`.

## IsZero(GpuWorkspace, GpuBigUInt)

Evaluates whether the given arbitrary-precision integer is equal to zero on the GPU.

```csharp
bool IsZero(GpuWorkspace workspace, GpuBigUInt leftOperand);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace.

`leftOperand` _GpuBigUInt_<br>
The integer operand to evaluate.

#### Returns

_Boolean_<br>
`true` if the value is zero; otherwise, `false`.

## IsOne(GpuWorkspace, GpuBigUInt)

Evaluates whether the given arbitrary-precision integer is equal to one on the GPU.

```csharp
bool IsOne(GpuWorkspace workspace, GpuBigUInt leftOperand);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace.

`leftOperand` _GpuBigUInt_<br>
The integer operand to evaluate.

#### Returns

_Boolean_<br>
`true` if the value is one; otherwise, `false`.

## GetBitLength(GpuWorkspace, GpuBigUInt)

Computates the active bit length of the given arbitrary-precision integer on the GPU.

```csharp
int GetBitLength(GpuWorkspace workspace, GpuBigUInt leftOperand);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace.

`leftOperand` _GpuBigUInt_<br>
The integer operand.

#### Returns

_Int32_<br>
The number of significant bits required to represent the integer.

## CompareTo(GpuWorkspace, GpuBigUInt, GpuBigUInt)

Compares two arbitrary-precision unsigned integers on the GPU.

```csharp
int CompareTo(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace.

`leftOperand` _GpuBigUInt_<br>
The left operand.

`rightOperand` _GpuBigUInt_<br>
The right operand.

#### Returns

_Int32_<br>
A signed integer indicating the relative order (`-1` if `leftOperand` &lt; `rightOperand`, `0` if equal, `1` if `leftOperand` &gt; `rightOperand`).

## Mul(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Multiplies two arbitrary-precision unsigned integers using GPU NTT or Comba/Karatsuba algorithms.

```csharp
void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace.

`leftOperand` _GpuBigUInt_<br>
The multiplicand.

`rightOperand` _GpuBigUInt_<br>
The multiplier.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer where the product will be stored.

## Mul(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)

Multiplies an arbitrary-precision integer by a 32-bit unsigned scalar on the GPU.

```csharp
void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace.

`leftOperand` _GpuBigUInt_<br>
The big integer operand.

`rightOperand` _UInt32_<br>
The 32-bit scalar multiplier.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

## InverseSqrtMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, Int32, Int32, Int32)

Executes a high-precision inverse square root algorithm using iterative Newton-Raphson approximation kernels on the GPU.

```csharp
void InverseSqrtMega(
    GpuWorkspace workspace,
    GpuBigUInt targetOperand,
    GpuBigUInt resultBuffer,
    int marginWords,
    int actualBits,
    int iterations);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace.

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

Executes a high-precision reciprocal division algorithm using Newton-Raphson approximation kernels on the GPU.

```csharp
void ReciprocalMega(
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
The GPU workspace.

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

Computes the greatest common divisor (GCD) of two arbitrary-precision integers using Lehmer / Binary GCD kernels on the GPU.

```csharp
void Gcd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace.

`leftOperand` _GpuBigUInt_<br>
The first integer operand.

`rightOperand` _GpuBigUInt_<br>
The second integer operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer where the GCD will be stored.

## ModInverse(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes the modular multiplicative inverse (`value^-1 mod modulus`) on the GPU.

```csharp
void ModInverse(GpuWorkspace workspace, GpuBigUInt value, GpuBigUInt modulus, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace.

`value` _GpuBigUInt_<br>
The value to invert.

`modulus` _GpuBigUInt_<br>
The modulus.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer for the modular inverse.

## BeginCapture(IntPtr)

Begins capturing CUDA kernel launches into a CUDA Graph on the specified stream.

```csharp
void BeginCapture(IntPtr stream);
```

#### Parameters

`stream` _IntPtr_<br>
The CUDA stream to capture.

## EndCapture(IntPtr)

Ends capture on the specified stream and returns the resulting unmanaged CUDA Graph handle.

```csharp
IntPtr EndCapture(IntPtr stream);
```

#### Parameters

`stream` _IntPtr_<br>
The CUDA stream being captured.

#### Returns

_IntPtr_<br>
An unmanaged handle to the captured `cudaGraph_t`.

## InstantiateGraph(IntPtr)

Instantiates an executable CUDA Graph instance from a captured graph handle.

```csharp
IntPtr InstantiateGraph(IntPtr graph);
```

#### Parameters

`graph` _IntPtr_<br>
The captured `cudaGraph_t` handle.

#### Returns

_IntPtr_<br>
An unmanaged handle to the instantiated `cudaGraphExec_t`.

## LaunchGraph(IntPtr, IntPtr)

Launches an instantiated executable CUDA Graph on the specified stream.

```csharp
void LaunchGraph(IntPtr graphExec, IntPtr stream);
```

#### Parameters

`graphExec` _IntPtr_<br>
The instantiated `cudaGraphExec_t` handle.

`stream` _IntPtr_<br>
The stream on which the graph should be queued.

## DestroyGraph(IntPtr, IntPtr)

Releases and destroys the unmanaged CUDA Graph and executable graph instances.

```csharp
void DestroyGraph(IntPtr graph, IntPtr graphExec);
```

#### Parameters

`graph` _IntPtr_<br>
The `cudaGraph_t` handle to destroy.

`graphExec` _IntPtr_<br>
The `cudaGraphExec_t` handle to destroy.

## Remarks

**Thread Safety:** Implementations must handle stream-based synchronization or ensure thread safety across concurrent stream captures.

## Examples

### Executing Bitwise Operations via IGpuExecutionProvider

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

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
provider.CopyDeviceToHost(hostResult, result.DevicePointer, 1, workspace.Stream);

// 100 = 0x64, 50 = 0x32, 100 | 50 = 118 (0x76)
Console.WriteLine($"100 | 50 = {hostResult[0]}");
```

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GSharpKernelProvider Class](GSharpKernelProvider.md)
- [CSharpCpuBigIntProvider Class](CSharpCpuBigIntProvider.md)
- [GpuWorkspace Class](../../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)

### Architecture

- [2. Compiler and Runtime Architecture](../../../../02_architecture/00_index.md)
- [1. Generator Architecture (`Kassyi.GSharp.Compiler.Generators`)](../../../../02_architecture/01_aot_transpiler.md)
