---
title: "CSharpCpuBigIntProvider Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Reference host (CPU) execution provider for arbitrary-precision large integer arithmetic."
---

## CSharpCpuBigIntProvider Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Provides a stateless, thread-safe reference host (CPU) execution provider implementing [`IGpuExecutionProvider`](IGpuExecutionProvider.md) by delegating multi-precision calculations to `System.Numerics.BigInteger` over unmanaged host heap memory (`Marshal.AllocHGlobal`).

```csharp
public sealed class CSharpCpuBigIntProvider : IGpuExecutionProvider
```

## Fields

| Name | Description |
| --- | --- |
| [Instance](#instance) | Gets the singleton instance of `CSharpCpuBigIntProvider`. |

## Instance

Gets the singleton instance of the `CSharpCpuBigIntProvider`.

```csharp
public static readonly CSharpCpuBigIntProvider Instance;
```

## Methods

| Name | Description |
| --- | --- |
| [Add(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#addgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes unsigned multi-word addition on the CPU. |
| [Add(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)](#addgpuworkspace-gpubiguint-uint32-gpubiguint) | Computes unsigned scalar addition on the CPU. |
| [AllocateMemory(Int32)](#allocatememoryint32) | Allocates an unmanaged host memory block of `numWords * 4` bytes via `Marshal.AllocHGlobal`. |
| [BeginCapture(IntPtr)](#begincaptureintptr) | No-op in CPU fallback mode. |
| [BitwiseAnd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwiseandgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes bitwise AND on the CPU. |
| [BitwiseNot(GpuWorkspace, GpuBigUInt, GpuBigUInt)](#bitwisenotgpuworkspace-gpubiguint-gpubiguint) | Computes bitwise NOT on the CPU. |
| [BitwiseOr(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwiseorgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes bitwise OR on the CPU. |
| [BitwiseXor(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwisexorgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes bitwise XOR on the CPU. |
| [ClearMemoryAsync(IntPtr, Int32, IntPtr)](#clearmemoryasyncintptr-int32-intptr) | Zero-initializes `numWords` 32-bit words in unmanaged host memory. |
| [CompareTo(GpuWorkspace, GpuBigUInt, GpuBigUInt)](#comparetogpuworkspace-gpubiguint-gpubiguint) | Compares two unsigned multi-word operands. |
| [CopyDeviceToDevice(IntPtr, IntPtr, Int32, IntPtr)](#copydevicetodeviceintptr-intptr-int32-intptr) | Copies `numWords` 32-bit words between two unmanaged host memory pointers. |
| [CopyDeviceToHost(UInt32[], IntPtr, Int32, IntPtr)](#copydevicetohostuint32-intptr-int32-intptr) | Copies `numWords` 32-bit words from unmanaged host memory into a managed `uint[]` array. |
| [CopyDeviceToHostAsync(Memory&lt;UInt32&gt;, IntPtr, Int32, IntPtr, CancellationToken)](#copydevicetohostasyncmemoryuint32-intptr-int32-intptr-cancellationtoken) | Synchronously copies `numWords` 32-bit words into `Memory<uint>` and returns a completed `ValueTask`. |
| [CopyHostToDevice(IntPtr, UInt32[], Int32, IntPtr)](#copyhosttodeviceintptr-uint32-int32-intptr) | Copies `numWords` 32-bit words from a managed `uint[]` array into unmanaged host memory. |
| [DestroyGraph(IntPtr, IntPtr)](#destroygraphintptr-intptr) | No-op in CPU fallback mode. |
| [EndCapture(IntPtr)](#endcaptureintptr) | Returns `IntPtr.Zero` in CPU fallback mode. |
| [FreeMemory(IntPtr, IntPtr)](#freememoryintptr-intptr) | Frees an unmanaged host memory block allocated by `AllocateMemory`. |
| [Gcd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#gcdgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes the greatest common divisor on the CPU. |
| [GetBitLength(GpuWorkspace, GpuBigUInt)](#getbitlengthgpuworkspace-gpubiguint) | Gets the significant bit length of the operand. |
| [InstantiateGraph(IntPtr)](#instantiategraphintptr) | Returns `IntPtr.Zero` in CPU fallback mode. |
| [InverseSqrtMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, Int32, Int32, Int32)](#inversesqrtmegagpuworkspace-gpubiguint-gpubiguint-int32-int32-int32) | Computes fixed-point inverse square root on the CPU. |
| [IsOne(GpuWorkspace, GpuBigUInt)](#isonegpuworkspace-gpubiguint) | Determines whether the operand equals `1`. |
| [IsZero(GpuWorkspace, GpuBigUInt)](#iszerogpuworkspace-gpubiguint) | Determines whether all words of the operand are zero. |
| [LaunchGraph(IntPtr, IntPtr)](#launchgraphintptr-intptr) | No-op in CPU fallback mode. |
| [ModInverse(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#modinversegpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes the modular multiplicative inverse on the CPU using the Extended Euclidean Algorithm. |
| [Mul(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#mulgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes unsigned multi-word multiplication on the CPU. |
| [Mul(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)](#mulgpuworkspace-gpubiguint-uint32-gpubiguint) | Computes unsigned scalar multiplication on the CPU. |
| [ReadWord(IntPtr, Int32, IntPtr)](#readwordintptr-int32-intptr) | Reads a single 32-bit word at `wordOffset` from unmanaged host memory. |
| [ReciprocalMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, Int32, Int32, Int32, Int32, Int32)](#reciprocalmegagpuworkspace-gpubiguint-gpubiguint-int32-int32-int32-int32-int32) | Computes fixed-point reciprocal ($2^{\text{targetBits}} / x$) on the CPU. |
| [ShiftLeft(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)](#shiftleftgpuworkspace-gpubiguint-int32-gpubiguint) | Computes left bit-shift on the CPU. |
| [ShiftRight(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)](#shiftrightgpuworkspace-gpubiguint-int32-gpubiguint) | Computes right bit-shift on the CPU. |
| [Square(GpuWorkspace, IntPtr, Int32)](#squaregpuworkspace-intptr-int32) | Computes in-place squaring on the CPU. |
| [Sub(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#subgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes unsigned multi-word subtraction on the CPU. |
| [Sub(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)](#subgpuworkspace-gpubiguint-uint32-gpubiguint) | Computes unsigned scalar subtraction on the CPU. |
| [SynchronizeStream(IntPtr)](#synchronizestreamintptr) | No-op in CPU fallback mode. |
| [SynchronizeStreamAsync(IntPtr, CancellationToken)](#synchronizestreamasyncintptr-cancellationtoken) | Returns a completed `ValueTask` (or canceled if `cancellationToken` is requested). |
| [WriteWord(IntPtr, Int32, UInt32, IntPtr)](#writewordintptr-int32-uint32-intptr) | Writes a single 32-bit word at `wordOffset` in unmanaged host memory. |

## AllocateMemory(Int32)

Allocates an unmanaged host memory block of `numWords * 4` bytes via `Marshal.AllocHGlobal`.

```csharp
public IntPtr AllocateMemory(int numWords);
```

#### Parameters

`numWords` _Int32_<br>
The number of 32-bit words (limbs) to allocate.

#### Returns

_IntPtr_<br>
A pointer to the allocated unmanaged host memory block.

## FreeMemory(IntPtr, IntPtr)

Frees an unmanaged host memory block allocated by `AllocateMemory` (`stream` is ignored).

```csharp
public void FreeMemory(IntPtr devicePointer, IntPtr stream = 0);
```

#### Parameters

`devicePointer` _IntPtr_<br>
The pointer to the unmanaged host memory block to free.

`stream` _IntPtr_<br>
The stream handle (ignored; host memory is freed immediately).

## ClearMemoryAsync(IntPtr, Int32, IntPtr)

Zero-initializes `numWords` 32-bit words in unmanaged host memory (`stream` is ignored).

```csharp
public unsafe void ClearMemoryAsync(IntPtr devicePointer, int numWords, IntPtr stream);
```

#### Parameters

`devicePointer` _IntPtr_<br>
The starting unmanaged host memory address.

`numWords` _Int32_<br>
The number of 32-bit words to clear.

`stream` _IntPtr_<br>
The stream handle (ignored).

## CopyHostToDevice(IntPtr, UInt32[], Int32, IntPtr)

Copies `numWords` 32-bit words from a managed `uint[]` array into unmanaged host memory (`stream` is ignored).

```csharp
public unsafe void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords, IntPtr stream = 0);
```

#### Parameters

`devicePointer` _IntPtr_<br>
The destination unmanaged host memory pointer.

`hostData` _UInt32[]_<br>
The source managed 32-bit unsigned integer array.

`numWords` _Int32_<br>
The number of 32-bit words to copy.

`stream` _IntPtr_<br>
The stream handle (ignored).

## CopyDeviceToHost(UInt32[], IntPtr, Int32, IntPtr)

Copies `numWords` 32-bit words from unmanaged host memory into a managed `uint[]` array (`stream` is ignored).

```csharp
public unsafe void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords, IntPtr stream = 0);
```

#### Parameters

`hostData` _UInt32[]_<br>
The destination managed 32-bit unsigned integer array.

`devicePointer` _IntPtr_<br>
The source unmanaged host memory pointer.

`numWords` _Int32_<br>
The number of 32-bit words to copy.

`stream` _IntPtr_<br>
The stream handle (ignored).

## CopyDeviceToHostAsync(Memory&lt;UInt32&gt;, IntPtr, Int32, IntPtr, CancellationToken)

Synchronously copies `numWords` 32-bit words from unmanaged host memory into `hostData` and returns a completed `ValueTask` (`stream` is ignored).

```csharp
public unsafe ValueTask CopyDeviceToHostAsync(
    Memory<uint> hostData,
    IntPtr devicePointer,
    int numWords,
    IntPtr stream,
    CancellationToken cancellationToken = default);
```

#### Parameters

`hostData` _Memory&lt;UInt32&gt;_<br>
The destination memory buffer, at least `numWords` words long.

`devicePointer` _IntPtr_<br>
The source unmanaged host memory pointer.

`numWords` _Int32_<br>
The number of 32-bit words to copy.

`stream` _IntPtr_<br>
The stream handle (ignored).

`cancellationToken` _CancellationToken_<br>
A cancellation token checked before performing the copy.

#### Returns

_ValueTask_<br>
A completed `ValueTask`, or a canceled `ValueTask` if `cancellationToken` was already canceled.

#### Exceptions

`ArgumentOutOfRangeException`<br>
`numWords` exceeds `hostData.Length`.

## CopyDeviceToDevice(IntPtr, IntPtr, Int32, IntPtr)

Copies `numWords` 32-bit words between two unmanaged host memory pointers (`stream` is ignored).

```csharp
public unsafe void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords, IntPtr stream = 0);
```

#### Parameters

`destPointer` _IntPtr_<br>
The destination unmanaged host memory pointer.

`srcPointer` _IntPtr_<br>
The source unmanaged host memory pointer.

`numWords` _Int32_<br>
The number of 32-bit words to copy.

`stream` _IntPtr_<br>
The stream handle (ignored).

## ReadWord(IntPtr, Int32, IntPtr)

Reads a single 32-bit word at `wordOffset` from unmanaged host memory (`stream` is ignored).

```csharp
public unsafe uint ReadWord(IntPtr devicePointer, int wordOffset = 0, IntPtr stream = 0);
```

#### Parameters

`devicePointer` _IntPtr_<br>
The base unmanaged host memory pointer.

`wordOffset` _Int32_<br>
The zero-based 32-bit word offset.

`stream` _IntPtr_<br>
The stream handle (ignored).

#### Returns

_UInt32_<br>
The 32-bit unsigned integer read from host memory.

## WriteWord(IntPtr, Int32, UInt32, IntPtr)

Writes a single 32-bit word at `wordOffset` in unmanaged host memory (`stream` is ignored).

```csharp
public unsafe void WriteWord(IntPtr devicePointer, int wordOffset, uint value, IntPtr stream = 0);
```

#### Parameters

`devicePointer` _IntPtr_<br>
The base unmanaged host memory pointer.

`wordOffset` _Int32_<br>
The zero-based 32-bit word offset.

`value` _UInt32_<br>
The 32-bit unsigned integer value to write.

`stream` _IntPtr_<br>
The stream handle (ignored).

## SynchronizeStream(IntPtr)

Performs no operation in CPU fallback mode.

```csharp
public void SynchronizeStream(IntPtr stream);
```

#### Parameters

`stream` _IntPtr_<br>
The stream handle (ignored).

## SynchronizeStreamAsync(IntPtr, CancellationToken)

Returns `ValueTask.CompletedTask` unless `cancellationToken.IsCancellationRequested` is `true`.

```csharp
public ValueTask SynchronizeStreamAsync(IntPtr stream, CancellationToken cancellationToken = default);
```

#### Parameters

`stream` _IntPtr_<br>
The stream handle (ignored).

`cancellationToken` _CancellationToken_<br>
A cancellation token checked before returning.

#### Returns

_ValueTask_<br>
A completed `ValueTask`, or a canceled `ValueTask` if `cancellationToken` is canceled.

## Add(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes unsigned multi-word addition on the CPU.

```csharp
public void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The execution workspace.

`leftOperand` _GpuBigUInt_<br>
The left operand.

`rightOperand` _GpuBigUInt_<br>
The right operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

## Sub(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes unsigned multi-word subtraction on the CPU.

```csharp
public void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The execution workspace.

`leftOperand` _GpuBigUInt_<br>
The minuend operand.

`rightOperand` _GpuBigUInt_<br>
The subtrahend operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

## Add(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)

Computes unsigned scalar addition on the CPU.

```csharp
public void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The execution workspace.

`leftOperand` _GpuBigUInt_<br>
The multi-word operand.

`rightOperand` _UInt32_<br>
The 32-bit unsigned scalar operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

## Sub(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)

Computes unsigned scalar subtraction on the CPU.

```csharp
public void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The execution workspace.

`leftOperand` _GpuBigUInt_<br>
The multi-word operand.

`rightOperand` _UInt32_<br>
The 32-bit unsigned scalar operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

## BitwiseAnd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes bitwise AND on the CPU.

```csharp
public void BitwiseAnd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The execution workspace.

`leftOperand` _GpuBigUInt_<br>
The left operand.

`rightOperand` _GpuBigUInt_<br>
The right operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

## BitwiseOr(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes bitwise OR on the CPU.

```csharp
public void BitwiseOr(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The execution workspace.

`leftOperand` _GpuBigUInt_<br>
The left operand.

`rightOperand` _GpuBigUInt_<br>
The right operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

## BitwiseXor(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes bitwise XOR on the CPU.

```csharp
public void BitwiseXor(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The execution workspace.

`leftOperand` _GpuBigUInt_<br>
The left operand.

`rightOperand` _GpuBigUInt_<br>
The right operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

## BitwiseNot(GpuWorkspace, GpuBigUInt, GpuBigUInt)

Computes bitwise NOT on the CPU.

```csharp
public void BitwiseNot(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The execution workspace.

`leftOperand` _GpuBigUInt_<br>
The source operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

## ShiftLeft(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)

Computes left bit-shift on the CPU.

```csharp
public void ShiftLeft(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The execution workspace.

`leftOperand` _GpuBigUInt_<br>
The operand to shift.

`shiftBits` _Int32_<br>
The number of bit positions to shift left.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

## ShiftRight(GpuWorkspace, GpuBigUInt, Int32, GpuBigUInt)

Computes right bit-shift on the CPU.

```csharp
public void ShiftRight(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The execution workspace.

`leftOperand` _GpuBigUInt_<br>
The operand to shift.

`shiftBits` _Int32_<br>
The number of bit positions to shift right.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

## Square(GpuWorkspace, IntPtr, Int32)

Computes in-place squaring on the CPU.

```csharp
public bool Square(GpuWorkspace workspace, IntPtr devicePointer, int logicalWords);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The execution workspace.

`devicePointer` _IntPtr_<br>
The pointer to the buffer containing the operand limbs.

`logicalWords` _Int32_<br>
The number of active limbs.

#### Returns

_Boolean_<br>
Always returns `true`.

## IsZero(GpuWorkspace, GpuBigUInt)

Determines whether all words of the operand are zero.

```csharp
public bool IsZero(GpuWorkspace workspace, GpuBigUInt leftOperand);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The execution workspace.

`leftOperand` _GpuBigUInt_<br>
The operand to evaluate.

#### Returns

_Boolean_<br>
`true` if the operand equals `0`; otherwise, `false`.

## IsOne(GpuWorkspace, GpuBigUInt)

Determines whether the operand equals `1`.

```csharp
public bool IsOne(GpuWorkspace workspace, GpuBigUInt leftOperand);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The execution workspace.

`leftOperand` _GpuBigUInt_<br>
The operand to evaluate.

#### Returns

_Boolean_<br>
`true` if the operand equals `1`; otherwise, `false`.

## GetBitLength(GpuWorkspace, GpuBigUInt)

Gets the significant bit length of the operand.

```csharp
public int GetBitLength(GpuWorkspace workspace, GpuBigUInt leftOperand);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The execution workspace.

`leftOperand` _GpuBigUInt_<br>
The operand.

#### Returns

_Int32_<br>
The number of significant bits required to represent the operand.

## CompareTo(GpuWorkspace, GpuBigUInt, GpuBigUInt)

Compares two unsigned multi-word operands.

```csharp
public int CompareTo(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The execution workspace.

`leftOperand` _GpuBigUInt_<br>
The left operand.

`rightOperand` _GpuBigUInt_<br>
The right operand.

#### Returns

_Int32_<br>
`-1` if `leftOperand` &lt; `rightOperand`, `0` if equal, or `1` if `leftOperand` &gt; `rightOperand`.

## Mul(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes unsigned multi-word multiplication on the CPU.

```csharp
public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The execution workspace.

`leftOperand` _GpuBigUInt_<br>
The multiplicand.

`rightOperand` _GpuBigUInt_<br>
The multiplier.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

## Mul(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)

Computes unsigned scalar multiplication on the CPU.

```csharp
public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The execution workspace.

`leftOperand` _GpuBigUInt_<br>
The multi-word operand.

`rightOperand` _UInt32_<br>
The 32-bit scalar multiplier.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

## InverseSqrtMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, Int32, Int32, Int32)

Computes fixed-point inverse square root on the CPU.

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
The execution workspace.

`targetOperand` _GpuBigUInt_<br>
The target input operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

`marginWords` _Int32_<br>
The safety limb margin allocated for intermediate expansion.

`actualBits` _Int32_<br>
The target precision in bits.

`iterations` _Int32_<br>
The number of refinement iterations.

#### Exceptions

`DivideByZeroException`<br>
Thrown when `targetOperand` is zero.

## ReciprocalMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, Int32, Int32, Int32, Int32, Int32)

Computes fixed-point reciprocal ($2^{\text{targetBits}} / x$) on the CPU.

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
The execution workspace.

`targetOperand` _GpuBigUInt_<br>
The target divisor operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

`marginWords` _Int32_<br>
The margin limbs allocated for scratch precision.

`numBits` _Int32_<br>
The base bit precision.

`marginBits` _Int32_<br>
Additional precision guard bits.

`iterations` _Int32_<br>
The number of refinement iterations.

`targetBits` _Int32_<br>
The final desired precision in bits.

#### Exceptions

`DivideByZeroException`<br>
Thrown when `targetOperand` is zero.

## Gcd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes the greatest common divisor on the CPU.

```csharp
public void Gcd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The execution workspace.

`leftOperand` _GpuBigUInt_<br>
The first operand.

`rightOperand` _GpuBigUInt_<br>
The second operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

## ModInverse(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes the modular multiplicative inverse on the CPU using the Extended Euclidean Algorithm.

```csharp
public void ModInverse(GpuWorkspace workspace, GpuBigUInt value, GpuBigUInt modulus, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The execution workspace.

`value` _GpuBigUInt_<br>
The value to invert.

`modulus` _GpuBigUInt_<br>
The modulus.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer.

#### Exceptions

`ArithmeticException`<br>
Thrown when `value` and `modulus` are not coprime ($\gcd \ne 1$).

## BeginCapture(IntPtr)

Performs no operation. CUDA Graph stream capture is not required in CPU fallback mode.

```csharp
public void BeginCapture(IntPtr stream);
```

#### Parameters

`stream` _IntPtr_<br>
The unmanaged stream handle (ignored).

## EndCapture(IntPtr)

Ends graph capture in CPU fallback mode and returns a dummy handle.

```csharp
public IntPtr EndCapture(IntPtr stream);
```

#### Parameters

`stream` _IntPtr_<br>
The unmanaged stream handle (ignored).

#### Returns

_IntPtr_<br>
Always returns `IntPtr.Zero`.

## InstantiateGraph(IntPtr)

Instantiates an executable graph instance in CPU fallback mode and returns a dummy handle.

```csharp
public IntPtr InstantiateGraph(IntPtr graph);
```

#### Parameters

`graph` _IntPtr_<br>
The captured graph handle (ignored).

#### Returns

_IntPtr_<br>
Always returns `IntPtr.Zero`.

## LaunchGraph(IntPtr, IntPtr)

Performs no operation. Graph execution is not applicable in CPU fallback mode.

```csharp
public void LaunchGraph(IntPtr graphExec, IntPtr stream);
```

#### Parameters

`graphExec` _IntPtr_<br>
The executable graph handle (ignored).

`stream` _IntPtr_<br>
The target stream handle (ignored).

## DestroyGraph(IntPtr, IntPtr)

Performs no operation. Destroys dummy graph handles in CPU fallback mode.

```csharp
public void DestroyGraph(IntPtr graph, IntPtr graphExec);
```

#### Parameters

`graph` _IntPtr_<br>
The captured graph handle (ignored).

`graphExec` _IntPtr_<br>
The executable graph handle (ignored).

## Remarks

`CSharpCpuBigIntProvider` enables Tier-1 unit testing and golden-master verification on CI runners or development machines without NVIDIA GPUs. CUDA Graph capture methods (`IGpuGraphProvider`) are implemented as safe no-ops returning `IntPtr.Zero`.

## Examples

### Executing Multi-Precision Operations on the CPU Reference Provider

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
// Execution Context: Host (CPU)
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

IGpuExecutionProvider provider = CSharpCpuBigIntProvider.Instance;
IntPtr buffer = provider.AllocateMemory(numWords: 2);
try
{
    provider.CopyHostToDevice(buffer, new uint[] { 42u, 100u }, numWords: 2);
    uint low = provider.ReadWord(buffer, wordOffset: 0);
    uint high = provider.ReadWord(buffer, wordOffset: 1);
    Console.WriteLine($"Low={low}, High={high}");
}
finally
{
    provider.FreeMemory(buffer);
}
```

## See also

### BCL API > Math (BigNum)

- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
- [IGpuExecutionProvider](IGpuExecutionProvider.md)
- [GSharpKernelProvider](GSharpKernelProvider.md)
- [NativeProvider](NativeProvider.md)
