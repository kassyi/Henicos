# 2. Lexical and Syntactic Grammar

This document defines the strict subset of C# lexical and syntactic rules supported by the G# transpiler. Because G# translates C# Abstract Syntax Trees (AST) directly into CUDA C++ during the AOT phase, it supports a very specific footprint of C# constructs.

## 2.1 Supported Statements and Control Flow

G# supports standard procedural programming paradigms essential for GPU compute kernels.

| C# Construct | Supported | CUDA Equivalent | Notes |
| :--- | :---: | :--- | :--- |
| `if` / `else if` / `else` | Yes | `if` / `else` | Standard branching. Beware of warp divergence. |
| `for` loop | Yes | `for` loop | Automatically prefixed with `#pragma unroll` during transpilation. |
| `while` loop | Yes | `while` loop | Supported. |
| `do...while` loop | Yes | `do...while` loop | Supported. |
| `switch` statement | Yes | `switch` statement | Pattern matching is **not** supported (only basic value switches). |
| `break` / `continue` | Yes | `break` / `continue` | Supported within loops and switches. |
| `return` | Yes | `return` | Kernels (`[GpuKernel]`) must return `void`. Helpers (`[GpuDevice]`) can return values. |
| `goto` | No | - | Strictly prohibited to ensure static analyzability and structured control flow. |
| `try` / `catch` / `finally` | No | - | GPU execution does not support C++ exception handling for performance reasons. |
| `yield return` | No | - | Iterators and state machines are unsupported. |
| `await` / `async` | No | - | Asynchronous execution within a kernel is unsupported. |
| `lock` | No | - | Use `Gpu.AtomicAdd` or `Gpu.SyncThreads` for synchronization instead. |

## 2.2 Variables and Scoping

Variables in G# are strictly translated to native C++ variables. 

* **`var` keyword:** Fully supported. The transpiler infers the equivalent C++ type (e.g., `auto`).
* **Variable Shadowing:** Supported, following standard C# scope rules.
* **Tuple Deconstruction:** Not supported directly as native CUDA lacks managed tuples.
* **Heap Allocation (`new Class()`):** Prohibited. All local variables must be primitive types or value types (`struct`), which are allocated in GPU registers or local memory.
* **Arrays (`T[]`):** Local instantiation (`new int[10]`) is prohibited inside a kernel. Arrays must be passed in as arguments (allocated via `__shared__` or globally in device memory).

## 2.3 Operators

Standard C# operators map directly to their CUDA C++ counterparts with identical precedence and associativity.

* **Arithmetic:** `+`, `-`, `*`, `/`, `%`
* **Bitwise:** `&`, `|`, `^`, `~`, `<<`, `>>`
* **Logical:** `&&`, `||`, `!` (Short-circuit evaluation is fully supported)
* **Relational:** `==`, `!=`, `<`, `>`, `<=`, `>=`
* **Assignment:** `=`, `+=`, `-=`, `*=`, `/=`, `%=`, `&=`, `|=`, `^=`, `<<=`, `>>=`
* **Ternary Conditional:** `? :`
* **Pointer Operators:** `*` (dereference), `&` (address-of), `->` (member access). *Note: The `unsafe` keyword must be used in the C# method signature.*
* **`sizeof(T)`:** Supported. Translated directly to `sizeof(T)` in C++.

## 2.4 Preprocessor Directives

C# preprocessor directives (e.g., `#if`, `#define`) are **not** translated into CUDA C++ preprocessor directives. They are evaluated by the Roslyn compiler during the AOT phase.
To inject C++ preprocessor macros (like `#define`) into the generated CUDA code, you must use the `[JitConstant]` attribute on kernel parameters.
