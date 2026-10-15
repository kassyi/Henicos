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
| `Instance` | Gets the singleton instance of `CSharpCpuBigIntProvider`. |

## Methods

| Name | Description |
| --- | --- |
| `AllocateMemory(int)` | Allocates an unmanaged host memory block of `numWords * 4` bytes via `Marshal.AllocHGlobal`. |
| `FreeMemory(IntPtr, IntPtr)` | Frees an unmanaged host memory block allocated by `AllocateMemory`. |
| `ClearMemoryAsync(IntPtr, int, IntPtr)` | Zero-initializes `numWords` 32-bit words in unmanaged host memory. |
| `CopyHostToDevice(IntPtr, uint[], int, IntPtr)` | Copies `numWords` 32-bit words from a managed `uint[]` array into unmanaged host memory. |
| `CopyDeviceToHost(uint[], IntPtr, int, IntPtr)` | Copies `numWords` 32-bit words from unmanaged host memory into a managed `uint[]` array. |
| `CopyDeviceToHostAsync(Memory<uint>, IntPtr, int, IntPtr, CancellationToken)` | Synchronously copies `numWords` 32-bit words into `Memory<uint>` and returns a completed `ValueTask`. |
| `CopyDeviceToDevice(IntPtr, IntPtr, int, IntPtr)` | Copies `numWords` 32-bit words between two unmanaged host memory pointers. |
| `ReadWord(IntPtr, int, IntPtr)` | Reads a single 32-bit word at `wordOffset` from unmanaged host memory. |
| `WriteWord(IntPtr, int, uint, IntPtr)` | Writes a single 32-bit word at `wordOffset` in unmanaged host memory. |
| `SynchronizeStream(IntPtr)` | No-op in CPU fallback mode. |
| `SynchronizeStreamAsync(IntPtr, CancellationToken)` | Returns a completed `ValueTask` (or canceled if `cancellationToken` is requested). |
| `Add(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)` | Computes unsigned multi-word addition on the CPU. |
| `Sub(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)` | Computes unsigned multi-word subtraction on the CPU. |
| `Add(GpuWorkspace, GpuBigUInt, uint, GpuBigUInt)` | Computes unsigned scalar addition on the CPU. |
| `Sub(GpuWorkspace, GpuBigUInt, uint, GpuBigUInt)` | Computes unsigned scalar subtraction on the CPU. |
| `Mul(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)` | Computes unsigned multi-word multiplication on the CPU. |
| `Mul(GpuWorkspace, GpuBigUInt, uint, GpuBigUInt)` | Computes unsigned scalar multiplication on the CPU. |
| `BitwiseAnd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)` | Computes bitwise AND on the CPU. |
| `BitwiseOr(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)` | Computes bitwise OR on the CPU. |
| `BitwiseXor(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)` | Computes bitwise XOR on the CPU. |
| `BitwiseNot(GpuWorkspace, GpuBigUInt, GpuBigUInt)` | Computes bitwise NOT on the CPU. |
| `ShiftLeft(GpuWorkspace, GpuBigUInt, int, GpuBigUInt)` | Computes left bit-shift on the CPU. |
| `ShiftRight(GpuWorkspace, GpuBigUInt, int, GpuBigUInt)` | Computes right bit-shift on the CPU. |
| `Square(GpuWorkspace, IntPtr, int)` | Computes in-place squaring on the CPU. |
| `InverseSqrtMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, int, int, int)` | Computes fixed-point inverse square root on the CPU. |
| `ReciprocalMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, int, int, int, int, int)` | Computes fixed-point reciprocal ($2^{\text{targetBits}} / x$) on the CPU. |
| `Gcd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)` | Computes the greatest common divisor on the CPU. |
| `ModInverse(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)` | Computes the modular multiplicative inverse on the CPU using the Extended Euclidean Algorithm. |
| `IsZero(GpuWorkspace, GpuBigUInt)` | Determines whether all words of the operand are zero. |
| `IsOne(GpuWorkspace, GpuBigUInt)` | Determines whether the operand equals `1`. |
| `GetBitLength(GpuWorkspace, GpuBigUInt)` | Gets the significant bit length of the operand. |
| `CompareTo(GpuWorkspace, GpuBigUInt, GpuBigUInt)` | Compares two unsigned multi-word operands. |
| `BeginCapture(IntPtr)` | No-op in CPU fallback mode. |
| `EndCapture(IntPtr)` | Returns `IntPtr.Zero` in CPU fallback mode. |
| `InstantiateGraph(IntPtr)` | Returns `IntPtr.Zero` in CPU fallback mode. |
| `LaunchGraph(IntPtr, IntPtr)` | No-op in CPU fallback mode. |
| `DestroyGraph(IntPtr, IntPtr)` | No-op in CPU fallback mode. |

## Examples

### Executing Multi-Precision Operations on the CPU Reference Provider

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
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

## Remarks

`CSharpCpuBigIntProvider` enables Tier-1 unit testing and golden-master verification on CI runners or development machines without NVIDIA GPUs. CUDA Graph capture methods (`IGpuGraphProvider`) are implemented as safe no-ops returning `IntPtr.Zero`.

## See also

### BCL API > Math (BigNum)

- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
- [IGpuExecutionProvider](IGpuExecutionProvider.md)
- [GSharpAotProvider](GSharpAotProvider.md)
- [NativeProvider](NativeProvider.md)
