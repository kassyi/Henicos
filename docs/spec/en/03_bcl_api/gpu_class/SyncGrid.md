---
title: "Gpu.SyncGrid Method (G#)"
site: "G# Documentation"
published: "2026-09-26T00:00:00.000Z"
language: "en"
description: "Synchronizes all threads across a cooperative thread grid."
---

## Gpu.SyncGrid Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Synchronizes all threads across a cooperative thread grid.

## Overloads

| Name                                 | Description                                                                                                                                           |
| ------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
| [SyncGrid()](#syncgrid)              | Synchronizes all threads across a cooperative thread grid.                                                                                            |
| [SyncGrid(Action)](#syncgrid-action) | Synchronizes all threads across a cooperative thread grid and automatically handles synchronization barriers across the provided block of statements. |

## SyncGrid()

Synchronizes all threads across a cooperative thread grid.

```csharp
[GpuKernel]
public static void SyncGrid();
```

#### Exceptions

[GpuOnlyException](../01_diagnostics_and_exceptions.md#gpuonlyexception)<br>
Thrown when evaluated on the host CPU.

## SyncGrid(Action)

Synchronizes all threads across a cooperative thread grid and automatically handles synchronization barriers across the provided block of statements.

```csharp
[GpuKernel]
public static void SyncGrid(Action action);
```

#### Parameters

`action` <i>Action</i><br>
A delegate or lambda expressing the body to execute with synchronization barriers.

#### Exceptions

[GpuOnlyException](../01_diagnostics_and_exceptions.md#gpuonlyexception)<br>
Thrown when evaluated on the host CPU.

## Remarks

The `Gpu.SyncGrid` method acts as a barrier at which all threads in a cooperative grid must wait before any thread is allowed to proceed. This is transpiled to `cg::this_grid().sync();` using the Cooperative Groups API in CUDA C++.

- **Lambda Expression Requirement**: The `Gpu.SyncGrid(Action)` overload strictly requires an inline lambda expression. Passing a delegate variable or method group will result in a compiler error. See [GSH0003](../diagnostics/GSH0003.md) for details.
- **Cooperative Kernel Requirement**: `SyncGrid` requires the kernel to be launched as a cooperative kernel. Using this method in a non-cooperative kernel will result in a compiler error. See [GSH0030](../diagnostics/GSH0030.md) for details.
- **Top-Level Execution (Deadlock Prevention)**: `SyncGrid` statements must be executed by all threads in the grid. Divergent control flow will cause GPU deadlocks. Avoid placing `SyncGrid` inside conditional statements (`if`) unless the condition evaluates identically for all threads in the grid, and ensure no threads return early before reaching the barrier.

### Runtime Deadlock Traps

While the compiler ensures your kernel is capable of grid synchronization (via `GSH0030`), **you must also ensure that all threads in the grid reach the barrier at runtime**.

**Divergent If Statements**:

```csharp
[GpuKernel(Cooperative = true)]
public static void DivergentIfDeadlock()
{
    // ❌ BAD: Divergent condition. Threads where X != 0 will not reach the barrier, causing a deadlock.
    if (Gpu.ThreadIndex.X == 0)
    {
        Gpu.SyncGrid();
    }

    // ✅ GOOD: Identical condition for all threads in the grid.
    if (Context.IsGlobalConditionMet)
    {
        Gpu.SyncGrid();
    }
}
```

**Early Returns**:

```csharp
[GpuKernel(Cooperative = true)]
public static void EarlyReturnDeadlock(int[] data)
{
    if (Gpu.GlobalThreadIndex.X >= data.Length)
    {
        // ❌ BAD: Out-of-bounds threads return early and disappear.
        // They will never reach the SyncGrid below, deadlocking the surviving threads!
        return;
    }

    // Surviving threads wait forever for the early-returned threads.
    Gpu.SyncGrid();
}
```

_To fix the early return deadlock, either ensure `SyncGrid` happens before the early return, or use thread-masking techniques (like `SingleThread`) instead of early returns._

### Flattening and Nesting

When using the delegate-based syntax (`Gpu.SyncGrid(() => { ... })`), the compiler handles the synchronization barrier automatically across the provided block of statements. Furthermore, the compiler supports **nested `SyncGrid` blocks by flattening them**.

### Empty Block Elimination (No-op)

If a `SyncGrid` block contains no executable statements, the compiler will completely eliminate it and treat it as a No-op. It will not generate any empty blocks or redundant synchronization barriers in the resulting CUDA C++ code.

## Examples

### Nested Flattening

A nested `SyncGrid` structure:

```csharp
Gpu.SyncGrid(() =>
{
    statement1();
    Gpu.SyncGrid(() =>
    {
        statement2();
    });
});
```

Will be transpiled as flattened sequential statements with barriers safely inserted at the boundaries:

```cpp
cg::this_grid().sync();
statement1();
cg::this_grid().sync();
statement2();
cg::this_grid().sync();
```

### Empty Block Elimination

An empty block generates zero output in C++:

```csharp
Gpu.SyncGrid(() => { }); // No-op
```

## See Also

### BCL API Reference

- [BCL API and Hardware Intrinsics Reference](../00_index.md)
- [SyncThreads](SyncThreads.md)
- [SingleThread](SingleThread.md)
- [GpuKernelAttribute](../attributes/GpuKernelAttribute.md)
- [Diagnostics and Exceptions](../01_diagnostics_and_exceptions.md)

### Transpiler Diagnostics

- [Diagnostic Reference Catalog](../diagnostics/00_index.md)
- [GSH0030: SyncGrid requires a cooperative kernel](../diagnostics/GSH0030.md)
- [GSH0003: Unsupported syntax in GPU kernel](../diagnostics/GSH0003.md)

### Guides

- [Practical Guides and Best Practices](../../04_guides/00_index.md)
- [Warp Primitives and Thread Masking Guide](../../04_guides/01_warp_programming_guide.md)
- [The CPU to GPU Paradigm Shift: Anti-Patterns & Best Practices](../../04_guides/06_antipatterns_and_best_practices.md)
