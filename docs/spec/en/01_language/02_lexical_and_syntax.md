---
title: "2. Lexical and Syntactic Grammar"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "This document defines the strict subset of C# lexical and syntactic rules supported by the G# transpiler. Because G# translates C# Abstract Syntax ..."
---

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
| `switch` statement | Yes | `switch` statement | Pattern matching is **supported** (including constant and relational patterns). |
| `switch` expression | Yes | Zero-allocation branch | Supported for pattern matching and value switching. Note: C# does not support `ref switch` expressions (`ref var r = x switch { ... }`), so use `if/else` or `ref ? :` for conditional `ref` assignment. |
| `break` / `continue` | Yes | `break` / `continue` | Supported within loops and switches. |
| `return` | Yes | `return` | Kernels (`[GpuKernel]`) can now directly return primitive types and value structs. When invoked from the host CPU, the runtime automatically handles device output buffer allocation and host-bound memory copying. |
| Expression-bodied (`=>`) | Yes | Function body | Supported for both `void` and value-returning methods (automatically injects `return`). |
| Local functions / Lambdas | Yes | C++ Lambdas | Supported for non-capturing lambdas and local functions. Transpiled to C++ lambda expressions. |
| `goto` | No | - | Strictly prohibited to ensure static analyzability and structured control flow. |
| `try` / `catch` / `finally` | No | - | GPU execution does not support C++ exception handling for performance reasons. |
| `yield return` | Yes (`[GpuDeviceFunction]`) | Inlined `foreach` body | Supported in a `[GpuDeviceFunction]` that returns `IEnumerable<T>`; lowered by inlining the iterator body directly into each consumer `foreach`. Not in `[GpuKernel]`, and not across a barrier (GSH0046, GSH0070-GSH0073). |
| `await` / `async` | No | - | Asynchronous execution within a kernel is unsupported. |
| `lock` | No | - | Use `Gpu.AtomicAdd` or `Gpu.SyncThreads` for synchronization instead. |

## 2.2 Variables and Scoping

Variables in G# are strictly translated to native C++ variables. 

* **`var` keyword:** Fully supported. The transpiler infers the equivalent C++ type (e.g., `auto`).
* **Variable Shadowing:** Supported, following standard C# scope rules.
* **Inline `out var`:** Supported. Generates the equivalent variable declaration and reference pass in C++.
* **Tuple Swaps / Assignments:** Supported for simple tuple swaps `(a, b) = (b, a)` (evaluated without destructive assignment). Full C# tuple deconstruction into managed types remains unsupported.
* **Target-Typed `new()`:** Fully supported. The transpiler infers the correct structural type instantiation.
* **Heap Allocation (`new Class()`):** Prohibited. All local variables must be primitive types or value types (`struct`), which are allocated in GPU registers or local memory.
* **Arrays (`T[]`) and Collections:** Explicit local instantiation on the managed heap (`new int[10]`) is prohibited. Implicit stack arrays (`stackalloc[]`) and C# 12 collection expressions (`[]`) are fully supported and optimized into C++ local stack arrays. However, **zero-length arrays** (e.g., `stackalloc int[0]`) are explicitly unsupported due to C++ compilation constraints.
* **Array `.Length` Property:** The `.Length` property of local constant-sized arrays (or `[InlineArray]`) is aggressively optimized; it is folded into a compile-time constant or evaluated as `.size()` without generating runtime field accesses.
* **Default Literal (`default`):** Fully supported. Transpiles to zero-initialization or C++ default constructors (e.g., `{}`).
* **Default Parameters and Named Arguments:** Fully supported. Call-site named arguments are automatically normalized to declaration order in CUDA C++, and omitted optional parameters are populated with compile-time default constant literals.
* **Extension Methods:** Supported. Invocation via `this` arguments (e.g., `obj.Method()`) correctly transpiles to standard static C++ function calls.

## 2.3 Operators

Standard C# operators map directly to their CUDA C++ counterparts with identical precedence and associativity.

* **Arithmetic:** `+`, `-`, `*`, `/`, `%`
* **Bitwise:** `&`, `|`, `^`, `~`, `<<`, `>>`
* **Logical:** `&&`, `||`, `!` (Short-circuit evaluation is fully supported)
* **Relational:** `==`, `!=`, `<`, `>`, `<=`, `>=`
* **Assignment:** `=`, `+=`, `-=`, `*=`, `/=`, `%=`, `&=`, `|=`, `^=`, `<<=`, `>>=`. Tuple Swaps `(a, b) = (b, a)` are safely evaluated using temporary variables to prevent destructive assignments.
* **Ternary Conditional & Null-Coalescing:** `? :`, `??`, `??=`. Null-coalescing operators are fully supported with zero-allocation branching.
* **Pointer Operators:** `*` (dereference), `&` (address-of), `->` (member access), as well as postfix increment/decrement (`ptr++`, `ptr--`). *Note: The `unsafe` keyword must be used in the C# method signature.*
* **`sizeof(T)`:** Supported. Translated directly to `sizeof(T)` in C++.
* **Order of evaluation:** As in C#, operands and arguments are evaluated from left to right, and an assignment finds
  its target before it evaluates the value (compound assignments read the target first). C++ does not keep that order
  for call arguments and binary operands, and evaluates the right side of an assignment first, so when one operand
  changes what another reads (`F(Next(ref i), Next(ref i))`, `a[i] = Next(ref i)`, `x + Update(ref x)`), the
  transpiler evaluates the operands into temporaries in the C# order. Calls are classified by what they write, so
  operands without such an interference are emitted as written and cost nothing.

## 2.4 Preprocessor Directives

C# preprocessor directives (e.g., `#if`, `#define`) are **not** translated into CUDA C++ preprocessor directives. They are evaluated by the Roslyn compiler during the AOT phase.
To inject C++ preprocessor macros (like `#define`) into the generated CUDA code, you must use the `[Constant]` attribute on kernel parameters.

## 2.5 In-Depth Syntax Guides

To help you write highly expressive and allocation-free GPU code, G# uniquely supports many modern C# features by lowering them into highly optimized CUDA C++ during the AOT transpilation phase.

### 2.5.1 Switch Expressions

> [!NOTE]
> **Performance Characteristics**
> The G# transpiler lowers switch expressions into strict zero-allocation structures that the CUDA compiler (`nvcc`) aggressively inlines during the PTX generation phase, resulting in branching logic with absolutely zero invocation overhead.

Switch expressions provide a concise, expression-based way to implement pattern matching. G# fully supports switch expressions and guarantees zero-allocation and optimal execution on the GPU.

**Example: Value and Relational Pattern Matching**
```csharp
// Execution Context: G# Kernel (GPU)
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

### 2.5.2 Local Functions and Lambdas

> [!IMPORTANT]
> **Closure Constraints**
> Local functions and lambdas can freely capture local `struct` variables, primitive types, and kernel parameters. However, capturing managed reference types (classes) or performing actions that force a managed heap closure allocation is strictly prohibited and will trigger an AOT transpilation error.

G# natively supports C# local functions and lambda expressions. They are highly effective for organizing complex math pipelines or algorithm steps without leaving the scope of the `[GpuKernel]`.

**Example: Local Functions and Capturing Lambdas**
```csharp
// Execution Context: G# Kernel (GPU)
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

### 2.5.3 `null` to `nullptr` Mapping

In C#, the `null` keyword represents a null reference. During AOT transpilation to CUDA C++, G# explicitly remaps all `null` literal expressions to the `nullptr` keyword. This guarantees strict C++ type safety and prevents implicit integer conversions (e.g., `0`) that could cause compilation failures in modern CUDA drivers.

**Example**
```csharp
// Execution Context: G# Kernel (GPU)
[GpuKernel]
public static void ProcessPointer(int* ptr)
{
    // The C# 'null' keyword is transparently lowered to 'nullptr' in CUDA C++
    if (ptr == null)
    {
        return;
    }
}
```

### 2.5.4 Default Parameters and Named Arguments

> [!NOTE]
> **Static Determinism & NVCC Compatibility**  
> CUDA C++ does not natively support C#-style named argument syntax (`param: expr`). Furthermore, relying purely on C++ default parameter values can lead to compilation errors or unexpected signature mismatches across compilation boundaries. The G# AOT transpiler guarantees 100% static determinism by resolving parameter bindings at compile-time via Roslyn Semantic Models and synthesizing the exact declaration order and default constant literals into generated CUDA C++ call sites.

G# fully supports C# optional parameters (default values) and named arguments across device functions (`[GpuDeviceFunction]`), local methods, and kernel call sites.

* **Named Argument Reordering**: Callers can specify arguments in any order using C# named syntax (`name: expr`). The transpiler inspects the semantic symbol and normalizes arguments to strictly match the parameter declaration sequence expected by C++.
* **Default Argument Synthesis**: When optional parameters are omitted at call sites, the transpiler extracts their compile-time default values (from declaration syntax or constant symbols) and injects them directly into the generated C++ invocation arguments.
* **Kernel Signature Preservation**: For `[GpuKernel]` entry points, explicit default parameter values are preserved in the generated host launcher extensions (`WrapperEmitter`), providing a natural and idiomatic C# experience on the CPU.

**Example: Named and Default Arguments on Device Methods**
```csharp
// Execution Context: G# Kernel (GPU)
public static class MathDeviceHelpers
{
    [GpuDeviceFunction]
    public static int ComputeStep(int input, int offset = 0, int scale = 1)
    {
        return (input + offset) * scale;
    }

    [GpuKernel]
    public static void SampleKernel(int[] data)
    {
        int tid = ThreadIndex.X;

        // 1. Standard call: offset (= 0) and scale (= 1) are omitted and synthesized
        int r1 = ComputeStep(data[tid]);

        // 2. Named arguments: reordered to match parameter signature (offset: 5, scale: 2)
        int r2 = ComputeStep(data[tid], scale: 2, offset: 5);

        // 3. Partial omission: offset is synthesized with default 0, scale is explicitly 4
        int r3 = ComputeStep(data[tid], scale: 4);

        data[tid] = r1 + r2 + r3;
    }
}
```

Transpiled CUDA C++ equivalent:
```cpp
// Execution Context: Generated CUDA C++ (Device)
__device__ int ComputeStep(int input, int offset, int scale)
{
    return (input + offset) * scale;
}

extern "C" __global__ void SampleKernel(int* __restrict__ data)
{
    int tid = threadIdx.x;

    // Call site 1: Default arguments synthesized
    int r1 = ComputeStep(data[tid], 0, 1);

    // Call site 2: Named arguments reordered to parameter order
    int r2 = ComputeStep(data[tid], 5, 2);

    // Call site 3: Partially omitted arguments synthesized
    int r3 = ComputeStep(data[tid], 0, 4);

    data[tid] = r1 + r2 + r3;
}
```

## See also



### Language Specification

- [1. Language Specification](00_index.md)
- [5. Limitations and Diagnostics](05_limitations.md)

