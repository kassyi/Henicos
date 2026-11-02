---
title: "6. Semantics Charter"
site: "G# Documentation"
published: "2026-10-28T00:00:00.000Z"
language: "en"
description: "Which definition a G# kernel follows when C#, the GPU and the CPU evaluation paths disagree, and how G# treats undefined behavior."
---

# 6. Semantics Charter

A G# kernel is C# that runs on an NVIDIA GPU. Three sources could define what a kernel means:

- **C#**: the C# language specification and the documented behavior of the .NET APIs the kernel calls.
- **The GPU**: CUDA C++ and PTX, as executed by the hardware.
- **The CPU paths**: the native C++ build and the managed C# path. They exist to evaluate kernels without a GPU.

This page fixes which source wins in every combination. Every compiler rule and every design follows it.

## 6.1 Principles

1. **The GPU is the target.** The G in G# is the GPU. The CPU paths reproduce the GPU result and check it. G# never makes the GPU slower to match the CPU.
2. **What you write is what runs.** Each GPU thread executes the C# it was given. G# does not add work, insert synchronization, or change how many times something runs unless you asked for it.
3. **Extra meaning needs a marker.** Syntax sugar is welcome, but only behind a marker you wrote: an API such as `AsParallel()` or `Gpu.SyncGrid(body)`, an attribute, or a modifier, in the way `async` marks a method for `await`. Once the marker is there, the compiler may implement its promise in the fastest form that keeps the promised result.
4. **Undefined behavior is an error.** When neither C# nor the GPU defines the result, G# reports an error. It does not quietly pick a meaning.

## 6.2 Which definition wins

| C# | GPU | Rule | Examples |
| :--- | :--- | :--- | :--- |
| Defines the result | Same result | No conflict | Ordinary arithmetic |
| Defines the result | Undefined | **Follow C#** on the GPU and the CPU | Shift counts are masked; signed integer overflow wraps; integer division by zero, an out-of-range index and reading a null array (`Length`, an element, `foreach`) throw; operands are evaluated left to right |
| Defines the result | Defines a different result | **Follow the GPU**, except value contracts that C# fixes exactly, which follow C# | Math function accuracy, fused multiply-add, denormal handling follow the GPU; `Math.Round` rounding to even, comparisons and conversions follow C# (`uint` mixed with `int` is computed as `long`) |
| Does not define it (G#-specific APIs) | Defines the result | **Follow the GPU**; the CPU paths match it, even where the CPU would define something else | Warp shuffles, barriers |
| Does not define it | Undefined | **Error** (§6.3) | A data race on the same memory, reading uninitialized shared memory, a barrier in divergent control flow |

Where the GPU result differs from C# (math accuracy, for example), the documentation of the API states the GPU result. An exact variant, when one exists, is a separate API that you call by name.

## 6.3 Undefined behavior is an error

G# treats a form that neither C# nor the GPU defines like `1 / 0` in C#:

| When it is known | C# `1 / 0` | G# |
| :--- | :--- | :--- |
| At compile time, with certainty | `1 / 0` is a compile error | A compile error. The message shows the explicit form that does what you meant |
| Only at run time | `1 / x` throws | A run-time error, with the source location, on the native CPU build, the managed path and the checked CUDA build |
| In the release CUDA build | (C# always checks) | Not checked: detecting races while running is too slow for production |

- A compile error is reported only when the analysis is certain. A form that the analysis can only suspect compiles and is left to the run-time checks, so correct code is never rejected.
- A result that a CPU path happens to produce for such a form is never treated as correct. A CPU result that differs from the GPU result points to undefined behavior on the GPU first.
- When a GPU thread traps through the runtime error channel (`__gsharp_trap` / `__gsharp_trap_site`), the first trapping thread records the `GSharpErrorCode`, the compile-time error site ID, and its 3D thread and block coordinates in a pinned host-mapped error slot (`__gsharp_error_slot`) before executing `trap;`. On the host, the next synchronization point (or the post-launch stream synchronization emitted when `<GSharpCheckedBuild>true</GSharpCheckedBuild>` is enabled; Release builds never add implicit synchronization) translates the slot into the matching standard .NET exception (`IndexOutOfRangeException`, `DivideByZeroException`, `OverflowException`, `ArgumentOutOfRangeException`, `ArgumentException`, `InvalidOperationException`, `NullReferenceException`, or `NotSupportedException`) with `InnerException` set to `CudaException` and `Exception.Data` populated with `GSharp.ErrorCode`, `GSharp.ThreadIndex`, `GSharp.BlockIndex`, `GSharp.MemberName`, and `GSharp.SourceLocation`.

## 6.4 The cost of following C#

Some C# definitions cost time on the GPU: bounds checks and division-by-zero checks add a test to each access or division.

- By default, G# follows C# and emits the check.
- The compiler removes the check where it can prove the access is in range, as the .NET JIT does for loop indices.
- Removing the remaining checks for speed is explicit: you write a marker, in the same way `[SkipLocalsInit]` turns off zero-initialization in C#.

## 6.5 The CPU paths

- The native CPU build and the managed path must produce the GPU result.
- A form whose GPU result the CPU cannot reproduce exactly is not supported on the CPU. G# works around technical obstacles, but not limits that come from the definition of the form itself.
- The CPU paths are also checkers: they report the undefined behavior of §6.3 instead of hiding it.

## 6.6 Status

This charter was adopted on 2026-10-28. The compiler is being brought in line with it: some forms in §6.2 and §6.3 are not yet checked or diagnosed, and the checked CUDA build does not exist yet. Until then, [5. Limitations and Diagnostics](05_limitations.md) lists what the compiler reports today.

## See also



### Language Specification

- [1. Language Specification](00_index.md)
- [4. Execution and Concurrency Model](04_execution_model.md)
- [5. Limitations and Diagnostics](05_limitations.md)

### Getting Started

- [2. The Mental Model & Safety Net](../00_getting_started/02_mental_model.md)
