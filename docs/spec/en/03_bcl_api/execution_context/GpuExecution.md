---
title: "GpuExecution Class and KernelExecution Enum (G#)"
site: "G# Documentation"
published: "2026-11-12T00:00:00.000Z"
language: "en"
description: "Selects how the generated launchers run kernels inside a scope: the default path, the managed CPU path, or the kernel's C# body one GPU thread at a time."
---

## GpuExecution Class

## Definition

**Namespace:** `Kassyi.GSharp.Runtime`  
**Assembly:** `Kassyi.GSharp.Runtime.dll`

Selects how the generated `Launch{Kernel}` methods run kernels for the code inside a scope, the launches that libraries
make included.

```csharp
public enum KernelExecution
{
    Default,
    Managed,
    CSharp
}

public static class GpuExecution
{
    public static KernelExecution Current { get; }
    public static GpuExecutionScope Use(KernelExecution execution);
}

public readonly struct GpuExecutionScope : IDisposable
{
    public void Dispose();
}
```

## KernelExecution Values

| Name | What a launch inside the scope does |
| --- | --- |
| `Default` | The build's own path: the GPU on a CUDA build; the native CPU library on a CPU build, or the managed CPU path when the library is not there. |
| `Managed` | The managed CPU path on a CPU build, even when the native CPU library is there: the C# state machine that G# generates for the kernel, which reproduces barriers, warp intrinsics and shared memory. A CUDA build still runs on the GPU. |
| `CSharp` | The kernel's own C# method, once per GPU thread of the launch, one thread after another: the value that C# gives for each thread. |

## Members

| Name | Description |
| --- | --- |
| `GpuExecution.Current` | Gets how the launches of the current asynchronous flow run kernels; `Default` outside every scope. |
| `GpuExecution.Use(KernelExecution)` | Runs the launches of the current asynchronous flow as the given value until the returned scope is disposed, which restores the previous value. Throws `ArgumentOutOfRangeException` for a value outside the enumeration. |
| `GpuExecutionScope.Dispose()` | Restores the value that was in effect when the scope began. |

## Examples

### Running a Kernel's C# Body as the Reference Result

```csharp
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;

// Execution Context: Host

public static partial class AddKernels
{
    [GpuKernel]
    public static void AddKernel(int[] a, int[] b, int[] result)
    {
        int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i < result.Length)
        {
            result[i] = a[i] + b[i];
        }
    }
}

public static class Reference
{
    public static int[] Add(int[] a, int[] b)
    {
        var result = new int[a.Length];
        using (GpuExecution.Use(KernelExecution.CSharp))
        {
            // The same launcher as in production; inside the scope it runs AddKernel as C#, thread by thread
            AddKernels.LaunchAddKernel(a, b, result, gridDimX: (uint)(a.Length + 255) / 256, blockDimX: 256);
        }

        return result;
    }
}
```

## Remarks

### The Scope

The selection follows the asynchronous flow, as an `AsyncLocal<T>` does: tasks started inside the scope, `await`
continuations and threads started inside it see it; a thread that was already running, and the code after the scope,
do not. Scopes nest; dispose them in the reverse order, as `using` does. Every launch inside the scope follows the
selection, also the launches that a library makes on its own.

### What `KernelExecution.CSharp` Computes

The launcher checks its arguments and the launch dimensions as it always does, then calls the kernel method that the
user wrote, not the code that G# generates from it, once per GPU thread of the launch. The threads run one after
another, in the order of their linear index (x fastest, then y and z; the blocks in the same order), on a host thread
of their own, each with its coordinates set, so `ThreadIndex`, `BlockIndex`, `BlockDimension` and `GridDimension` read
the values of that thread. The result is the value that C# gives for each thread when no other thread runs at the same
time: the reference that the GPU, native CPU and managed results are compared with.

- The arguments are the ones passed to the launcher, as a C# call takes them. The copies that the launchers make for
  `[In]` arrays do not apply: a write to such an array reaches the caller's array.
- A `ref` or `out` argument is copied in before the first thread and back after the last one, as the GPU launchers
  copy it to the device and back.
- A kernel that returns a value fills the launcher's result array, one element per thread.
- The first exception of a thread ends the launch and reaches the caller as C# throws it, with the kernel method on its
  stack trace.
- Kernels run on the CPU only; no GPU or native library is needed.

### Kernels That Cannot Run One Thread at a Time

A kernel that reaches an operation that waits for or reads other threads has no C# value of its own, so the launcher
throws `InvalidOperationException` instead of running it, before any thread runs. The message names the kernel and the
construct:

- a block or grid barrier (`Gpu.SyncThreads()`, `Gpu.SyncGrid()`), an mbarrier,
- a warp shuffle, vote, match, reduction or WMMA operation, `Warp.ActiveMask()`,
- `SharedMemory<T>`,
- code whose meaning only the generated code gives: an operator macro (`[GpuOperatorMacro]`), a `float` or `double`
  StackLinq `Sum` (summed in order by the generated code), `Gpu.InlinePtx`.

The compiler finds these in the kernel and in every device function it calls. What it cannot see, such as an operation
reached through a delegate, throws `InvalidOperationException` when a thread reaches it. Launch
such a kernel under `Default` or `Managed`. A device function of another assembly is judged by what its assembly
recorded when it was built; one built before the record said whether it reaches static shared memory is caught when a
thread reaches the declaration. The `Launch{Kernel}Ptr` launchers take the arguments in the form the device uses; they
throw `InvalidOperationException` inside the scope.

### Reflection and Dynamic Calls

A kernel method called through reflection, `dynamic` or a delegate is a plain C# call: it runs once, with no launch
and no thread context, and a GPU intrinsic in it throws `GpuOnlyException`. Such calls are the caller's
responsibility; `GpuExecution` affects only the generated launchers.

The thread context that the C# execution sets is the one of [`ManagedGpuContext`](ManagedGpuContext.md), whose
members the code G# generates writes from the user's assembly. They stay public, hidden from IntelliSense only; code
that writes them itself changes what the intrinsics read on that thread, which G# does not support.

## See also

### BCL API > Execution Context

- [Execution Context](00_index.md)
- [ManagedGpuContext](ManagedGpuContext.md)
- [ThreadIndex](ThreadIndex.md)

### BCL API > Host Runtime

- [CpuKernelLoader Class (G#)](../host_runtime/CpuKernelLoader.md)

### BCL API > Diagnostics

- [GSH0031: Direct invocation of [GpuKernel] is prohibited](../diagnostics/GSH0031.md)
