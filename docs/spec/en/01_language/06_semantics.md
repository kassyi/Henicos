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

- The release CUDA build keeps a check placed in a branch that only an unmatched case reaches, such as the trap after the last arm of a switch expression without a discard arm: removing it would leave undefined behavior in the output.
- A compile error is reported only when the analysis is certain. A form that the analysis can only suspect compiles and is left to the run-time checks, so correct code is never rejected.
- A result that a CPU path happens to produce for such a form is never treated as correct. A CPU result that differs from the GPU result points to undefined behavior on the GPU first.
- When a GPU thread fails a check through the runtime error channel (`__gsharp_trap` / `__gsharp_trap_site`), it records the `GSharpErrorCode`, the compile-time error site ID, and its 3D thread and block coordinates in the error record of its launch (the first failing thread of the launch wins), and ends there: on sm_70 and later it exits, so the kernel completes and the context stays usable. The launcher of the launch (or, for a `Ptr` launch, the next synchronization point; the post-launch stream synchronization emitted when `<GSharpCheckedBuild>true</GSharpCheckedBuild>` is enabled covers both) translates the record into the matching standard .NET exception (`IndexOutOfRangeException`, `DivideByZeroException`, `OverflowException`, `ArgumentOutOfRangeException`, `ArgumentException`, `InvalidOperationException`, `SwitchExpressionException`, `NullReferenceException`, or `NotSupportedException`) with no `InnerException`, and `Exception.Data` populated with `GSharp.ErrorCode`, `GSharp.ThreadIndex`, `GSharp.BlockIndex`, `GSharp.MemberName`, and `GSharp.SourceLocation`.
- After a thread of a launch failed, the other threads run to their next grid barrier (`Gpu.SyncGrid`, the grid strategy of `AsParallel()` aggregates), their next mbarrier wait, or the end of the kernel. A grid barrier that a block reaches after the failure does not complete: every block leaves it, and no thread runs the code after it. Block barriers (`Gpu.SyncThreads`) and warp collectives are not affected, since they wait only for the threads that have not exited. Results written before the failure stay in the output; results the failing thread would have written are not defined. A block that arrived at a grid barrier just before the failure was signaled can still complete it for the blocks that wait there.
- The native CPU build and the managed path stop the failing thread in the same way: it writes nothing after the failing check, and the other threads run on as above (a block barrier no longer counts the failed thread, a warp collective leaves it out as a lane that has exited, and a cooperative launch ends at its next grid barrier). The launch throws the exception of the first failure. When several threads fail, which one the launch reports is not fixed: on CUDA and the native CPU build it is the thread that records its failure first, which can change from run to run; the managed path steps the threads one after another and reports the first failure in that order (the lowest thread among those that fail before the same barrier). A failure inside a `using` disposes the resource as the failure leaves the `using`, as C# does in a `finally`, and an exception of that `Dispose` replaces the first one; the CUDA build does not dispose the resource of a failing thread yet.
- `Gpu.SyncGrid(GridSyncMode.Fast)` gives up part of that guarantee for speed, and only where it is written (§6.4): the arriving block reads the failure signal after its arrival instead of before it, which saves about 0.3 µs per barrier on the CUDA build (about a quarter of a barrier of a full grid). After a failure, the arrival of a block of the failing launch can then complete the barrier, and the other blocks run the code after it up to their next grid barrier, mbarrier wait or the end of the kernel, where they leave. The failure is detected in the same way, the launch throws the same exception, and no thread is left waiting at the barrier; only the results written in that one extra interval may differ from run to run. The code of that interval may read data that the failing block never wrote, so a loop whose count comes from such data can run for a long time before the block reaches the point where it leaves; use `GridSyncMode.Deterministic` when the code after the barrier depends on that. The default, `GridSyncMode.Deterministic`, keeps the behavior of the previous item, and so do the barriers that `Gpu.SyncGrid(body)` inserts. The mode must be a compile-time constant (GSH0012): the CUDA build calls one barrier or the other and does not choose at run time. The native CPU build and the managed path run a cooperative kernel in grid phases and treat both modes alike.

## 6.4 The cost of following C#

Some C# definitions cost time on the GPU: bounds checks and division-by-zero checks add a test to each access or division.

- By default, G# follows C# and emits the check.
- The compiler removes the check where it can prove the access is in range, as the .NET JIT does for loop indices.
- An integer `/` or `%` (and `/=`, `%=`) whose divisor may be 0 throws `DivideByZeroException` on every path: the
  native CPU build, the managed path and the CUDA build, release included (the failing CUDA thread records the error
  and exits, §6.3). A divisor whose constant or type excludes 0 has no check (`x / 7`).
  `MinValue / -1` and `MinValue % -1`, which C# leaves implementation-defined in an unchecked context, give the GPU
  result, `MinValue` and `0`, on every path: the native CPU build tests for `-1`, and the managed path calls a G#
  function for a division that may reach that case, in a kernel and in a device function alike, where the C# operator
  would throw `OverflowException`. The C# execution (`KernelExecution.CSharp`) runs the user's C# as it is and throws
  it. The managed path throws it too in three forms that keep the C# operator:
  - a device function declared in a `struct` that is not `partial` (a class that holds device functions must be
    `partial`, GSH0036): the rewritten copy of the function is written into a partial declaration of its types, and
    such a function has none;
  - the compound assignment (`/=`, `%=`) of an indexer whose receiver or argument writes, may throw, or reads what
    the right side writes (`g[k++] /= -1`): the getter and the setter would evaluate it twice;
  - the compound assignment of a property of a value type whose receiver is not a variable (the result of a call), or of
    a property of a type parameter.
  The other compound assignments are rewritten, also where the right side writes the variable of the target or the
  receiver of the property: the target is found once and read before the right side.
- The null check of an array read is removed where the array cannot be `null`: a stack array, and a parameter of a
  device function that is declared non-nullable (`T[]` in a `#nullable enable` context, or a struct that holds such an
  array) and never assigned. A call of a device function checks an argument that may be `null` once, at the call, for
  such a parameter and throws `NullReferenceException` there; this is the one place where G# throws earlier than C#,
  which throws only when such a `null` is read. A kernel array parameter, and an array field of a struct kernel
  argument, keep the check where they are read, whatever their annotation, since C# passes a `null` to them as it is:
  read them through
  [`Gpu.NotNull`](../03_bcl_api/gpu_class/NotNull.md) to check once, and a local initialized from the call and never
  assigned afterwards is read without a check.
- Removing the remaining checks for speed is explicit: you write a marker, in the same way `[SkipLocalsInit]` turns off zero-initialization in C#. `Gpu.SyncGrid(GridSyncMode.Fast)` is such a marker for the check that a grid barrier makes before a block arrives (§6.3).

## 6.5 The CPU paths

- The native CPU build and the managed path must produce the GPU result.
- The CPU paths accept every launch that the GPU accepts and reject what every GPU rejects ([launch limits](../02_architecture/01_aot_transpiler.md)). The run time, the speed or the memory of the machine never defines that contract.
- A form whose GPU result the CPU cannot reproduce exactly is not supported on the CPU. G# works around technical obstacles, but not limits that come from the definition of the form itself.
- The CPU paths are also checkers: they report the undefined behavior of §6.3 instead of hiding it.
- Where the GPU leaves a value unspecified within bounds, the CPU paths return one value within them, as they do within the accuracy bounds of the approximate math functions. `Warp.ActiveMask()` is such a value: the GPU returns the calling lane, no lane that has exited, and any of the other lanes on the same path (from Volta on it may return fewer), and the CPU paths return the classic value, the lanes that took the same way through the thread-dependent branches since the warp last ran together ([Warp guide](../04_guides/01_warp_programming_guide.md)). A program whose result depends on which subset it gets is not portable between GPUs either.

## 6.6 Status

This charter was adopted on 2026-10-28. The compiler is being brought in line with it: some forms in §6.2 and §6.3 are not yet checked or diagnosed, and the checked CUDA build does not exist yet. Until then, [5. Limitations and Diagnostics](05_limitations.md) lists what the compiler reports today.

## See also



### Language Specification

- [1. Language Specification](00_index.md)
- [4. Execution and Concurrency Model](04_execution_model.md)
- [5. Limitations and Diagnostics](05_limitations.md)

### Getting Started

- [2. The Mental Model & Safety Net](../00_getting_started/02_mental_model.md)
