---
title: "GpuWorkspace Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Represents a GPU execution workspace managing memory allocation pools, device workspaces, resource tracking, and backend providers for large integer computations."
---

## GpuWorkspace Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Represents a GPU execution workspace managing memory allocation pools, device workspaces, composite resource tracking, and backend providers for large integer computations.

```csharp
public enum GpuExecutionEngine
{
    GSharpAot
}

public class GpuWorkspace : IGpuResourceScope, IDisposable
```

## Constructors

| Name | Description |
| --- | --- |
| [GpuWorkspace(Int32, GpuExecutionEngine, Boolean)](#gpuworkspaceint32-gpuexecutionengine-boolean) | Initializes a new instance of the `GpuWorkspace` class with the specified limb capacity, engine backend, and optional isolated CUDA context. |

## GpuWorkspace(Int32, GpuExecutionEngine, Boolean)

Initializes a new instance of the `GpuWorkspace` class with the specified limb capacity, engine backend, and optional isolated CUDA context.

```csharp
public GpuWorkspace(
    int maxWords,
    GpuExecutionEngine engine = GpuExecutionEngine.GSharpAot,
    bool useIsolatedContext = false);
```

#### Parameters

`maxWords` _Int32_<br>
The maximum word count for BigInteger operations.

`engine` _GpuExecutionEngine_<br>
The GPU execution backend engine to utilize (`GSharpAot`).

`useIsolatedContext` _Boolean_<br>
Whether to create and use an isolated CUDA context (`cuCtxCreate_v2`) instead of the shared global context.

#### Exceptions

`InvalidOperationException`<br>
Thrown when CUDA context or workspace initialization fails.

## Properties

| Name | Description |
| --- | --- |
| [DeviceSmCount](#devicesmcount) | Gets the number of streaming multiprocessors (SMs) on the active CUDA device. |
| [Engine](#engine) | Gets the active GPU execution engine type. |
| [MaxWords](#maxwords) | Gets the maximum number of 32-bit limbs (words) supported in this workspace's fixed allocations. |
| [MemoryManager](#memorymanager) | Gets the memory manager for this workspace. |
| [PolyAlgorithmThreshold](#polyalgorithmthreshold) | Gets or sets the bit length threshold for algorithm selection in poly-algorithms like ModPow. |
| [Provider](#provider) | Gets the active GPU arithmetic execution provider implementation. |
| [Stream](#stream) | Gets the dedicated CUDA Stream for this workspace, used for operations and CUDA Graph capture. |
| [Transcendental](#transcendental) | Gets the tuning parameters of the big-float `Exp`, `Log` and `Pow` run on this workspace. |
| [WideOperandThresholdWords](#wideoperandthresholdwords) | Gets or sets the threshold $g$, in words, from which the big-float `Divide` and `Rsqrt` on the GPU truncate a divisor or radicand much wider than the result. |
| [WorkspacePointer](#workspacepointer) | Gets the raw native device pointer to the allocated workspace scratchpad memory (used by legacy native backend). |

## DeviceSmCount

Gets the number of streaming multiprocessors (SMs) on the active CUDA device. This value is used to clamp grid dimensions for cooperative kernel launches.

```csharp
public int DeviceSmCount { get; }
```

## Engine

Gets the active GPU execution engine type.

```csharp
public GpuExecutionEngine Engine { get; }
```

## MaxWords

Gets the maximum number of 32-bit limbs (words) supported in this workspace's fixed allocations.

```csharp
public int MaxWords { get; }
```

## MemoryManager

Gets the memory manager for this workspace.

```csharp
public IGpuMemoryManager MemoryManager { get; }
```

## PolyAlgorithmThreshold

Gets or sets the bit length threshold for algorithm selection in poly-algorithms like ModPow. Values smaller than the threshold will use intermediate-scale algorithms (e.g., Host-Loop or RNS), while values greater than or equal to the threshold will dispatch to giant-scale MegaKernels (e.g., NTT). Default is `1024` bits.

```csharp
public int PolyAlgorithmThreshold { get; set; }
```

## Provider

Gets the active GPU arithmetic execution provider implementation (`GSharpKernelProvider.Instance`).

```csharp
public IGpuExecutionProvider Provider { get; }
```

## Stream

Gets the dedicated CUDA Stream for this workspace, used for operations and CUDA Graph capture.

```csharp
public IntPtr Stream { get; }
```

## Transcendental

Gets the tuning parameters of the big-float transcendental functions (`Exp`, `Log`, `Pow`) run on this workspace, on the GPU and in CPU fallback mode alike. See [GpuBigFloatTranscendentalOptions](../gpu_big_float/GpuBigFloatTranscendentalOptions.md).

```csharp
public GpuBigFloatTranscendentalOptions Transcendental { get; }
```

## WideOperandThresholdWords

Gets or sets the threshold $g$, in words, from which the big-float `Divide` and `Rsqrt` on the GPU truncate a divisor or radicand much wider than the result. Default `DefaultWideOperandThresholdWords` (4).

```csharp
public const int DefaultWideOperandThresholdWords = 4;
public int WideOperandThresholdWords { get; set; }
```

For a result of $n$ words, a divisor or radicand of at least $n + 2 + g$ words is cut to its top $n + 2$ words, and one multiplication at the full width corrects the result when its discarded bits leave it open (see [Wide Divisors and Radicands](../gpu_big_float/GSharpKernelFloatProvider.md#wide-divisors-and-radicands)). The results are the same bits for every $g$ (the exact value rounded toward zero); $g$ changes only the speed. `int.MaxValue` turns the cut off. The CPU paths do not truncate.

#### Exceptions

`ArgumentOutOfRangeException`<br>
The value is negative.

## WorkspacePointer

Gets the legacy native device pointer (deprecated and returns `IntPtr.Zero`).

```csharp
[Obsolete("Legacy native workspace pointers are deprecated and always IntPtr.Zero.")]
public IntPtr WorkspacePointer { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [ActivateContext()](#activatecontext) | Activates the CUDA context associated with this workspace for the lifetime of the returned scope. |
| [CreateScope()](#createscope) | Creates a nested GPU resource scope. Not currently supported on the root workspace. |
| [Dispose()](#dispose) | Releases all device memory resources, tracked buffers, and workspace handles held by this workspace. |
| [Dispose(Boolean)](#disposeboolean) | Releases unmanaged and, optionally, managed resources. |
| [Track(IDisposable)](#trackidisposable) | Registers a disposable resource to be tracked and collectively disposed by this workspace. |

## ActivateContext()

Activates the CUDA context associated with this workspace for the lifetime of the returned scope.

```csharp
public IDisposable ActivateContext();
```

#### Returns

_IDisposable_<br>
An `IDisposable` token restoring the previous CUDA context upon disposal.

## CreateScope()

Creates a nested GPU resource scope. Not currently supported on the root workspace.

```csharp
public IGpuResourceScope CreateScope();
```

#### Exceptions

`NotImplementedException`<br>
Thrown unconditionally as nested scopes are not yet supported on `GpuWorkspace`.

## Track(IDisposable)

Registers a disposable resource to be tracked and deterministically disposed upon workspace disposal.

```csharp
public void Track(IDisposable resource);
```

#### Parameters

`resource` _IDisposable_<br>
The disposable resource to track.

## Dispose()

Releases all device memory resources, tracked buffers, and workspace handles held by this workspace.

```csharp
public void Dispose();
```

## Dispose(Boolean)

Releases unmanaged and, optionally, managed resources. Safely aborts any leaked CUDA stream capture before releasing tracked resources, memory manager pools, and engine streams/contexts.

```csharp
protected virtual void Dispose(bool disposing);
```

#### Parameters

`disposing` _Boolean_<br>
`true` to release both managed and unmanaged resources; `false` to release only unmanaged resources.

## Remarks

### Composite Scope and Bulk Disposal Architecture

`GpuWorkspace` implements `IGpuResourceScope`, introducing a unified **composite scope (tray) pattern** for GPU memory and resource lifecycle management.

Prior to this architecture, GPU data structures required granular, per-field manual `.Dispose()` chains in constructor `catch` blocks and tear-down methods. Under the `Track()` model:
- **Automatic Registration**: Constructors of GPU data types (such as [GpuBigUInt](../gpu_big_int_and_uint/GpuBigUInt.md) and [GpuBigIntMatrix2x2](../gpu_big_int_and_uint/GpuBigIntMatrix2x2.md)) automatically register newly allocated instances into their parent `GpuWorkspace` via `Track(this)`.
- **Deterministic Bulk Disposal**: When `GpuWorkspace.Dispose()` is invoked (typically via a `using` statement), any leaked CUDA Graph capture on `Stream` is aborted first, and all tracked GPU buffers, memory manager pools, streams, and scratchpads are reclaimed collectively within a guarded `CudaContextScope`, eliminating manual disposal boilerplate and leak risks.
- **Double-Free Safety**: The underlying memory manager maintains active allocation registries to ensure that if a tracked resource is manually disposed early by the caller, redundant teardown during workspace disposal is safely ignored.

**Thread Safety:** Instance members are thread-safe for allocation pooling and resource tracking.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
// Execution Context: Host (CPU)
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

// 1. Create a GpuWorkspace with a specific capacity
using var workspace = new GpuWorkspace(maxWords: 1024, engine: GpuExecutionEngine.GSharpAot);

using (workspace.ActivateContext())
{
    // Instances auto-register via workspace.Track(this) inside their constructors
    var a = new GpuBigUInt(workspace, new uint[] { 42 });
    var b = new GpuBigUInt(workspace, new uint[] { 58 });

    Console.WriteLine($"MaxWords: {workspace.MaxWords}, Engine: {workspace.Engine}, a={a}, b={b}");
}
```

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuWorkspace](../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)

### Guides

- [4. Practical Guides and Best Practices](../../../04_guides/00_index.md)
- [7. Stdlib Math and Lazy Evaluation](../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
- [4. BigNum Expression Trees Guide](../../../04_guides/04_bignum_expression_trees_guide.md)
