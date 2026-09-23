---
title: "11. Strict Memory Management and Ownership Patterns (Zero-Leak Analyzer)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "This specification defines the complete matrix of C# memory management patterns (ownership, lifecycle, disposal, and unsafe escape hatches) enforce..."
---

# 11. Strict Memory Management and Ownership Patterns (Zero-Leak Analyzer)

---

## 11.1 Overview & Core Principles

This specification defines the complete matrix of C# memory management patterns (ownership, lifecycle, disposal, and unsafe escape hatches) enforced by G#.

By encoding these rules into Roslyn Diagnostic Analyzers (`ObjectLifecycleAnalyzer`), G# guarantees **zero VRAM leaks and zero false positives** at compile-time without requiring a garbage collector on the GPU.

### 11.1.1 Strict Ownership Model

Standard C# struggles to enforce strict ownership rules for injected `IDisposable` dependencies (e.g., via constructor injection). If a class receives a `GpuContext`, should it dispose it? Or will the caller dispose it? This ambiguity leads to either memory leaks (missed `.Dispose()`) or double-dispose bugs (both caller and receiver calling `.Dispose()`).

To solve this, G# implements a **Strict Ownership Model** applied strictly to `IDisposable` resources:

1. **Borrowing (Default)**: By default, when an `IDisposable` resource is passed into a method or constructor, it is **Borrowed**. The receiving class _must not_ call `.Dispose()`. If it does, the analyzer throws `GSH0027` (Double-dispose hazard). Furthermore, holding a borrowed resource does not require the receiving class to implement `IDisposable` (`GSH0020` is suppressed).

```csharp
// Example: Borrowing
// GpuBigIntLeafExpression does NOT need to implement IDisposable because `value` is borrowed.
public class GpuBigIntLeafExpression(GpuBigInteger value) : IGpuBigIntExpression
{
    // ❌ ERROR (GSH0027): Calling value.Dispose() here would cause a double-dispose hazard.
}
```

2. **Moving / Ownership Transfer (`[TakesOwnership]`)**: When ownership must be transferred (e.g., factories, context wrappers), the parameter is marked with `[TakesOwnership]`. This explicit contract dictates that the caller is relinquishing control, and the receiving class is now the sole owner. The analyzer then strongly enforces that the receiving class _must_ implement `IDisposable` (`GSH0020`) and _must_ explicitly call `.Dispose()` on that resource in its own `Dispose()` method (`GSH0021`).

```csharp
// Execution Context: G# Kernel (GPU)
// Example: Moving / Ownership Transfer
// GpuBigInteger MUST implement IDisposable because it takes ownership of `value`.
public readonly struct GpuBigInteger([TakesOwnership] GpuUIntBigInteger value) : IDisposable
{
    // ❌ ERROR (GSH0021) if Dispose is missing or doesn't dispose `value`.
    // Note: Dispose() is only invoked on the Host side.
    [GpuKernel]
    public void Dispose()
    {
        value.Dispose(); // ✅ REQUIRED
    }
}
```

This mental model brings compile-time memory safety guarantees to C# without requiring a garbage collector on the GPU.

> [!TIP]
> **What does `[TakesOwnership]` apply to?**
> The analyzer strictly triggers on the `IDisposable` interface.
>
> - **Value Types (`struct`)**: If a `struct` implements `IDisposable` (e.g., `GpuUIntBigInteger`), `[TakesOwnership]` works perfectly and enforces the ownership rules.
> - **Primitives & Pointers**: For types that do _not_ implement `IDisposable` (like `int`, `float`, or raw pointers `void*`), the analyzer completely ignores them. Adding `[TakesOwnership]` to a raw pointer is meaningless; pointers must be managed manually in `unsafe` blocks (see Layer 7).

---

## 11.2 Layer 1: Local Variable Scope (Stack / Method Level)

Lifecycle of resources allocated and scoped within a single method execution.

| Pattern                        | Code Example                        | Analyzer Evaluation & Diagnostics                                                |
| :----------------------------- | :---------------------------------- | :------------------------------------------------------------------------------- |
| **Discarded Allocation**       | `new GpuContext();`                 | ❌ **Error**: Immediate leak. Instantiated without binding or scope management.  |
| **Undisposed Local**           | `var ctx = new GpuContext();`       | ❌ **Error**: Local variable leaves scope without invoking `Dispose()`.          |
| **Scoped Disposal (Standard)** | `using var ctx = new GpuContext();` | ⭕ **OK**: Guaranteed automatic disposal upon scope exit.                        |
| **Explicit Manual Disposal**   | `var ctx = ...; ctx.Dispose();`     | ⭕ **OK**: Satisfies disposal criteria (Warning may suggest `using` preference). |

**Code Examples:**

```csharp
// Execution Context: G# Kernel (GPU)

// ❌ Error: Immediate leak. Instantiated without binding or scope management.
new GpuContext();

// ❌ Error: Local variable leaves scope without invoking `Dispose()`.
void BadScope() {
    var ctx = new GpuContext();
} // ctx leaks here

// ⭕ OK: Guaranteed automatic disposal upon scope exit.
void GoodScope() {
    using var ctx = new GpuContext();
}

// ⭕ OK: Satisfies disposal criteria (Warning may suggest `using` preference).
void ManualScope() {
    var ctx = new GpuContext();
    ctx.Dispose();
}
```

---

## 11.3 Layer 2: Ownership Transfer (Outward Propagation)

Patterns where the allocating method does not dispose of the resource, but cleanly transfers ownership responsibility to the caller or downstream consumer.

| Pattern                            | Code Example                      | Analyzer Evaluation & Diagnostics                                                                                       |
| :--------------------------------- | :-------------------------------- | :---------------------------------------------------------------------------------------------------------------------- |
| **Return Value Transfer**          | `return new GpuContext();`        | ⭕ **OK**: Ownership propagates to the caller. The call-site variable is now subject to Layer 1 validation.             |
| **Output Parameter (`out`)**       | `out var ctx = new GpuContext();` | ⭕ **OK**: Ownership propagates to the caller's target variable.                                                        |
| **Deferred Enumeration (`yield`)** | `yield return new GpuContext();`  | ⭕ **OK**: Ownership transfers to the `IEnumerable` consumer. The consumer must explicitly dispose the yielded items. |

**Code Examples:**

```csharp
// Execution Context: G# Kernel (GPU)

// ⭕ OK: Ownership propagates to the caller. The call-site variable is now subject to Layer 1 validation.
public GpuContext CreateContext() {
    return new GpuContext();
}

// ⭕ OK: Ownership propagates to the caller's target variable.
[GpuKernel]
public void TryCreate(out GpuContext ctx) {
    ctx = new GpuContext();
}

// ⭕ OK: Ownership transfers to the `IEnumerable` consumer. The consumer must explicitly dispose the yielded items.
public IEnumerable<GpuContext> GenerateContexts() {
    yield return new GpuContext();
}
public void ConsumeContexts() {
    foreach (var ctx in GenerateContexts()) {
        using (ctx) { 
            // use ctx 
        }
    }
}
```

---

## 11.4 Layer 3: Class Fields & Member Lifecycles (Object-Level Synchronization)

Patterns where resources are retained as class fields and their lifecycle is tied to the containing object's lifetime.

| Pattern                             | Code Example & Preconditions                                                 | Analyzer Evaluation & Diagnostics                                                                            |
| :---------------------------------- | :--------------------------------------------------------------------------- | :----------------------------------------------------------------------------------------------------------- |
| **Missing `IDisposable` on Class**  | `class A { GpuContext _c = new(); }`                                         | ❌ **Error (GSH0020)**: Class `A` holds a disposable GPU resource but does not implement `IDisposable`.      |
| **Undisposed Field in `Dispose()`** | `class A : IDisposable { GpuContext _c = new(); public void Dispose() { } }` | ❌ **Error (GSH0021)**: `Dispose()` AST does not contain an invocation of `_c.Dispose()` or `_c?.Dispose()`. |
| **Clean Chained Disposal**          | `class A : IDisposable { ... public void Dispose() { _c.Dispose(); } }`      | ⭕ **OK**: Layer 3 validation passed. Resource lifecycle is safely chained.                                  |
| **Unsafe Field Overwrite**          | `_c = new GpuContext();` (Reassignment)                                      | ❌ **Error**: Reassigning an owned field without prior disposal leaks the previous instance.                 |

**Code Examples:**

```csharp
// Execution Context: G# Kernel (GPU)

// ❌ Error (GSH0020): Class `A` holds a disposable GPU resource but does not implement `IDisposable`.
class A {
    GpuContext _c = new GpuContext();
}

// ❌ Error (GSH0021): `Dispose()` AST does not contain an invocation of `_c.Dispose()`.
class B : IDisposable {
    GpuContext _c = new GpuContext();
    [GpuKernel]
    public void Dispose() { }
}

// ⭕ OK: Layer 3 validation passed. Resource lifecycle is safely chained.
class C : IDisposable {
    GpuContext _c = new GpuContext();
    public void Dispose() {
        _c.Dispose();
    }
}

// ❌ Error: Reassigning an owned field without prior disposal leaks the previous instance.
class D : IDisposable {
    GpuContext _c = new GpuContext();
    public void Reassign() {
        _c = new GpuContext();
    }
    public void Dispose() { _c.Dispose(); }
}
```

---

## 11.5 Layer 4: Dependency Injection & Borrowing (Inward Handover)

Patterns where an existing resource is injected from the outside. Distinguishes between **Borrowing** and **Ownership Acquisition**.

| Pattern                            | Code Example                              | Analyzer Evaluation & Diagnostics                                                                                                         |
| :--------------------------------- | :---------------------------------------- | :---------------------------------------------------------------------------------------------------------------------------------------- |
| **Standard Injection (Borrowing)** | `public A(GpuContext c) { _c = c; }`      | ⚠️ **Default: Borrowed**: Class `A` does not own `_c`. Calling `_c.Dispose()` inside `A.Dispose()` is flagged as a double-dispose hazard. |
| **Explicit Ownership Transfer**    | `public A([TakesOwnership] GpuContext c)` | ⭕ **OK**: Explicit attribute transfers ownership. Class `A` is now required to dispose of `_c` in its `Dispose()` implementation.        |

**Code Examples:**

```csharp
// Execution Context: G# Kernel (GPU)

// ⚠️ Default (Borrowed): Class `A` does not own `_c`.
// Calling `_c.Dispose()` inside `A.Dispose()` is flagged as a double-dispose hazard.
public class A(GpuContext c) {
    private readonly GpuContext _c = c;
}

// ⭕ OK: Explicit attribute transfers ownership.
// Class `B` is now required to dispose of `_c` in its `Dispose()` implementation.
public class B([TakesOwnership] GpuContext c) : IDisposable {
    private readonly GpuContext _c = c;
    [GpuKernel]
    public void Dispose() => _c.Dispose();
}
```

---

## 11.6 Layer 5: Collections & Multi-Resource Management (List, Array, Dictionary)

Aggregation of multiple disposable GPU instances.

| Pattern                         | Code Example                                                     | Analyzer Evaluation & Diagnostics                                                            |
| :------------------------------ | :--------------------------------------------------------------- | :------------------------------------------------------------------------------------------- |
| **Collection Ingestion**        | `var list = new List<GpuContext>(); list.Add(new GpuContext());` | ⚠️ **Not Tracked**: Collection element ownership is not currently tracked by the analyzer.   |
| **Manual Loop Disposal**        | `foreach (var c in list) c.Dispose();`                           | ⭕ **Required**: The developer must manually ensure exhaustive traversal and disposal.         |

**Code Examples:**

```csharp
// Execution Context: G# Kernel (GPU)

// ⚠️ Not Tracked: Collection element ownership is not currently tracked by the analyzer.
var list = new List<GpuContext>();
list.Add(new GpuContext());

// ⭕ Required: The developer must manually ensure exhaustive traversal and disposal.
class SafeContainer : IDisposable {
    List<GpuContext> _list = new();
    [GpuKernel]
    public void Dispose() {
        foreach (var c in _list) c.Dispose();
    }
}
```

---

## 11.7 Layer 6: Constructor Exception Safety (Partial Allocation Leaks)

Handling partial failures during object initialization when multiple resources are allocated.

| Pattern                        | Code Example                                                                     | Analyzer Evaluation & Diagnostics                                                                                      |
| :----------------------------- | :------------------------------------------------------------------------------- | :--------------------------------------------------------------------------------------------------------------------- |
| **Unguarded Multi-Allocation** | `_c1 = new GpuContext(); _c2 = new GpuContext(); /* If _c2 throws, _c1 leaks */` | ❌ **Error**: Multiple allocations in a constructor must be wrapped in `try { ... } catch { _c1?.Dispose(); throw; }`. |
| **Safe Factory Pattern**       | `public static A Create() { ... }`                                               | ⭕ **OK**: Preferred architecture. Assembles resources within a protected `using`/`try` scope before constructing `A`. |

**Code Examples:**

```csharp
// Execution Context: G# Kernel (GPU)

// ❌ Error: Multiple allocations in a constructor can leak if one throws.
class UnsafeAlloc : IDisposable {
    GpuContext _c1, _c2;
    public UnsafeAlloc() {
        _c1 = new GpuContext();
        _c2 = new GpuContext(); // If this throws, _c1 is permanently leaked
    }
    [GpuKernel]
    public void Dispose() { _c1.Dispose(); _c2.Dispose(); }
}

// ⭕ OK: Preferred architecture using a Safe Factory Pattern.
class SafeAlloc : IDisposable {
    GpuContext _c1, _c2;
    private SafeAlloc(GpuContext c1, GpuContext c2) {
        _c1 = c1; _c2 = c2;
    }
    public static SafeAlloc Create() {
        var c1 = new GpuContext();
        try {
            var c2 = new GpuContext();
            return new SafeAlloc(c1, c2); // Transfer ownership
        } catch {
            c1.Dispose(); // Clean up if c2 allocation fails
            throw;
        }
    }
    public void Dispose() { _c1.Dispose(); _c2.Dispose(); }
}
```

---

## 11.8 Layer 7: Unsafe Contexts & Escape Hatches (Privileged Unmanaged Operations)

Honoring C#'s foundational philosophy: **"Safe by default, explicitly bounded in `unsafe` blocks for low-level systems programming."**

| Pattern                             | Code Example                                  | Analyzer Evaluation & Transpilation Behavior                                                                                                                                                           |
| :---------------------------------- | :-------------------------------------------- | :----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Unbounded Pointer Operation**     | `int* ptr = ...;` (Without `unsafe` block)    | ❌ **C# Compile Error**: Prohibited by standard C# language semantics.                                                                                                                                 |
| **Stack Allocation (`stackalloc`)** | `unsafe { int* p = stackalloc int[10]; }`     | ⭕ **OK (No Warning)**: Transpiles directly to a C++ local stack array (`int p[10];`). Automatically reclaimed on scope exit.                                                                          |
| **Memory Pinning (`fixed`)**        | `unsafe { fixed (byte* p = arr) { ... } }`    | ⭕ **OK (No Warning)**: GC pinning and unpinning are scoped to the block. Fully safe.                                                                                                                  |
| **Dynamic Device-side Allocation**  | `unsafe { void* p = Gpu.Unsafe.Malloc(sz); }` | ⚠️ **Warning (GSH0029)**: Emits a compiler warning: _"Manual unmanaged memory allocation detected. Ensure corresponding Free is called."_ Preserves developer freedom for advanced kernel development. |

**Code Examples:**

```csharp
// Execution Context: G# Kernel (GPU)

// ❌ C# Compile Error: Prohibited by standard C# language semantics.
int* ptr = ...;

// ⭕ OK (No Warning): Transpiles directly to a C++ local stack array (`int p[10];`).
// Automatically reclaimed on scope exit.
unsafe {
    int* p = stackalloc int[10];
}

// ⭕ OK (No Warning): GC pinning and unpinning are scoped to the block. Fully safe.
unsafe {
    fixed (byte* p = arr) {
        // Use p safely
    }
}

// ⚠️ Warning (GSH0029): Emits a compiler warning: "Manual unmanaged memory allocation detected."
// Preserves developer freedom for advanced kernel development but requires manual Gpu.Unsafe.Free().
unsafe {
    void* p = Gpu.Unsafe.Malloc(sz);
}
```

---

## 11.9 Conclusion: Absolute Safety Harmonized with Developer Freedom

By implementing these 7 layers as static Roslyn Diagnostic Analyzers:

1. **Standard Code**: Eliminates human-error VRAM leaks by 100% at compile-time.
2. **Advanced Systems Code**: Preserves the full expressiveness and power of low-level GPU programming through explicit `unsafe` scopes.

This achieves a memory management architecture that brings C#'s expressive ergonomics and safety directly to bare-metal CUDA execution.

## See also




### Getting Started

- [0. Getting Started with G# (GSharp)](../00_getting_started/00_index.md)
- [2. The Mental Model & Safety Net](../00_getting_started/02_mental_model.md)

### BCL API > Attributes

- [Attributes](../03_bcl_api/attributes/00_index.md)
- [Kassyi.GSharp.Core: TakesOwnershipAttribute class](../03_bcl_api/attributes/TakesOwnershipAttribute.md)

### BCL API > Memory

- [Memory Management](../03_bcl_api/memory/00_index.md)
- [SharedMemory<T> Class (G#)](../03_bcl_api/memory/SharedMemory.md)

