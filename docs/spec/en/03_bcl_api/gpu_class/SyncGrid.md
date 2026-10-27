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
- **Top-Level Execution (Deadlock Prevention)**: `SyncGrid` statements must be executed by all threads in the grid. Divergent control flow will cause GPU deadlocks. Avoid placing `SyncGrid` inside conditional statements (`if`) unless the condition evaluates identically for all threads in the grid, and ensure no threads return early before reaching the barrier. A `return` inside a `Gpu.SyncGrid(() => { ... })` body is different: the thread still takes part in the automatic barriers of the body (see [Return in a Body](#return-in-a-body)).

### Runtime Deadlock Traps

While the compiler ensures your kernel is capable of grid synchronization (via `GSH0030`), **you must also ensure that all threads in the grid reach the barrier at runtime**.

**Divergent If Statements**:

```csharp
[GpuKernel(Cooperative = true)]
public static void DivergentIfDeadlock()
{
    // ❌ BAD: Divergent condition. Threads where X != 0 will not reach the barrier, causing a deadlock.
    if (ThreadIndex.X == 0)
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
    if (BlockIndex.X * BlockDimension.X + ThreadIndex.X >= data.Length)
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

**Barriers After a Return in a Body**:

A `return` in a `Gpu.SyncGrid(() => { ... })` body skips the remaining statements of the body on that thread, but the thread still passes the automatic grid barriers between them (and an explicit `Gpu.SyncGrid();` statement of the body). A barrier inside a skipped statement is different: `Gpu.SyncThreads()`, a warp shuffle, or a call of a device function that reaches one is not reached by the threads that returned. When the condition of the `return` differs between threads, that barrier diverges. Ensuring that does not happen is your responsibility; the compiler does not report it.

```csharp
[GpuKernel(Cooperative = true)]
public static void BarrierAfterDivergentReturn(int[] data)
{
    int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
    Gpu.SyncGrid(() =>
    {
        if (tid % 2 == 0) return;   // Even threads skip the rest of the body
        Gpu.SyncThreads();          // BAD: only the odd threads of the block reach this block barrier
        data[tid] += 1;
    });
}
```

### Automatic Grid Syncs in a Body

When using the delegate-based syntax (`Gpu.SyncGrid(() => { ... })`), the compiler inserts a grid synchronization barrier (`cg::this_grid().sync();`) between the top-level statements of the body.

> [!IMPORTANT]
> **Key Operational Behaviors & Precautions**:
> 1. **Barriers Are Inserted Only Between Top-Level Statements (No Barrier After Final Statement)**:
>    Synchronization barriers are placed strictly **between** consecutive statements of the lambda body. The compiler **does NOT** insert a barrier after the final statement. If downstream operations outside the block require all threads in the grid to finish the final statement before proceeding, an explicit `Gpu.SyncGrid();` must be invoked immediately after the block.
> 2. **A Nested Block Is One Statement**:
>    A `for` or `if` statement, the body of `Gpu.ForUnroll`, `Gpu.GridStrideLoop` or `Gpu.SingleThread`, and the body of a local function are each one statement of the body. Nothing inside them is synchronized: a grid sync that only some threads reach (a `GridStrideLoop` body runs a different number of times on each thread, an `if (ThreadIndex.X == 0)` block runs on some threads only) would deadlock. To synchronize between the statements of a uniform loop, write a `Gpu.SyncGrid(() => { ... })` inside the loop (Example 4).
> 3. **Barriers Interleave Across All Executable Statements (Including Variable Declarations & Assignments)**:
>    Every top-level statement of the lambda body—including local variable declarations (e.g., `var args = ...;`) and property/field assignments (e.g., `args.Buffer = temp;`)—is treated as an executable statement that triggers an interleaved grid synchronization barrier. To prevent unnecessary GPU pipeline stalls and excessive grid synchronizations, prepare and configure all parameter structs and local variables **outside** the `Gpu.SyncGrid(() => { ... })` block, keeping only the kernel/device function invocations inside. An empty statement (also with a label, `L: ;`) and a local function declaration do not run, so no barrier is placed before or after them, and no automatic barrier is placed next to an explicit `Gpu.SyncGrid();`.
> 4. **Avoid Parallel-Dependent Constructs in Synchronized Blocks**:
>    Calling constructs that require independent thread-level parallel scheduling (e.g., `Gpu.GridStrideLoop`) within a single-thread context inside synchronized blocks will trigger diagnostic [GSH0035](../diagnostics/GSH0035.md).
> 5. **CPU Dialects**:
>    On the CPU dialects the body runs as states of the kernel's state machine: each barrier ends a state, and the whole grid reaches it before any thread continues (managed execution). The barriers are at the same places as in the CUDA output.

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

#### Example 4: Synchronizing Inside a Uniform Loop

```csharp
Gpu.SyncGrid(() =>
{
    Prepare();
    for (int k = 0; k < 2; k++) // The same number of iterations on every thread
    {
        // The loop is one statement of the outer body; this body syncs between its own statements
        Gpu.SyncGrid(() =>
        {
            Step1(k);
            Step2(k);
        });
    }
    Finish();
});
```

### Flattening and Nesting

The compiler supports **nested `SyncGrid` blocks by flattening them**. A `Gpu.SyncGrid(() => { ... })` that is a top-level statement of another body (or the expression body of one, `Gpu.SyncGrid(() => Gpu.SyncGrid(() => { ... }))`) adds its statements to the gaps of the enclosing body. Each nested body is still emitted in a scope of its own, so its locals stay its own, as in C#.

### Return in a Body

A `return` in the body ends the body for the thread, as it ends the lambda in C#: the thread runs none of the remaining statements of the body, and the kernel continues after the `Gpu.SyncGrid` call. The thread still takes part in the grid synchronizations of the remaining statements, so a `return` whose condition differs between threads does not deadlock at the automatic barriers.

- A `return` in a nested body ends only that body. A `return` in a lambda or a local function inside the body leaves that function, as usual.
- A local declared after a `return` (also an `out` variable, `Read(out var x);`) stays visible to the statements after it; its initializer does not run on the threads that returned.
- Reported as [GSH0003](../diagnostics/GSH0003.md): after a `return`, a `ref`, `scoped` or `using` local, a deconstruction (`var (a, b) = ...`), a pattern variable (`if (x is int v)`) that stays in scope after its statement, and a local of a `SharedMemory<T>`, array-like or iterator type; anywhere in the body, a `goto` to a label outside its own statement.

```csharp
[GpuKernel(Cooperative = true)]
public static void ReturnInBody(int[] a, int[] b, int n)
{
    int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
    Gpu.SyncGrid(() =>
    {
        if (tid >= n) return;          // Ends the body for this thread only
        int v = a[tid];                // Not run by the threads that returned
        b[tid] = v + a[(tid + 1) % n]; // The threads that returned still pass the grid sync before this statement
    });
    a[tid] += 1;                       // Runs on every thread
}
```

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

Will be transpiled as flattened sequential statements with barriers safely inserted at the boundaries; the nested body keeps a scope of its own:

```cpp
{
    statement1();
    cg::this_grid().sync();
    {
        statement2();
    }
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
