---
title: "2. The Mental Model & Safety Net"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Understanding G# Execution Context and Static Determinism"
---

# 2. The Mental Model & Safety Net

### What is G#?

**G# is a zero-overhead C#-to-CUDA AOT transpilation framework, operating as an Embedded Compute Dialect (eDSL) powered by standard C# syntax.**

G# is not a traditional general-purpose language, nor is it a mere runtime wrapper around CUDA. Instead, it serves as a high-performance **GPU compute dialect** embedded directly within the modern .NET ecosystem.

By leveraging Microsoft's Roslyn compiler platform as its syntactic and type-checking front-end, G# allows developers to author bare-metal GPU kernels using the familiar elegance, type safety, and first-class tooling of C# (Visual Studio, Rider, IntelliSense).

At build time, G# intercepts your C# kernel AST and directly transpiles it into optimized, native CUDA C++ and PTX Fatbin payloads, emitting zero-allocation host orchestration wrappers completely behind the scenes.

### The Mental Model: C# Syntax, GPU Execution

G# kernel code is valid C#, and it runs on the GPU's native execution model:

- **The Meaning is C# Where C# Defines It**: Each GPU thread runs the C# it was given. Where C# defines a result that CUDA leaves undefined (integer overflow, shift counts, order of evaluation, an out-of-range index), G# follows C#. Where C# says nothing (barriers, warp shuffles, shared memory), the GPU decides. Forms that neither defines are errors. See [6. Semantics Charter](../01_language/06_semantics.md).
- **The Syntax is C#**: You express logic using familiar loops, value `struct`s, scalar primitives, local functions, and modern C# pattern matching.
- **The Architecture is a GPU Dialect (eDSL)**: There is no .NET CLR, no managed heap allocations (`class`, `string`, LINQ), and no garbage collector inside the kernel. Standard BCL methods (such as `BitOperations` or `MathF`) serve strictly as zero-cost symbolic compile-time markers that lower directly into hardware PTX intrinsics.
- **The Engine is Bare-Metal CUDA**: You program thousands of massively parallel hardware threads structured into Grids, Blocks, and 32-thread Warps, interacting directly with registers, Shared Memory, and high-bandwidth VRAM.

If you know C#, G# eliminates the friction of C++ headers, CMake, build toolchain setup, and error-prone P/Invoke marshaling. But under the hood, you are commanding bare-metal NVIDIA hardware with uncompromising native performance.

### Host-Side vs Device-Side Execution

A critical concept in G# is understanding the execution boundary between the CPU and the GPU. Because you write both in the exact same C# language, maintaining awareness of this boundary is essential.

- **Host-Side (CPU)**: This is your standard .NET application code. It runs on the CPU, handles orchestration, allocates GPU VRAM, and dispatches workloads to the hardware.
- **Device-Side (GPU)**: This is the code that executes on the bare-metal NVIDIA hardware (e.g., inside a `[GpuKernel]`). It is massively parallel and enforces strict zero-allocation rules—prohibiting managed heap allocations (classes, dynamic strings, LINQ). Note that values fully resolved at compile time (such as compile-time constant string literals in [`Gpu.InlinePtx`](../03_bcl_api/gpu_class/InlinePtx.md) or static calculations) are supported (see [5. Limitations and Diagnostics](../01_language/05_limitations.md) for details).

To help you maintain this mental model, **every code snippet in this documentation explicitly declares its execution context** at the very top, like this:

```csharp
// Execution Context: Host (CPU)
var data = new float[100];
```

```csharp
// Execution Context: G# Kernel (Device/GPU)
int i = ThreadIndex.X;
```

### The Safety Net: Compile-Time Verification (Static Determinism)

A natural concern when writing GPU kernels in high-level C# is:
_"What happens if I accidentally call `Console.WriteLine`, an unmapped .NET API, or a third-party NuGet package inside a `[GpuKernel]`?"_

**The answer is: It will fail immediately at compile time.**

G# strictly enforces the principle of **Static Determinism** via compile-time Roslyn syntax and semantic analysis. It never allows unmapped or dynamic code to slip through to the CUDA runtime:

- **Zero Runtime Surprises**: You will never experience mysterious GPU driver crashes, silent NaN propagation, or device freezes caused by unsupported C# syntax or runtime reflection.
- **Instant IDE Feedback**: Any unmapped method invocation, managed string allocation, or unsupported BCL call is caught right in your editor (Visual Studio / Rider / `dotnet build`) with clear, dedicated compile errors ([GSH0001–GSH0029, RS0030](../03_bcl_api/diagnostics/00_index.md)).

If your G# code compiles without errors, you have a compile-time guarantee that your kernel translates safely into valid, bare-metal CUDA C++. For a comprehensive list of constraints and diagnostic codes, see [5. Limitations and Diagnostics](../01_language/05_limitations.md).

### The G# Philosophy: Strict Ownership Model

When dealing with scarce GPU memory and high-performance orchestration on the CPU, managing `IDisposable` resources can quickly lead to memory leaks or double-dispose hazards. Standard C# struggles to enforce strict ownership rules for injected dependencies.

To solve this, G# implements a **Strict Ownership Model** for `IDisposable` resources via its custom Roslyn Analyzer (`ObjectLifecycleAnalyzer`).

- By default, injected resources are considered **Borrowed** (the receiving class must not dispose them).
- When ownership must be transferred, G# uses the `[TakesOwnership]` attribute, strictly enforcing that the receiving class fully manages and disposes the resource.

This eliminates ambiguity and provides compile-time guarantees against memory leaks and double-free bugs. For detailed rules and patterns, see [Memory Safety Patterns](../04_guides/02_memory_safety_patterns.md).

Now that you understand the rules of the road, proceed to [3. The "Hello GPU" Tutorial](03_hello_gpu.md).

## See also



### Language Specification

- [1. Language Specification](../01_language/00_index.md)
- [4. Execution and Concurrency Model](../01_language/04_execution_model.md)
- [5. Limitations and Diagnostics](../01_language/05_limitations.md)

### BCL API > Attributes

- [Attributes](../03_bcl_api/attributes/00_index.md)
- [Kassyi.GSharp.Core: TakesOwnershipAttribute class](../03_bcl_api/attributes/TakesOwnershipAttribute.md)

### Guides

- [4. Practical Guides and Best Practices](../04_guides/00_index.md)
- [11. Strict Memory Management and Ownership Patterns (Zero-Leak Analyzer)](../04_guides/02_memory_safety_patterns.md)

