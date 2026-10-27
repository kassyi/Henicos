# Semantics Charter (decided 2026-10-28)

This rule fixes which definition wins when C#, the GPU (CUDA) and the CPU paths (native C++ and managed) disagree.
Every design, compiler change and new diagnostic follows it. The full user-facing text is
[docs/spec/en/01_language/06_semantics.md](../../docs/spec/en/01_language/06_semantics.md).

## 1. Principles

1. **The GPU is the target**: the G in G# is the GPU. The CPU paths exist for evaluation: they reproduce the GPU result and check it. Never make the GPU slower to match the CPU.
2. **What you write is what runs**: each GPU thread executes the C# it was given. Never implicitly add work or synchronization, or change how many times something runs.
3. **Extra meaning needs a marker**: syntax sugar is welcome, but only behind a marker the user wrote (an API such as `AsParallel()` or `Gpu.SyncGrid(body)`, an attribute, a modifier such as `async`). With the marker, the compiler may use the fastest form that keeps the promised result.
4. **Undefined behavior is an error**: when neither C# nor the GPU defines the result, G# does not pick a meaning; it reports an error.

## 2. Which definition wins

| C# | GPU | Follow |
| :- | :- | :- |
| Defines the result | Same result | No conflict |
| Defines the result | Undefined | C#, on the GPU and the CPU: shift counts, signed overflow, division by zero, out-of-range index, order of evaluation |
| Defines the result | Defines a different result | The GPU (math accuracy, FMA, denormals), except value contracts C# fixes exactly (`Math.Round` to even, comparisons, conversions), which follow C# |
| Does not define it (G#-specific APIs) | Defines the result | The GPU, even where the CPU leaves it undefined or gives a different result |
| Does not define it | Undefined | Error (§3) |

## 3. Undefined behavior (treated like `1 / 0`)

- A form that is certain at compile time is a compile error; the message shows the explicit form that does what the user meant.
- A form known only at run time is a run-time error, with the source location, on the native CPU build, the managed path and the checked CUDA build. The release CUDA build does not check.
- A form the analysis can only suspect is not a compile error (never reject correct code).
- A result a CPU path happens to produce is never treated as correct. When the CPU and GPU results differ, suspect undefined behavior on the GPU first.

## 4. The cost of following C#

- Bounds checks and division-by-zero checks are on by default and removed where the compiler proves them unnecessary (as the .NET JIT does).
- Turning off the rest for speed takes an explicit marker the user writes (the same idea as `[SkipLocalsInit]`).

## 5. The CPU paths

- A form whose GPU result the CPU cannot reproduce exactly is not supported on the CPU. Overcome technical obstacles, but not limits that come from the definition of the form itself.

## 6. Instructions for agents

- Never recommend a design that adds meaning implicitly (unwritten synchronization, changing how many times code runs, choosing cooperative launch automatically). If the behavior is needed, propose it as an explicit API or attribute.
- Never "define away" undefined behavior by adopting one dialect's result. Treat it as a diagnostic under §3.
- Existing code and designs that break this charter are listed by an inventory and filed as issues.
