---
title: "5. Barrier Uniformity Analysis"
site: "G# Documentation"
published: "2026-10-30T00:00:00.000Z"
language: "en"
description: "How the compiler decides which values, branches and memory may differ between threads, and which barriers only some threads reach (GSH0037, GSH0083, GSH0084)."
---

# 5. Barrier Uniformity Analysis

The analyzer reports a barrier that only some threads of a block (or of the grid) may reach: [GSH0037](../03_bcl_api/diagnostics/GSH0037.md), [GSH0083](../03_bcl_api/diagnostics/GSH0083.md) and [GSH0084](../03_bcl_api/diagnostics/GSH0084.md). This page describes how it decides that. The code is `UniformityAnalysis` and the types under `src/Compiler/Kassyi.GSharp.Compiler/Analysis/Uniformity/`.

*Note: You do not need to understand this to write kernels. The diagnostic pages say what is reported and what is not.*

## 5.1 Values and branches

The analysis is a forward data flow on the control flow graph of the method, with lambda and local function bodies and the bodies of `Gpu.ForUnroll` and `Gpu.SyncGrid` spliced in (`SplicedFlowBuilder`). Every value has a variance:

| Variance | Meaning |
| :--- | :--- |
| `Uniform` | The same in every thread of the grid |
| `Grid` | The same within a block, may differ between blocks (`BlockIndex`) |
| `Block` | May differ between the threads of a block (`ThreadIndex`) |

A branch whose condition varies makes the code it decides (its influence region, up to its immediate post-dominator) reached by only some threads, and the values written there differ where the paths meet again (LLVM's sync dependence). A barrier in such a region is divergent. `UniformityRecorder` records what each node writes and evaluates, `UniformitySolver` solves the values and the regions together, `BarrierClassifier` judges the barriers.

## 5.2 Memory (C9)

An element of memory that the threads share, read with a uniform index (`buffer[0]`), is not uniform by itself: another thread may have written it. Decision §5-4 of the barrier redesign follows the `noclobber` idea of AMDGPU: such a read is uniform only when no path from the last barrier to the read writes the same memory. `MemoryUniformity` follows this.

### What a memory is

A memory is named by the variable it is reached through, and each memory is a value of the data flow:

- a parameter of array, `Span<T>`, `ReadOnlySpan<T>` or `SharedMemory<T>` type;
- a field, or an auto-property, of such a type (the arrays of a struct, by field: one memory for every struct that holds the field);
- a `SharedMemory<T>` local that is not a copy of another;
- a `ref` or `out` parameter of a device function, so that its caller learns whether the callee left the location written.

A local of array, span or `SharedMemory<T>` type that is assigned another memory refers to it (`int[] alias = buffer;`), and so does a ref local bound to an element (`ref int first = ref buffer[0];`). Writes and reads through them reach every memory they may refer to; a ref local bound to a location that is not followed (a pointer) may reach any memory. An array a local creates (a stack array) belongs to its thread and is not memory.

### What writes and what lowers

| Event | Effect on the memory it reaches |
| :--- | :--- |
| An assignment, compound assignment, increment or deconstruction to an element, a ref local, a `ref` / `out` parameter, or the result of a device function that returns by reference | `Block`: may differ within the block |
| A block barrier | Everything not `Uniform` becomes `Grid`: the block is consistent, other blocks may still be writing |
| A grid barrier, or a gap of `Gpu.SyncGrid(body)` (between its statements, not before the first one) | Everything becomes `Uniform` |

A read takes the variance of the memory; a `SharedMemory<T>` read is never below `Grid`, as each block has its own.

### Calls

A device function with a body is analyzed for its caller, and its summary (`CalleeVariance`) records:

- for each parameter, the variance of its memory where the function returns, after its own barriers (`MemoryAtExit`), and the same for the memory of the fields of its struct parameters (`FieldMemoryAtExit`);
- the barrier that every path to its return passes (`BarrierOnEveryPath`): a must value that starts at `Block` and that only barriers lower, so a function that some threads leave early (a `return` before its barrier) does not count as synchronizing.

At the call, the caller's memory is first lowered as that barrier lowers it, then the memory of each argument (an array, a span, a `ref` element, the fields of a struct argument or receiver) takes the callee's exit variance. A helper that writes and then waits (a reduction) therefore leaves the memory consistent within the block; a helper that waits and then writes leaves it written. Any other call that may write memory (`DeviceFunctionEffects`) leaves what it reaches written; an intrinsic with a `ref` argument (an atomic) writes that element.

The memory summary does not depend on which arguments vary, so the call takes it with uniform argument masks.

### What is not followed

The assumptions that remain are listed on [GSH0083](../03_bcl_api/diagnostics/GSH0083.md) under "Not reported".

## See also

- [2. Compiler and Runtime Architecture](00_index.md)
- [GSH0083](../03_bcl_api/diagnostics/GSH0083.md)
