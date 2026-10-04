---
title: "GpuWorkspace Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Represents a GPU execution workspace managing memory allocation pools, device workspaces, resource tracking, and backend providers for large integer computations."
---

## GpuWorkspace Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Represents a GPU execution workspace managing memory allocation pools, device workspaces, composite resource tracking, and backend providers for large integer computations.

```csharp
public class GpuWorkspace : IGpuResourceScope, IDisposable
```

## Constructors

| Name | Description |
| --- | --- |
| [GpuWorkspace(int, GpuExecutionEngine)](#gpuworkspaceint-gpuexecutionengine) | Initializes a new instance of the `GpuWorkspace` class with the specified limb capacity and engine backend. |

## GpuWorkspace(int, GpuExecutionEngine)

Initializes a new instance of the `GpuWorkspace` class with the specified limb capacity and engine backend.

```csharp
public GpuWorkspace(int maxWords, GpuExecutionEngine engine = GpuExecutionEngine.GSharpAot);
```

#### Parameters

`maxWords` _int_<br>
The maximum word count for BigInteger operations.

`engine` _GpuExecutionEngine_<br>
The GPU execution backend engine to utilize.

#### Exceptions

`InvalidOperationException`<br>
Thrown when CUDA context or native workspace allocation fails.

## Properties

| Name | Description |
| --- | --- |
| [DeviceSmCount](#devicesmcount) | Gets the number of streaming multiprocessors (SMs) on the active CUDA device. |
| [Engine](#engine) | Gets the active GPU execution engine type. |
| [MaxWords](#maxwords) | Gets the maximum number of 32-bit limbs (words) supported in this workspace's fixed allocations. |
| [MemoryManager](#memorymanager) | Gets the memory manager for this workspace. |
| [Provider](#provider) | Gets the active GPU arithmetic execution provider implementation. |
| [Stream](#stream) | Gets the dedicated CUDA Stream for this workspace, used for operations and CUDA Graph capture. |
| [WorkspacePointer](#workspacepointer) | Gets the raw native device pointer to the allocated workspace scratchpad memory (used by legacy native backend). |

## DeviceSmCount

Gets the number of streaming multiprocessors (SMs) on the active CUDA device.

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

## Provider

Gets the active GPU arithmetic execution provider implementation.

```csharp
public IGpuExecutionProvider Provider { get; }
```

## Stream

Gets the dedicated CUDA Stream for this workspace, used for operations and CUDA Graph capture.

```csharp
public IntPtr Stream { get; }
```

## WorkspacePointer

Gets the raw native device pointer to the allocated workspace scratchpad memory (used by legacy native backend).

```csharp
public IntPtr WorkspacePointer { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [CreateScope()](#createscope) | Creates a nested scope. Not currently implemented for the root workspace. |
| [Dispose()](#dispose) | Releases all device memory resources, tracked buffers, and workspace handles held by this workspace. |
| [Dispose(bool)](#disposebool) | Releases unmanaged and, optionally, managed resources. |
| [Track(IDisposable)](#trackidisposable) | Registers a disposable resource to be tracked and collectively disposed by this workspace. |

## CreateScope()

Creates a nested scope. Not currently implemented for the root workspace.

```csharp
public IGpuResourceScope CreateScope();
```

## Track(IDisposable)

Registers a disposable resource to be tracked and collectively disposed by this workspace.

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

## Dispose(bool)

Releases unmanaged and, optionally, managed resources.

```csharp
protected virtual void Dispose(bool disposing);
```

#### Parameters

`disposing` _bool_<br>
`true` to release both managed and unmanaged resources; `false` to release only unmanaged resources.

## Remarks

### Composite Scope and Bulk Disposal Architecture

`GpuWorkspace` implements `IGpuResourceScope`, introducing a unified **composite scope (tray) pattern** for GPU memory and resource lifecycle management.

Prior to this architecture, GPU data structures required granular, per-field manual `.Dispose()` chains in constructor `catch` blocks and tear-down methods. Under the `Track()` model:
- **Automatic Registration**: Constructors of GPU data types (such as <see cref="GpuBigUInt"/> and <see cref="GpuBigIntMatrix2x2"/>) automatically register newly allocated instances into their parent `GpuWorkspace` via `Track(this)`.
- **Deterministic Bulk Disposal**: When `GpuWorkspace.Dispose()` is invoked (typically via a `using` statement), all tracked GPU buffers, streams, and scratchpads are reclaimed collectively within a guarded <c>CudaContextScope</c>, eliminating manual disposal boilerplate and leak risks.
- **Double-Free Safety**: The underlying memory manager maintains active allocation registries to ensure that if a tracked resource is manually disposed early by the caller, redundant teardown during workspace disposal is safely ignored.

**Thread Safety:** Instance members are thread-safe for allocation pooling and resource tracking.

## Examples

### Allocating a Workspace and Native Compilation Engine

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.GpuMath;

// 1. Create a GpuWorkspace with a specific capacity
using var workspace = new GpuWorkspace(maxWords: 1024, engine: GpuExecutionEngine.GSharpAot);

// 2. Output properties to verify initialization
// Outputs: MaxWords: 1024, Engine: GSharpAot
Console.WriteLine($"MaxWords: {workspace.MaxWords}");
Console.WriteLine($"Engine: {workspace.Engine}");
```

### Automatic Resource Tracking and Bulk Disposal

GPU data structures automatically register with the parent workspace upon creation, enabling deterministic bulk cleanup without manual disposal chains:

```csharp
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

void ComputeWithAutoCleanup()
{
    // The workspace acts as the root resource tray
    using var workspace = new GpuWorkspace(maxWords: 4096);

    // Instances auto-register via workspace.Track(this) inside their constructors
    var a = new GpuBigUInt(workspace, 128);
    var b = new GpuBigUInt(workspace, 128);
    var matrix = GpuBigIntMatrix2x2.Identity(workspace, 128);

    // Custom disposable GPU resources can also be tracked explicitly:
    // workspace.Track(customDisposableResource);

    // Perform arithmetic operations...
    // All resources (a, b, matrix) are automatically and safely freed when
    // the workspace scope exits, eliminating repetitive individual Dispose() calls.
}
```

### Explicit Dispose() and Try-Finally Pattern

When a `using` statement cannot be used (e.g., long-lived operations or manual lifecycle control), invoke `workspace.Dispose()` explicitly:

```csharp
var workspace = new GpuWorkspace(maxWords: 2048);
try
{
    var x = new GpuBigUInt(workspace, 64);
    var y = new GpuBigUInt(workspace, 64);

    // Compute...
}
finally
{
    // Explicit bulk disposal: Reclaims workspace device memory and all tracked operands (x, y)
    workspace.Dispose();
}
```

### Implementing IDisposable in Classes Holding Multiple GPU Resources

When designing custom classes or runner services that allocate multiple GPU resources, hold a `GpuWorkspace` and delegate disposal entirely to `_workspace.Dispose()`. Avoid chaining individual `.Dispose()` calls:

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

public sealed class GpuTransformPipeline : IDisposable
{
    private readonly GpuWorkspace _workspace;
    private readonly GpuBigUInt _source;
    private readonly GpuBigUInt _target;
    private readonly GpuBigIntMatrix2x2 _matrix;
    private bool _disposed;

    public GpuTransformPipeline(int numWords)
    {
        try
        {
            _workspace = new GpuWorkspace(numWords);
            _source = new GpuBigUInt(_workspace, numWords);
            _target = new GpuBigUInt(_workspace, numWords);
            _matrix = GpuBigIntMatrix2x2.Identity(_workspace, numWords);
        }
        catch
        {
            // [WHY] If initialization fails midway, disposing the workspace bulk-reclaims
            // all resources tracked up to that point. No manual per-field cleanup needed.
            _workspace?.Dispose();
            throw;
        }
    }

    public void Execute()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Execute GPU operations...
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // [WHY] Bulk disposal: Calling _workspace.Dispose() automatically frees
        // _source, _target, and _matrix. Do NOT call _source.Dispose(), etc. individually.
        _workspace.Dispose();
        _disposed = true;
    }
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
