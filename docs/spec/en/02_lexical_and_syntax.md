# 2. Lexical and Syntactic Grammar

This document defines the strict subset of C# lexical and syntactic rules supported by the G# transpiler. Because G# translates C# Abstract Syntax Trees (AST) directly into CUDA C++ during the AOT phase, it supports a very specific footprint of C# constructs.

## 2.1 Supported Statements and Control Flow

G# supports standard procedural programming paradigms essential for GPU compute kernels.

| C# Construct | Supported | CUDA Equivalent | Notes |
| :--- | :---: | :--- | :--- |
| `if` / `else if` / `else` | Yes | `if` / `else` | Standard branching. Single-statement braceless blocks are automatically scoped `{ }` in generated CUDA C++. |
| `for` loop | Yes | `for` loop | Automatically prefixed with `#pragma unroll` during transpilation. |
| `while` loop | Yes | `while` loop | Supported. |
| `do...while` loop | Yes | `do...while` loop | Supported. |
| `switch` statement | Yes | `switch` statement | Pattern matching is **not** supported (only basic value switches). |
| `switch` expression | Yes | Lambda (IIFE) | Supported for basic value switching. Note: C# does not support `ref switch` expressions (`ref var r = x switch { ... }`), so use `if/else` or `ref ? :` for conditional `ref` assignment. |
| `break` / `continue` | Yes | `break` / `continue` | Supported within loops and switches. |
| `return` | Yes | `return` | Kernels (`[GpuKernel]`) must return `void`. Helpers (`[GpuDeviceFunction]`) can return values. |
| Expression-bodied (`=>`) | Yes | Function body | Supported for both `void` and value-returning methods (automatically injects `return`). |
| `goto` | No | - | Strictly prohibited to ensure static analyzability and structured control flow. |
| `try` / `catch` / `finally` | No | - | GPU execution does not support C++ exception handling for performance reasons. |
| `yield return` | No | - | Iterators and state machines are unsupported. |
| `await` / `async` | No | - | Asynchronous execution within a kernel is unsupported. |
| `lock` | No | - | Use `Gpu.AtomicAdd` or `Gpu.SyncThreads` for synchronization instead. |

## 2.2 Variables and Scoping

Variables in G# are strictly translated to native C++ variables. 

* **`var` keyword:** Fully supported. The transpiler infers the equivalent C++ type (e.g., `auto`).
* **Variable Shadowing:** Supported, following standard C# scope rules.
* **Tuple Swaps / Assignments:** Supported for simple tuple swaps `(a, b) = (b, a)` via generated C++ IIFE (Immediately Invoked Lambda Expressions). Full C# tuple deconstruction into managed types remains unsupported.
* **Heap Allocation (`new Class()`):** Prohibited. All local variables must be primitive types or value types (`struct`), which are allocated in GPU registers or local memory.
* **Arrays (`T[]`):** Local instantiation (`new int[10]`) is prohibited inside a kernel. Arrays must be passed in as arguments (allocated via `__shared__` or globally in device memory).
* **Default Literal (`default`):** Fully supported. Transpiles to zero-initialization or C++ default constructors (e.g., `{}`).
* **Extension Methods:** Supported. Invocation via `this` arguments (e.g., `obj.Method()`) correctly transpiles to standard static C++ function calls.

## 2.3 Operators

Standard C# operators map directly to their CUDA C++ counterparts with identical precedence and associativity.

* **Arithmetic:** `+`, `-`, `*`, `/`, `%`
* **Bitwise:** `&`, `|`, `^`, `~`, `<<`, `>>`
* **Logical:** `&&`, `||`, `!` (Short-circuit evaluation is fully supported)
* **Relational:** `==`, `!=`, `<`, `>`, `<=`, `>=`
* **Assignment:** `=`, `+=`, `-=`, `*=`, `/=`, `%=`, `&=`, `|=`, `^=`, `<<=`, `>>=`, plus Tuple Swap `(a, b) = (b, a)`
* **Ternary Conditional:** `? :`
* **Pointer Operators:** `*` (dereference), `&` (address-of), `->` (member access), as well as postfix increment/decrement (`ptr++`, `ptr--`). *Note: The `unsafe` keyword must be used in the C# method signature.*
* **`sizeof(T)`:** Supported. Translated directly to `sizeof(T)` in C++.

## 2.4 Preprocessor Directives

C# preprocessor directives (e.g., `#if`, `#define`) are **not** translated into CUDA C++ preprocessor directives. They are evaluated by the Roslyn compiler during the AOT phase.
To inject C++ preprocessor macros (like `#define`) into the generated CUDA code, you must use the `[Constant]` attribute on kernel parameters.
