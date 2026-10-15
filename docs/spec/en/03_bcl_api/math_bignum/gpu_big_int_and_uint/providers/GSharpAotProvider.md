---
title: "GSharpAotProvider Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
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
| `Instance` | Gets the singleton instance of `GSharpAotProvider`. |

## Methods

| Name | Description |
| --- | --- |
| [AllocateMemory(int)](#allocatememoryint) | Allocates unmanaged global device memory for the specified word count. |
| [FreeMemory(IntPtr, IntPtr)](#freememoryintptr-intptr) | Frees the allocated unmanaged global device memory synchronously or stream-ordered. |
| [ClearMemoryAsync(IntPtr, int, IntPtr)](#clearmemoryasyncintptr-int-intptr) | Asynchronously zeroes out `numWords` 32-bit words on the specified CUDA stream. |
| [CopyHostToDevice(IntPtr, uint[], int, IntPtr)](#copyhosttodeviceintptr-uint-int-intptr) | Copies limb data from host managed memory to GPU device memory. |
| [CopyDeviceToHost(uint[], IntPtr, int, IntPtr)](#copydevicetohostuint-intptr-int-intptr) | Copies limb data from GPU device memory to host managed memory. |
| [CopyDeviceToHostAsync(Memory&lt;uint&gt;, IntPtr, int, IntPtr, CancellationToken)](#copydevicetohostasyncmemoryuint-intptr-int-intptr-cancellationtoken) | Asynchronously copies limb data from GPU device memory to host memory without blocking the calling thread. |
| [CopyDeviceToDevice(IntPtr, IntPtr, int, IntPtr)](#copydevicetodeviceintptr-intptr-int-intptr) | Copies limb data between two GPU device memory locations. |
| [ReadWord(IntPtr, int, IntPtr)](#readwordintptr-int-intptr) | Reads a single 32-bit word at `wordOffset` from device memory. |
| [WriteWord(IntPtr, int, uint, IntPtr)](#writewordintptr-int-uint-intptr) | Writes a single 32-bit word at `wordOffset` to device memory. |
| [SynchronizeStream(IntPtr)](#synchronizestreamintptr) | Synchronizes the specified CUDA stream. |
| [SynchronizeStreamAsync(IntPtr, CancellationToken)](#synchronizestreamasyncintptr-cancellationtoken) | Asynchronously waits for all preceding work on the specified CUDA stream to complete. |
| [Add(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#addgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes multi-word addition on the GPU. |
| [Sub(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#subgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes multi-word subtraction on the GPU. |
| [Add(GpuWorkspace, GpuBigUInt, uint, GpuBigUInt)](#addgpuworkspace-gpubiguint-uint-gpubiguint) | Computes scalar addition on the GPU. |
| [Sub(GpuWorkspace, GpuBigUInt, uint, GpuBigUInt)](#subgpuworkspace-gpubiguint-uint-gpubiguint) | Computes scalar subtraction on the GPU. |
| [BitwiseAnd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwiseandgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes bitwise AND on the GPU. |
| [BitwiseOr(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwiseorgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes bitwise OR on the GPU. |
| [BitwiseXor(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwisexorgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes bitwise XOR on the GPU. |
| [BitwiseNot(GpuWorkspace, GpuBigUInt, GpuBigUInt)](#bitwisenotgpuworkspace-gpubiguint-gpubiguint) | Computes bitwise NOT on the GPU. |
| [ShiftLeft(GpuWorkspace, GpuBigUInt, int, GpuBigUInt)](#shiftleftgpuworkspace-gpubiguint-int-gpubiguint) | Computes left bit-shift on the GPU. |
| [ShiftRight(GpuWorkspace, GpuBigUInt, int, GpuBigUInt)](#shiftrightgpuworkspace-gpubiguint-int-gpubiguint) | Computes right bit-shift on the GPU. |
| [Square(GpuWorkspace, IntPtr, int)](#squaregpuworkspace-intptr-int) | Computes in-place self-squaring on the GPU. |
| [IsZero(GpuWorkspace, GpuBigUInt)](#iszerogpuworkspace-gpubiguint) | Checks whether the operand equals zero on the GPU. |
| [IsOne(GpuWorkspace, GpuBigUInt)](#isonegpuworkspace-gpubiguint) | Checks whether the operand equals one on the GPU. |
| [GetBitLength(GpuWorkspace, GpuBigUInt)](#getbitlengthgpuworkspace-gpubiguint) | Gets the bit length of the operand on the GPU. |
| [CompareTo(GpuWorkspace, GpuBigUInt, GpuBigUInt)](#comparetogpuworkspace-gpubiguint-gpubiguint) | Compares two operands on the GPU. |
| [Mul(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#mulgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes multi-word NTT multiplication on the GPU. |
| [Mul(GpuWorkspace, GpuBigUInt, uint, GpuBigUInt)](#mulgpuworkspace-gpubiguint-uint-gpubiguint) | Computes scalar multiplication on the GPU. |
| [InverseSqrtMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, int, int, int)](#inversesqrtmegagpuworkspace-gpubiguint-gpubiguint-int-int-int) | Executes the cooperative Newton-Raphson inverse square root mega-kernel on the GPU. |
| [ReciprocalMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, int, int, int, int, int)](#reciprocalmegagpuworkspace-gpubiguint-gpubiguint-int-int-int-int-int) | Executes the cooperative Newton-Raphson reciprocal mega-kernel on the GPU. |
| [Gcd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#gcdgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes the Greatest Common Divisor on the GPU using Lehmer's GCD algorithm. |
| [ModInverse(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#modinversegpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes the modular multiplicative inverse on the GPU using Extended Binary GCD or host-orchestrated Euclidean fallback. |
| [BeginCapture(IntPtr)](#begincaptureintptr) | Begins CUDA Graph capture on the specified stream. |
| [EndCapture(IntPtr)](#endcaptureintptr) | Ends CUDA Graph capture on the specified stream and returns the captured graph handle. |
| [InstantiateGraph(IntPtr)](#instantiategraphintptr) | Instantiates an executable CUDA Graph from a captured graph handle. |
| [LaunchGraph(IntPtr, IntPtr)](#launchgraphintptr-intptr) | Launches an instantiated executable CUDA Graph on the specified stream. |
| [DestroyGraph(IntPtr, IntPtr)](#destroygraphintptr-intptr) | Destroys the captured graph and executable graph handles. |

## AllocateMemory(int)

```csharp
public IntPtr AllocateMemory(int numWords);
```

## FreeMemory(IntPtr, IntPtr)

```csharp
public void FreeMemory(IntPtr devicePointer, IntPtr stream = 0);
```

## ClearMemoryAsync(IntPtr, int, IntPtr)

```csharp
public void ClearMemoryAsync(IntPtr devicePointer, int numWords, IntPtr stream);
```

## CopyHostToDevice(IntPtr, uint[], int, IntPtr)

```csharp
public void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords, IntPtr stream = 0);
```

## CopyDeviceToHost(uint[], IntPtr, int, IntPtr)

```csharp
public void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords, IntPtr stream = 0);
```

## CopyDeviceToHostAsync(Memory\<uint\>, IntPtr, int, IntPtr, CancellationToken)

```csharp
public ValueTask CopyDeviceToHostAsync(
    Memory<uint> hostData,
    IntPtr devicePointer,
    int numWords,
    IntPtr stream,
    CancellationToken cancellationToken = default);
```

## CopyDeviceToDevice(IntPtr, IntPtr, int, IntPtr)

```csharp
public void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords, IntPtr stream = 0);
```

## ReadWord(IntPtr, int, IntPtr)

```csharp
public uint ReadWord(IntPtr devicePointer, int wordOffset = 0, IntPtr stream = 0);
```

## WriteWord(IntPtr, int, uint, IntPtr)

```csharp
public void WriteWord(IntPtr devicePointer, int wordOffset, uint value, IntPtr stream = 0);
```

## SynchronizeStream(IntPtr)

```csharp
public void SynchronizeStream(IntPtr stream);
```

## SynchronizeStreamAsync(IntPtr, CancellationToken)

```csharp
public ValueTask SynchronizeStreamAsync(IntPtr stream, CancellationToken cancellationToken = default);
```

## Add(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

```csharp
public void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

## Sub(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

```csharp
public void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

## Add(GpuWorkspace, GpuBigUInt, uint, GpuBigUInt)

```csharp
public void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
```

## Sub(GpuWorkspace, GpuBigUInt, uint, GpuBigUInt)

```csharp
public void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
```

## BitwiseAnd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

```csharp
public void BitwiseAnd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

## BitwiseOr(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

```csharp
public void BitwiseOr(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

## BitwiseXor(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

```csharp
public void BitwiseXor(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

## BitwiseNot(GpuWorkspace, GpuBigUInt, GpuBigUInt)

```csharp
public void BitwiseNot(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt resultBuffer);
```

## ShiftLeft(GpuWorkspace, GpuBigUInt, int, GpuBigUInt)

```csharp
public void ShiftLeft(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);
```

## ShiftRight(GpuWorkspace, GpuBigUInt, int, GpuBigUInt)

```csharp
public void ShiftRight(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);
```

## Square(GpuWorkspace, IntPtr, int)

```csharp
public bool Square(GpuWorkspace workspace, IntPtr devicePointer, int logicalWords);
```

## IsZero(GpuWorkspace, GpuBigUInt)

```csharp
public bool IsZero(GpuWorkspace workspace, GpuBigUInt leftOperand);
```

## IsOne(GpuWorkspace, GpuBigUInt)

```csharp
public bool IsOne(GpuWorkspace workspace, GpuBigUInt leftOperand);
```

## GetBitLength(GpuWorkspace, GpuBigUInt)

```csharp
public int GetBitLength(GpuWorkspace workspace, GpuBigUInt leftOperand);
```

## CompareTo(GpuWorkspace, GpuBigUInt, GpuBigUInt)

```csharp
public int CompareTo(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand);
```

## Mul(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

```csharp
public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

## Mul(GpuWorkspace, GpuBigUInt, uint, GpuBigUInt)

```csharp
public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
```

## InverseSqrtMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, int, int, int)

```csharp
public void InverseSqrtMega(
    GpuWorkspace workspace,
    GpuBigUInt targetOperand,
    GpuBigUInt resultBuffer,
    int marginWords,
    int actualBits,
    int iterations);
```

## ReciprocalMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, int, int, int, int, int)

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

## Gcd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

```csharp
public void Gcd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

## ModInverse(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

```csharp
public void ModInverse(GpuWorkspace workspace, GpuBigUInt value, GpuBigUInt modulus, GpuBigUInt resultBuffer);
```

## BeginCapture(IntPtr)

```csharp
public void BeginCapture(IntPtr stream);
```

## EndCapture(IntPtr)

```csharp
public IntPtr EndCapture(IntPtr stream);
```

## InstantiateGraph(IntPtr)

```csharp
public IntPtr InstantiateGraph(IntPtr graph);
```

## LaunchGraph(IntPtr, IntPtr)

```csharp
public void LaunchGraph(IntPtr graphExec, IntPtr stream);
```

## DestroyGraph(IntPtr, IntPtr)

```csharp
public void DestroyGraph(IntPtr graph, IntPtr graphExec);
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
IGpuExecutionProvider provider = GSharpAotProvider.Instance;

using var a = new GpuBigUInt(workspace, new uint[] { 100 });
using var b = new GpuBigUInt(workspace, new uint[] { 50 });
using var result = new GpuBigUInt(workspace, 1);

provider.BitwiseOr(workspace, a, b, result);

uint[] hostResult = new uint[1];
provider.CopyDeviceToHost(hostResult, result.DevicePointer, 1);

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
