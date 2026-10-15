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

| Name | Description |
| --- | --- |
| [SyncGrid()](#syncgrid) | Synchronizes all threads across a cooperative thread grid. |
| [SyncGrid(Action)](#syncgrid-action) | Synchronizes all threads across a cooperative thread grid and automatically handles synchronization barriers across the provided block of statements. |

## SyncGrid()

Synchronizes all threads across a cooperative thread grid.

```csharp
public static void SyncGrid();
```

#### Exceptions

[GpuOnlyException](../01_diagnostics_and_exceptions.md#gpuonlyexception)<br>
Thrown when evaluated on the host CPU outside of a managed execution context.

## SyncGrid(Action)

Synchronizes all threads across a cooperative thread grid and automatically handles synchronization barriers across the provided block of statements.

```csharp
public static void SyncGrid(Action body);
```

#### Parameters

`body` _Action_<br>
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

### AutoSyncGrid Mechanics & Statement Boundaries

When using the delegate-based syntax (`Gpu.SyncGrid(() => { ... })`), the compiler activates the `AutoSyncGrid` mechanism to automatically interleave grid synchronization barriers (`cg::this_grid().sync();`) between statements within the block.

> [!IMPORTANT]
> **Key Operational Behaviors & Precautions**:
> 1. **Barriers Are Inserted Exclusively Between Statements (No Barrier After Final Statement)**:
>    Synchronization barriers are placed strictly **between** consecutive statements inside the lambda body. The compiler **does NOT** insert a barrier after the final statement in the block. If downstream operations outside the block require all threads in the grid to finish the final statement before proceeding, an explicit `Gpu.SyncGrid();` must be invoked immediately after the block.
> 2. **Barriers Interleave Across All Executable Statements (Including Variable Declarations & Assignments)**:
>    Every statement within the lambda body—including local variable declarations (e.g., `var args = ...;`) and property/field assignments (e.g., `args.Buffer = temp;`)—is treated as an executable statement that triggers an interleaved grid synchronization barrier. To prevent unnecessary GPU pipeline stalls and excessive grid synchronizations, prepare and configure all parameter structs and local variables **outside** the `Gpu.SyncGrid(() => { ... })` block, keeping only the kernel/device function invocations inside.
> 3. **Avoid Parallel-Dependent Constructs in Synchronized Blocks**:
>    Calling constructs that require independent thread-level parallel scheduling (e.g., `Gpu.GridStrideLoop`) within a single-thread context inside synchronized blocks will trigger diagnostic [GSH0035](../diagnostics/GSH0035.md).

#### Example 1: Terminal Barrier Handling

```csharp
// ⚠️ Potential Hazard: No barrier is emitted after Step2()
Gpu.SyncGrid(() =>
{
    ExecuteStep1();
    ExecuteStep2(); // ❌ cg::this_grid().sync() is NOT inserted here!
});
// If another kernel or subsequent statement expects Step2 to be fully finished across all blocks,
// threads might diverge or race without an explicit barrier.

// ✅ Recommended Pattern: Explicit trailing sync when downstream logic depends on block completion
Gpu.SyncGrid(() =>
{
    ExecuteStep1();
    ExecuteStep2();
});
Gpu.SyncGrid(); // Explicitly ensures all grid blocks finish Step2() before continuing.
```

#### Example 2: Variable Preparation Outside the Block

```csharp
// ❌ Anti-Pattern: In-block variable mutation introduces unwanted grid syncs
Gpu.SyncGrid(() =>
{
    var args = new TransformArgs { Length = 1024 };
    // ⚠️ cg::this_grid().sync() is inserted after the local variable declaration!
    ExecuteForwardNtt(args);
    // ⚠️ cg::this_grid().sync() is inserted after kernel invocation
    args.Buffer = tempBuffer;
    // ⚠️ cg::this_grid().sync() is inserted after assignment!
    ExecuteInverseNtt(args);
});

// ✅ Best Practice: Prepare variables outside; place only pure GPU invocations inside
var argsFwd = new TransformArgs { Length = 1024, Buffer = mainBuffer };
var argsInv = new TransformArgs { Length = 1024, Buffer = tempBuffer };

Gpu.SyncGrid(() =>
{
    ExecuteForwardNtt(argsFwd);
    ExecuteInverseNtt(argsInv); // Only one clean sync between the two stages
});
```

#### Example 3: Parallel-Dependent Calls (GSH0035 Guard)

```csharp
// ❌ Compiler Error (GSH0035): GridStrideLoop invoked from single-thread context
Gpu.SingleThread(() =>
{
    // GSH0035: Cannot invoke parallel-dependent method from single-thread context
    Gpu.GridStrideLoop(1024, i => data[i] = 0);
});

// ✅ Correct Pattern: Keep parallel-distributed loops outside single-thread blocks
Gpu.SingleThread(() =>
{
    dataLength = ComputeTotalWords();
});
Gpu.SyncGrid();

// Executes across all threads and blocks concurrently
Gpu.GridStrideLoop(dataLength, i =>
{
    data[i] = 0;
});
```

### Flattening and Nesting

The compiler supports **nested `SyncGrid` blocks by flattening them**.

### Empty Block Elimination (No-op)

If a `SyncGrid` block contains no executable statements, the compiler will completely eliminate it and treat it as a No-op. It will not generate any empty blocks or redundant synchronization barriers in the resulting CUDA C++ code.

## Examples

### Interleaved Statement Barriers

```csharp
Gpu.SyncGrid(() =>
{
    Kernel1();
    Kernel2();
    Kernel3();
});
```

Transpiled CUDA C++ output:

```cpp
{
    Kernel1();
    cg::this_grid().sync();
    Kernel2();
    cg::this_grid().sync();
    Kernel3();
    // Note: No synchronization barrier is inserted after the last statement.
}
```

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
{
    statement1();
    cg::this_grid().sync();
    statement2();
}
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
