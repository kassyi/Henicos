---
title: "Kassyi.GSharp.Core: TakesOwnershipAttribute class"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Marks a dependency-injected GPU or native resource parameter as being strictly owned by the receiving class or struct."
---

## TakesOwnershipAttribute Class

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

The **`TakesOwnershipAttribute`** marks a constructor parameter as transferring full resource ownership and lifecycle management responsibility to the receiving class or struct. It interacts directly with the compile-time Zero-Leak Analyzer (`DisposableOwnershipAnalyzer`) to ensure deterministic disposal without runtime overhead.

```csharp
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class TakesOwnershipAttribute : Attribute
```

## Syntax

```csharp
// Standard Constructor
public ClassConstructor([TakesOwnership] GpuWorkspace workspace)

// C# 12 Primary Constructor
public struct StructWrapper([TakesOwnership] CudaStream stream) : IDisposable
```

---

## Core Philosophy (Architectural Tenets)

GSharp (Henicos) operates on two strict GPU computing pillars: **Static Determinism** and **Zero-Allocation**. In native GPU computing, VRAM pressure and driver resource lifetimes (such as CUDA streams, workspaces, and pinned memory buffers) are invisible to the .NET Garbage Collector.

1. **Borrowing by Default**: Passing an `IDisposable` resource as an argument to any method or constructor without `[TakesOwnership]` represents a **Borrow**. The caller retains full ownership and disposal responsibility. Callee disposal of borrowed resources is strictly prohibited to prevent catastrophic double-free crashes.
2. **Static Injection of Ownership**: Transferring ownership (Move semantics) is modeled strictly as dependency injection during object instantiation. The caller surrenders disposal responsibility, transferring it completely to the newly created instance.
3. **Compile-Time Determinism (No Dynamic Lifecycles)**: GSharp deliberately rejects runtime reference counting or dynamic ownership relays across arbitrary method calls. All ownership boundaries are fully resolved at compile-time via Roslyn AST and Semantic Models.

---

## Constraints and Rules

Applying `[TakesOwnership]` enforces the following strict compile-time constraints:

### 1. Constructor Parameters Only

The `[TakesOwnership]` attribute is permitted **only** on parameters of instance constructors (including standard constructors and C# 12 Primary Constructors). Applying it to regular member methods, static methods, local functions, lambda expressions, delegates, or property/indexer parameters is prohibited (**GSH0052**). Dynamic mid-stream ownership relays violate static determinism.

### 2. Target Type Must Implement `IDisposable`

The parameter type decorated with `[TakesOwnership]` must implement `System.IDisposable` (**GSH0051**). Transferring ownership of non-disposable types (e.g., primitives like `int` or plain data structs) is a semantic contradiction and produces a compile error.

### 3. No Pass-by-Reference Modifiers (`ref`, `in`, `out`)

A parameter marked with `[TakesOwnership]` cannot declare `ref`, `in`, or `out` modifiers (**GSH0053**). Pass-by-reference binds the parameter to the caller's storage location, conflicting with the move semantics of ownership transfer.

### 4. Receiving Type Must Implement `IDisposable`

Any class or struct whose constructor accepts an owned resource via `[TakesOwnership]` must implement `System.IDisposable` (**GSH0020**).

### 5. Deterministic Disposal in `Dispose()`

The receiving type must explicitly invoke `.Dispose()` on the owned resource inside its `Dispose()` method (**GSH0022**). For C# 12 Primary Constructors, the compiler-synthesized backing fields representing the owned parameters must be disposed.

---

## Associated Diagnostics & Errors

The Roslyn Zero-Leak Analyzer (`DisposableOwnershipAnalyzer`) verifies `[TakesOwnership]` usage and emits the following diagnostics:

| Diagnostic ID | Severity | Name | Description |
| :--- | :--- | :--- | :--- |
| **[GSH0020](../diagnostics/GSH0020.md)** | Error | `MissingIDisposableWithOwnership` | The class or struct takes ownership of a resource but does not implement `IDisposable`. |
| **[GSH0021](../diagnostics/GSH0021.md)** | Warning | `MissingBaseDisposeCall` | A derived disposable class fails to invoke `base.Dispose(bool)` in its disposal logic. |
| **[GSH0022](../diagnostics/GSH0022.md)** | Error | `UndisposedOwnedResource` | An owned resource accepted via `[TakesOwnership]` is not disposed within `Dispose()`. |
| **[GSH0027](../diagnostics/GSH0027.md)** | Error | `DisposingBorrowedResource` | A borrowed parameter (lacking `[TakesOwnership]`) is improperly disposed, causing double-free hazards. |
| **[GSH0051](../diagnostics/GSH0051.md)** | Error | `InvalidTakesOwnershipTarget` | `[TakesOwnership]` is applied to a parameter whose type does not implement `IDisposable`. |
| **[GSH0052](../diagnostics/GSH0052.md)** | Error | `TakesOwnershipOnlyAllowedOnConstructors` | `[TakesOwnership]` is applied to a method, lambda, or local function instead of a constructor. |
| **[GSH0053](../diagnostics/GSH0053.md)** | Error | `TakesOwnershipByRefNotSupported` | `[TakesOwnership]` is applied to a parameter marked with `ref`, `in`, or `out`. |

---

## Examples

### 1. Constructor Ownership Transfer

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
using System;
using System.IO;
using Kassyi.GSharp.Core;

// Execution Context: Host (CPU)

using var pipeline = new ComputePipeline(new MemoryStream());
Console.WriteLine("ComputePipeline initialized and will dispose owned resource.");

public sealed class ComputePipeline : IDisposable
{
    private readonly IDisposable _ownedResource;

    // ✅ Correct: Takes ownership via constructor parameter
    public ComputePipeline([TakesOwnership] IDisposable ownedResource)
    {
        _ownedResource = ownedResource;
    }

    public void Dispose()
    {
        // ✅ Enforced by GSH0022: Owned resource must be disposed
        _ownedResource.Dispose();
    }
}
```

### 3. Anti-Patterns and Prohibited Usages

```csharp
// ❌ Error GSH0051: int does not implement IDisposable
public class InvalidTarget([TakesOwnership] int count) : IDisposable
{
    public void Dispose() { }
}

// ❌ Error GSH0052: [TakesOwnership] cannot be applied to methods
public class DynamicService
{
    public void Consume([TakesOwnership] CudaStream stream) { }
}

// ❌ Error GSH0053: Cannot take ownership of reference parameter
public class RefScope : IDisposable
{
    public RefScope(in [TakesOwnership] CudaStream stream) { }
    public void Dispose() { }
}

// ❌ Error GSH0020: Class takes ownership but fails to implement IDisposable
public class LeakingWrapper([TakesOwnership] CudaStream stream)
{
}
```

---

## See Also

### Diagnostics

- [GSH0020: Missing IDisposable implementation](../diagnostics/GSH0020.md)
- [GSH0022: Unsafe field overwrite / Undisposed owned resource](../diagnostics/GSH0022.md)
- [GSH0027: Disposing borrowed resource](../diagnostics/GSH0027.md)
- [GSH0051: [TakesOwnership] applied to non-IDisposable type](../diagnostics/GSH0051.md)
- [GSH0052: [TakesOwnership] is only allowed on constructor parameters](../diagnostics/GSH0052.md)
- [GSH0053: Cannot take ownership of a reference parameter](../diagnostics/GSH0053.md)

### BCL API > Attributes

- [Attributes Index](00_index.md)

### Guides

- [11. Strict Memory Management and Ownership Patterns (Zero-Leak Analyzer)](../../04_guides/02_memory_safety_patterns.md)
- [The CPU to GPU Paradigm Shift: Anti-Patterns & Best Practices](../../04_guides/06_antipatterns_and_best_practices.md)
