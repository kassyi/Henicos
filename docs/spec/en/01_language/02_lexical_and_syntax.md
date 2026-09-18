# 2. Lexical and Syntactic Grammar

This document defines the strict subset of C# lexical and syntactic rules supported by the G# transpiler. Because G# translates C# Abstract Syntax Trees (AST) directly into CUDA C++ during the AOT phase, it supports a very specific footprint of C# constructs.

## 2.1 Supported Statements and Control Flow

G# supports standard procedural programming paradigms essential for GPU compute kernels.

| C# Construct | Supported | CUDA Equivalent | Notes |
| :--- | :---: | :--- | :--- |
| `if` / `else if` / `else` | Yes | `if` / `else` | Standard branching. Single-statement braceless blocks are automatically scoped `{ }` in generated CUDA C++. |
| `for` loop | Yes | `for` loop | Supported. Use `Gpu.Hint.Unroll()` immediately preceding the loop to emit `#pragma unroll`. |
| `while` loop | Yes | `while` loop | Supported. |
| `do...while` loop | Yes | `do...while` loop | Supported. |
| `switch` statement | Yes | `switch` statement | Pattern matching is **supported** (including constant and relational patterns). |
| `switch` expression | Yes | Lambda (IIFE) | Supported for pattern matching and value switching. Note: C# does not support `ref switch` expressions (`ref var r = x switch { ... }`), so use `if/else` or `ref ? :` for conditional `ref` assignment. |
| `break` / `continue` | Yes | `break` / `continue` | Supported within loops and switches. |
| `return` | Yes | `return` | Kernels (`[GpuKernel]`) must return `void` to enforce zero-allocation output buffer contracts (enforced by **GSH0004**). Helpers (`[GpuDeviceFunction]`) can return value types. |
| Expression-bodied (`=>`) | Yes | Function body | Supported for both `void` and value-returning methods (automatically injects `return`). |
| Local functions / Lambdas | Yes | C++ Lambdas | Supported for non-capturing lambdas and local functions. Transpiled to C++ lambda expressions. |
| `goto` | No | - | Strictly prohibited to ensure static analyzability and structured control flow. |
| `try` / `catch` / `finally` | No | - | GPU execution does not support C++ exception handling for performance reasons. |
| `yield return` | Yes | - | Supported for coroutines and collection dispose routing. |
| `await` / `async` | No | - | Asynchronous execution within a kernel is unsupported. |
| `lock` | No | - | Use `Gpu.AtomicAdd` or `Gpu.SyncThreads` for synchronization instead. |

## 2.2 Variables and Scoping

Variables in G# are strictly translated to native C++ variables. 

* **`var` keyword:** Fully supported. The transpiler infers the equivalent C++ type (e.g., `auto`).
* **Variable Shadowing:** Supported, following standard C# scope rules.
* **Inline `out var`:** Supported. Generates the equivalent variable declaration and reference pass in C++.
* **Tuple Swaps / Assignments:** Supported for simple tuple swaps `(a, b) = (b, a)` via generated C++ IIFE (Immediately Invoked Lambda Expressions). Full C# tuple deconstruction into managed types remains unsupported.
* **Target-Typed `new()`:** Fully supported. The transpiler infers the correct structural type instantiation.
* **Heap Allocation (`new Class()`):** Prohibited. All local variables must be primitive types or value types (`struct`), which are allocated in GPU registers or local memory.
* **Arrays (`T[]`) and Collections:** Explicit local instantiation on the managed heap (`new int[10]`) is prohibited. Implicit stack arrays (`stackalloc[]`) and C# 12 collection expressions (`[]`) are fully supported and optimized into C++ local stack arrays. However, **zero-length arrays** (e.g., `stackalloc int[0]`) are explicitly unsupported due to C++ compilation constraints.
* **Array `.Length` Property:** The `.Length` property of local constant-sized arrays (or `[InlineArray]`) is aggressively optimized; it is folded into a compile-time constant or evaluated as `.size()` without generating runtime field accesses.
* **Default Literal (`default`):** Fully supported. Transpiles to zero-initialization or C++ default constructors (e.g., `{}`).
* **Extension Methods:** Supported. Invocation via `this` arguments (e.g., `obj.Method()`) correctly transpiles to standard static C++ function calls.

## 2.3 Operators

Standard C# operators map directly to their CUDA C++ counterparts with identical precedence and associativity.

* **Arithmetic:** `+`, `-`, `*`, `/`, `%`
* **Bitwise:** `&`, `|`, `^`, `~`, `<<`, `>>`
* **Logical:** `&&`, `||`, `!` (Short-circuit evaluation is fully supported)
* **Relational:** `==`, `!=`, `<`, `>`, `<=`, `>=`
* **Assignment:** `=`, `+=`, `-=`, `*=`, `/=`, `%=`, `&=`, `|=`, `^=`, `<<=`, `>>=`. Tuple Swaps `(a, b) = (b, a)` are safely lowered into Immediately Invoked Function Expressions (IIFE) using temporary variables to prevent destructive assignments.
* **Ternary Conditional & Null-Coalescing:** `? :`, `??`, `??=`. Null-coalescing operators are fully supported and lowered into C++ IIFE closures (e.g., `[&]() { auto tmp = ...; return tmp.HasValue ? tmp.Value : ...; }()`).
* **Pointer Operators:** `*` (dereference), `&` (address-of), `->` (member access), as well as postfix increment/decrement (`ptr++`, `ptr--`). *Note: The `unsafe` keyword must be used in the C# method signature.*
* **`sizeof(T)`:** Supported. Translated directly to `sizeof(T)` in C++.

## 2.4 Preprocessor Directives

C# preprocessor directives (e.g., `#if`, `#define`) are **not** translated into CUDA C++ preprocessor directives. They are evaluated by the Roslyn compiler during the AOT phase.
To inject C++ preprocessor macros (like `#define`) into the generated CUDA code, you must use the `[Constant]` attribute on kernel parameters.

## 2.5 In-Depth Syntax Guides

To help you write highly expressive and allocation-free GPU code, G# uniquely supports many modern C# features by lowering them into highly optimized CUDA C++ during the AOT transpilation phase.

### 2.5.1 Switch Expressions

Switch expressions provide a concise, expression-based way to implement pattern matching. G# fully supports switch expressions and automatically translates them into Immediately Invoked Function Expressions (IIFE) in C++ to guarantee zero-allocation and perfect execution on the GPU.

**Example: Value and Relational Pattern Matching**
```csharp
[GpuKernel]
public static void CategorizeValuesKernel(int[] input, float[] output)
{
    int tid = ThreadIndex.X;
    int val = input[tid];
    
    // Switch expressions are natively supported on the GPU.
    output[tid] = val switch
    {
        <= 0 => 0.0f,
        1 => 3.14159f,
        2 => 2.71828f,
        _ => -1.0f
    };
}
```

> [!NOTE]
> **Performance Characteristics**
> The G# transpiler lowers switch expressions into an inline C++ lambda expression (`[&]() { ... }()`). The CUDA compiler (`nvcc`) aggressively inlines this during the PTX generation phase, resulting in branching logic with absolutely zero invocation overhead.

### 2.5.2 Local Functions and Lambdas

G# natively supports C# local functions and lambda expressions. They are highly effective for organizing complex math pipelines or algorithm steps without leaving the scope of the `[GpuKernel]`.

**Example: Local Functions and Capturing Lambdas**
```csharp
[GpuKernel]
public static void LocalMathKernel(int[] data, int factor)
{
    int tid = ThreadIndex.X;

    // 1. Local functions are fully supported
    int LocalAdd(int a, int b)
    {
        return a + b;
    }

    // 2. Value-capturing lambdas are safely transpiled
    var multiply = (int x) => x * factor;

    data[tid] = multiply(LocalAdd(data[tid], 100));
}
```

> [!IMPORTANT]
> **Closure Constraints**
> Local functions and lambdas can freely capture local `struct` variables, primitive types, and kernel parameters. However, capturing managed reference types (classes) or performing actions that force a managed heap closure allocation is strictly prohibited and will trigger an AOT transpilation error.

