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
| [CopyHostToDevice(IntPtr, UInt32[], Int32, IntPtr)](#copyhosttodeviceintptr-uint32-int32-intptr) | Copies limb data from host memory to device global memory. |
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

## FreeMemory(IntPtr, IntPtr)

Releases previously allocated device global memory. When `stream` is `IntPtr.Zero`, memory is freed immediately; when a stream handle is provided, release is ordered on that stream without blocking the calling thread.

```csharp
void FreeMemory(IntPtr devicePointer, IntPtr stream = 0);
```

## ClearMemoryAsync(IntPtr, Int32, IntPtr)

Asynchronously clears the specified device memory region to zero on the given CUDA stream.

```csharp
void ClearMemoryAsync(IntPtr devicePointer, int numWords, IntPtr stream);
```

## CopyHostToDevice(IntPtr, UInt32[], Int32, IntPtr)

Copies limb data synchronously or asynchronously from host memory to device global memory.

```csharp
void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords, IntPtr stream = 0);
```

## CopyDeviceToHost(UInt32[], IntPtr, Int32, IntPtr)

Copies limb data from device global memory to host memory and waits until the data has arrived.

```csharp
void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords, IntPtr stream = 0);
```

> [!IMPORTANT]
> This method is synchronous: `hostData` holds the data when it returns, whether the array is pageable or page-locked, so callers need no additional stream synchronization. Use [CopyDeviceToHostAsync](#copydevicetohostasyncmemoryuint32-intptr-int32-intptr-cancellationtoken) to avoid blocking the calling thread while queued work completes.

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

#### Exceptions

`ArgumentOutOfRangeException`<br>
`numWords` exceeds the length of `hostData`.

`OperationCanceledException`<br>
`cancellationToken` was canceled.

## CopyDeviceToDevice(IntPtr, IntPtr, Int32, IntPtr)

Copies data directly between two device memory allocations.

```csharp
void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords, IntPtr stream = 0);
```

## ReadWord(IntPtr, Int32, IntPtr)

Reads a single 32-bit limb value directly from device memory at the given word offset.

```csharp
uint ReadWord(IntPtr devicePointer, int wordOffset = 0, IntPtr stream = 0);
```

## WriteWord(IntPtr, Int32, UInt32, IntPtr)

Writes a single 32-bit limb value directly to device memory at the given word offset.

```csharp
void WriteWord(IntPtr devicePointer, int wordOffset, uint value, IntPtr stream = 0);
```

## SynchronizeStream(IntPtr)

Blocks the host thread until all previously submitted tasks in the specified CUDA stream have completed.

```csharp
void SynchronizeStream(IntPtr stream);
```

## SynchronizeStreamAsync(IntPtr, CancellationToken)

Asynchronously waits until all previously submitted work in the specified CUDA stream has completed, without blocking the calling thread.

```csharp
ValueTask SynchronizeStreamAsync(IntPtr stream, CancellationToken cancellationToken = default);
```

## Add(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Adds two arbitrary-precision unsigned integers on the GPU.

```csharp
void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

## Sub(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Subtracts the right operand from the left operand on the GPU.

```csharp
void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

## Add(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)

Adds a 32-bit unsigned integer scalar to an arbitrary-precision integer on the GPU.

```csharp
void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
```

## Sub(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)

Subtracts a 32-bit unsigned integer scalar from an arbitrary-precision integer on the GPU.

```csharp
void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
```

## BitwiseAnd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes the bitwise AND of two arbitrary-precision integers on the GPU.

```csharp
void BitwiseAnd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

## BitwiseOr(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes the bitwise OR of two arbitrary-precision integers on the GPU.

```csharp
void BitwiseOr(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

## BitwiseXor(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes the bitwise XOR of two arbitrary-precision integers on the GPU.

```csharp
void BitwiseXor(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

## BitwiseNot(GpuWorkspace, GpuBigUInt, GpuBigUInt)

Computes the bitwise NOT (one's complement) of an arbitrary-precision integer on the GPU.

```csharp
void BitwiseNot(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt resultBuffer);
```

## ShiftLeft(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)

Shifts an arbitrary-precision integer left by the specified bit count on the GPU.

```csharp
void ShiftLeft(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);
```

## ShiftRight(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)

Shifts an arbitrary-precision integer right by the specified bit count on the GPU.

```csharp
void ShiftRight(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);
```

## Square(GpuWorkspace, IntPtr, Int32)

Computes the square of a large integer in-place on the GPU.

```csharp
bool Square(GpuWorkspace workspace, IntPtr devicePointer, int logicalWords);
```

## IsZero(GpuWorkspace, GpuBigUInt)

Evaluates whether the given arbitrary-precision integer is equal to zero on the GPU.

```csharp
bool IsZero(GpuWorkspace workspace, GpuBigUInt leftOperand);
```

## IsOne(GpuWorkspace, GpuBigUInt)

Evaluates whether the given arbitrary-precision integer is equal to one on the GPU.

```csharp
bool IsOne(GpuWorkspace workspace, GpuBigUInt leftOperand);
```

## GetBitLength(GpuWorkspace, GpuBigUInt)

Computes the active bit length of the given arbitrary-precision integer on the GPU.

```csharp
int GetBitLength(GpuWorkspace workspace, GpuBigUInt leftOperand);
```

## CompareTo(GpuWorkspace, GpuBigUInt, GpuBigUInt)

Compares two arbitrary-precision unsigned integers on the GPU.

```csharp
int CompareTo(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand);
```

## Mul(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Multiplies two arbitrary-precision unsigned integers using GPU NTT or Comba/Karatsuba algorithms.

```csharp
void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

## Mul(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)

Multiplies an arbitrary-precision integer by a 32-bit unsigned scalar on the GPU.

```csharp
void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
```

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

## Gcd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes the greatest common divisor (GCD) of two arbitrary-precision integers using Lehmer / Binary GCD kernels on the GPU.

```csharp
void Gcd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

## ModInverse(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes the modular multiplicative inverse (`value^-1 mod modulus`) on the GPU.

```csharp
void ModInverse(GpuWorkspace workspace, GpuBigUInt value, GpuBigUInt modulus, GpuBigUInt resultBuffer);
```

## BeginCapture(IntPtr)

Begins capturing CUDA kernel launches into a CUDA Graph on the specified stream.

```csharp
void BeginCapture(IntPtr stream);
```

## EndCapture(IntPtr)

Ends capture on the specified stream and returns the resulting unmanaged CUDA Graph handle.

```csharp
IntPtr EndCapture(IntPtr stream);
```

## InstantiateGraph(IntPtr)

Instantiates an executable CUDA Graph instance from a captured graph handle.

```csharp
IntPtr InstantiateGraph(IntPtr graph);
```

## LaunchGraph(IntPtr, IntPtr)

Launches an instantiated executable CUDA Graph on the specified stream.

```csharp
void LaunchGraph(IntPtr graphExec, IntPtr stream);
```

## DestroyGraph(IntPtr, IntPtr)

Releases and destroys the unmanaged CUDA Graph and executable graph instances.

```csharp
void DestroyGraph(IntPtr graph, IntPtr graphExec);
```

## Examples

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

- [GSharpAotProvider Class](GSharpAotProvider.md)
- [NativeProvider Class](NativeProvider.md)
- [CSharpCpuBigIntProvider Class](CSharpCpuBigIntProvider.md)
- [GpuWorkspace Class](../../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)
