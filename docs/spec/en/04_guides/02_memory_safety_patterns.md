# 11. Strict Memory Management and Ownership Patterns (Zero-Leak Analyzer)

---

## 11.1 Overview & Core Principles

Based on the engineering principle that **"Software execution paths are finite, bounded, and exhaustively verifiable,"** this specification defines the complete matrix of C# memory management patterns (ownership, lifecycle, disposal, and unsafe escape hatches) enforced by G#.

By encoding these rules into Roslyn Diagnostic Analyzers, G# guarantees **zero VRAM leaks and zero false positives** at compile-time while maximizing developer ergonomics.

The foundational principle maps to an **Ownership Model** (analogous to Rust and C++ RAII):
When a resource (such as `new GpuContext()`) is instantiated, an ownership binding is created. The owner must either **consume (`Dispose`)** the resource or **transfer** ownership to an outer or companion scope.

---

## 11.2 Layer 1: Local Variable Scope (Stack / Method Level)

Lifecycle of resources allocated and scoped within a single method execution.

| Pattern | Code Example | Analyzer Evaluation & Diagnostics |
| :--- | :--- | :--- |
| **Discarded Allocation** | `new GpuContext();` | ❌ **Error**: Immediate leak. Instantiated without binding or scope management. |
| **Undisposed Local** | `var ctx = new GpuContext();` | ❌ **Error**: Local variable leaves scope without invoking `Dispose()`. |
| **Scoped Disposal (Standard)** | `using var ctx = new GpuContext();` | ⭕ **OK**: Guaranteed automatic disposal upon scope exit. |
| **Explicit Manual Disposal** | `var ctx = ...; ctx.Dispose();` | ⭕ **OK**: Satisfies disposal criteria (Warning may suggest `using` preference). |

---

## 11.3 Layer 2: Ownership Transfer (Outward Propagation)

Patterns where the allocating method does not dispose of the resource, but cleanly transfers ownership responsibility to the caller or downstream consumer.

| Pattern | Code Example | Analyzer Evaluation & Diagnostics |
| :--- | :--- | :--- |
| **Return Value Transfer** | `return new GpuContext();` | ⭕ **OK**: Ownership propagates to the caller. The call-site variable is now subject to Layer 1 validation. |
| **Output Parameter (`out`)** | `out var ctx = new GpuContext();` | ⭕ **OK**: Ownership propagates to the caller's target variable. |
| **Deferred Enumeration (`yield`)** | `yield return new GpuContext();` | ⭕ **OK**: Ownership transfers to the `IEnumerable` consumer. Disposed automatically when the consumer exits `foreach`. |

---

## 11.4 Layer 3: Class Fields & Member Lifecycles (Object-Level Synchronization)

Patterns where resources are retained as class fields and their lifecycle is tied to the containing object's lifetime.

| Pattern | Code Example & Preconditions | Analyzer Evaluation & Diagnostics |
| :--- | :--- | :--- |
| **Missing `IDisposable` on Class** | `class A { GpuContext _c = new(); }` | ❌ **Error (GSH021)**: Class `A` holds a disposable GPU resource but does not implement `IDisposable`. |
| **Undisposed Field in `Dispose()`** | `class A : IDisposable { GpuContext _c = new(); public void Dispose() { } }` | ❌ **Error (GSH022)**: `Dispose()` AST does not contain an invocation of `_c.Dispose()` or `_c?.Dispose()`. |
| **Clean Chained Disposal** | `class A : IDisposable { ... public void Dispose() { _c.Dispose(); } }` | ⭕ **OK**: Layer 3 validation passed. Resource lifecycle is safely chained. |
| **Unsafe Field Overwrite** | `_c = new GpuContext();` (Reassignment) | ❌ **Error**: Reassigning an owned field without prior disposal leaks the previous instance. |

---

## 11.5 Layer 4: Dependency Injection & Borrowing (Inward Handover)

Patterns where an existing resource is injected from the outside. Distinguishes between **Borrowing** and **Ownership Acquisition**.

| Pattern | Code Example | Analyzer Evaluation & Diagnostics |
| :--- | :--- | :--- |
| **Standard Injection (Borrowing)** | `public A(GpuContext c) { _c = c; }` | ⚠️ **Default: Borrowed**: Class `A` does not own `_c`. Calling `_c.Dispose()` inside `A.Dispose()` is flagged as a double-dispose hazard. |
| **Explicit Ownership Transfer** | `public A([TakesOwnership] GpuContext c)` | ⭕ **OK**: Explicit attribute transfers ownership. Class `A` is now required to dispose of `_c` in its `Dispose()` implementation. |

---

## 11.6 Layer 5: Collections & Multi-Resource Management (List, Array, Dictionary)

Aggregation of multiple disposable GPU instances.

| Pattern | Code Example | Analyzer Evaluation & Diagnostics |
| :--- | :--- | :--- |
| **Collection Ingestion** | `var list = new List<GpuContext>(); list.Add(new GpuContext());` | ⭕ **OK**: Ownership is transferred to the collection container. |
| **Undisposed Collection Field** | (Field `List<GpuContext> _list` left unhandled) | ❌ **Error**: Container class `Dispose()` lacks an iteration/clear loop over elements. |
| **Exhaustive Loop Disposal** | `foreach (var c in list) c.Dispose();` | ⭕ **OK**: AST confirms an exhaustive traversal loop executing `.Dispose()` on each element. |

---

## 11.7 Layer 6: Constructor Exception Safety (Partial Allocation Leaks)

Handling partial failures during object initialization when multiple resources are allocated.

| Pattern | Code Example | Analyzer Evaluation & Diagnostics |
| :--- | :--- | :--- |
| **Unguarded Multi-Allocation** | `_c1 = new GpuContext(); _c2 = new GpuContext(); /* If _c2 throws, _c1 leaks */` | ❌ **Error**: Multiple allocations in a constructor must be wrapped in `try { ... } catch { _c1?.Dispose(); throw; }`. |
| **Safe Factory Pattern** | `public static A Create() { ... }` | ⭕ **OK**: Preferred architecture. Assembles resources within a protected `using`/`try` scope before constructing `A`. |

---

## 11.8 Layer 7: Unsafe Contexts & Escape Hatches (Privileged Unmanaged Operations)

Honoring C#'s foundational philosophy: **"Safe by default, explicitly bounded in `unsafe` blocks for low-level systems programming."**

| Pattern | Code Example | Analyzer Evaluation & Transpilation Behavior |
| :--- | :--- | :--- |
| **Unbounded Pointer Operation** | `int* ptr = ...;` (Without `unsafe` block) | ❌ **C# Compile Error**: Prohibited by standard C# language semantics. |
| **Stack Allocation (`stackalloc`)** | `unsafe { int* p = stackalloc int[10]; }` | ⭕ **OK (No Warning)**: Transpiles directly to a C++ local stack array (`int p[10];`). Automatically reclaimed on scope exit. |
| **Memory Pinning (`fixed`)** | `unsafe { fixed (byte* p = arr) { ... } }` | ⭕ **OK (No Warning)**: GC pinning and unpinning are scoped to the block. Fully safe. |
| **Dynamic Device-side Allocation** | `unsafe { void* p = Gpu.Unsafe.Malloc(sz); }` | ⚠️ **Warning (GSW010)**: Emits a compiler warning: *"Manual unmanaged memory allocation detected. Ensure corresponding Free is called."* Preserves developer freedom for advanced kernel development. |

---

## 11.9 Conclusion: Absolute Safety Harmonized with Developer Freedom

By implementing these 7 layers as static Roslyn Diagnostic Analyzers:
1. **Standard Code**: Eliminates human-error VRAM leaks by 100% at compile-time.
2. **Advanced Systems Code**: Preserves the full expressiveness and power of low-level GPU programming through explicit `unsafe` scopes.

This achieves a memory management architecture that brings C#'s expressive ergonomics and safety directly to bare-metal CUDA execution.
