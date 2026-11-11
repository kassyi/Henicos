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
| `for` loop | Yes | `for` loop | Supported. Loop unrolling can be requested with `Gpu.Hint.Unroll()`. |
| `while` loop | Yes | `while` loop | Supported. |
| `do...while` loop | Yes | `do...while` loop | Supported. |
| `switch` statement | Yes | `switch` statement | Pattern matching is **supported** (including constant and relational patterns). As in C#, the switch block is one scope: a local or const declared in one section can be used in another (the C++ declares it in a block around the switch). A barrier, a warp shuffle or a call that may suspend inside a section suspends the thread on the CPU paths as it does outside a switch. A local that needs its initializer at the declaration (a span or other ref struct, a ref local, a stack array, shared memory) used that way is reported as [GSH0003](../03_bcl_api/diagnostics/GSH0003.md). |
| `switch` expression | Yes | Zero-allocation branch | Supported for pattern matching and value switching. Note: C# does not support `ref switch` expressions (`ref var r = x switch { ... }`), so use `if/else` or `ref ? :` for conditional `ref` assignment. |
| `break` / `continue` | Yes | `break` / `continue` | Supported within loops and switches. |
| `return` | Yes | `return` | Kernels (`[GpuKernel]`) can now directly return primitive types and value structs. When invoked from the host CPU, the runtime automatically handles device output buffer allocation and host-bound memory copying. |
| Expression-bodied (`=>`) | Yes | Function body | Supported for both `void` and value-returning methods (automatically injects `return`). |
| Local functions / Lambdas | Yes | C++ Lambdas | Supported for lambdas and local functions, capturing or not. Transpiled to C++ lambda expressions that capture by reference (`[&]`), as C# closures capture variables, not values. |
| `goto` | Yes | `goto` | Supported, also on the CPU paths, where a label that a `goto` reaches from after a barrier starts a state of the state machine. A `goto` whose jump crosses a barrier in the source text (a `Gpu.SyncThreads()` or a call to a device function that may suspend lies between the `goto` and its label) is reported as [GSH0040](../03_bcl_api/diagnostics/GSH0040.md). |
| `try` / `catch` / `finally` | No | - | GPU execution does not support C++ exception handling for performance reasons. |
| `yield return` | Yes (`[GpuDeviceFunction]`) | Inlined `foreach` body | Supported in a `[GpuDeviceFunction]` that returns `IEnumerable<T>`; lowered by inlining the iterator body directly into each consumer `foreach`. Not in `[GpuKernel]`, and not across a barrier (GSH0046, GSH0070-GSH0073). |
| `await` / `async` | No | - | Asynchronous execution within a kernel is unsupported. |
| `lock` | No | - | Use `Gpu.AtomicAdd` or `Gpu.SyncThreads` for synchronization instead. |

## 2.2 Variables and Scoping

Variables in G# are strictly translated to native C++ variables. 

* **`var` keyword:** Fully supported. The transpiler infers the equivalent C++ type (e.g., `auto`).
* **Variable Shadowing:** Supported, following standard C# scope rules.
* **Inline `out var`:** Supported. Generates the equivalent variable declaration and reference pass in C++.
* **Tuple Swaps / Deconstruction:** Supported when the right side is a tuple literal or a conditional of tuple literals, nested on both sides or not: `(a, b) = (b, a)`, `(a, (b, c)) = (x, (y, z))`, `(a, b) = f ? (b, a) : (a, b)`, and deconstruction declarations `var (p, (q, w)) = ...` and `(int p, var q) = ...`. As in C#, the target locations are found from left to right, then the right side is evaluated (one branch of a conditional), then the elements are assigned, so a swap reads every value before it writes. A discard (`_`, `int _`) evaluates its value and assigns nothing; `_ = expr;` evaluates `expr`, and `out _` passes a variable that nobody reads. A deconstruction of another value (a `Deconstruct` method, a `ValueTuple` variable), a deconstruction whose value is used, and a conditional deconstruction whose element type is a type parameter are [GSH0003](../03_bcl_api/diagnostics/GSH0003.md).
* **Target-Typed `new()`:** Fully supported. The transpiler infers the correct structural type instantiation.
* **Heap Allocation (`new Class()`):** Prohibited. All local variables must be primitive types or value types (`struct`), which are allocated in GPU registers or local memory.
* **Arrays (`T[]`) and Collections:** A local variable initialized with `new T[N]` of a constant size, a `stackalloc`, an array initializer or a C# 12 collection expression (`[]`) becomes a C++ stack array of the thread; there is no managed heap. The arrays that C# fills with zeros (`new T[N]`, and `stackalloc T[N]` without an initializer unless `[SkipLocalsInit]` applies) are zero-filled ([Type System §2.5](03_type_system.md#25-local-initialization-and-skiplocalsinit)). A stack array has a constant size and one dimension; an array (not a span) of length zero cannot be told apart from a null array and is rejected as well ([GSH0003](../03_bcl_api/diagnostics/GSH0003.md)). It lives until the end of the innermost block, `switch` section or statement body around it, so it cannot be reassigned or returned, and cannot be stored anywhere that outlives that scope: a variable of an outer scope, a parameter, a field or element of memory the method did not create, also through another local or through a call with a `ref` / `out` argument ([GSH0091](../03_bcl_api/diagnostics/GSH0091.md)). An array created anywhere other than the initializer of a local variable is [GSH0001](../03_bcl_api/diagnostics/GSH0001.md). A stack array passes to a `Span<T>`, `ReadOnlySpan<T>` or `T[]` parameter, or initializes a span local, as a view of its elements with its length; it is also the receiver of the span members `Slice`, `Fill` and `AsSpan` and of StackLinq (`AsStackEnumerable()`). A null test on a local array is always false, as in C#. A `stackalloc` or a collection expression may also appear in an argument (`Len(stackalloc int[4])`, `Sum([1, 2])`), a pointer declaration, a `for` header or a nested expression: G# declares its storage before the statement, in the same scope, and zero-fills or initializes it each time it is evaluated, with the elements in their C# order. A span over `stackalloc T[0]` or `[]` is an empty span, and a pointer to `stackalloc T[0]` is null, as in C#.
* **Array `.Length` Property:** The `.Length` property of local constant-sized arrays (or `[InlineArray]`) is aggressively optimized; it is folded into a compile-time constant or evaluated as `.size()` without generating runtime field accesses.
* **Default Literal (`default`):** Fully supported. Transpiles to zero-initialization or C++ default constructors (e.g., `{}`).
* **Default Parameters and Named Arguments:** Fully supported. Call-site named arguments are automatically normalized to declaration order in CUDA C++, and omitted optional parameters are populated with compile-time default constant literals.
* **Extension Methods:** Supported. Invocation via `this` arguments (e.g., `obj.Method()`) correctly transpiles to standard static C++ function calls.

## 2.3 Operators

Standard C# operators map directly to their CUDA C++ counterparts with identical precedence and associativity.

* **Arithmetic:** `+`, `-`, `*`, `/`, `%`. The `%` of `float` and `double` operands (and `%=`) is the C# remainder, the
  C `fmod` result: the sign of the dividend, NaN for an infinite dividend or a zero divisor, the dividend for an
  infinite divisor, and exact on every path. Signed integer arithmetic (`+`, `-`, `*`, unary `-`, `++`, `--`) wraps per
  C# two's complement semantics, emitting Prelude helper functions (such as `__gsharp_add_i32`) where overflow is
  possible to prevent C++ undefined behavior.
* **Bitwise:** `&`, `|`, `^`, `~`, `<<`, `>>`, `>>>`. Shift counts are masked per C# rules (`& 31` for 32-bit types,
  `& 63` for 64-bit types).
* **Logical:** `&&`, `||`, `!` (Short-circuit evaluation is fully supported)
* **Relational:** `==`, `!=`, `<`, `>`, `<=`, `>=`
* **Assignment:** `=`, `+=`, `-=`, `*=`, `/=`, `%=`, `&=`, `|=`, `^=`, `<<=`, `>>=`, `>>>=`. Tuple Swaps
  `(a, b) = (b, a)` are safely evaluated using temporary variables to prevent destructive assignments. A computed
  property of a struct (a getter and setter with bodies) may be the target of a compound assignment, an increment or
  decrement, and a chained assignment (`x = s.P = v`): the receiver is found once, then the getter runs, then the right
  side, then the setter, and the value is the one C# defines; with a user-defined or lifted operator, or with `??=`, it
  is [GSH0003](../03_bcl_api/diagnostics/GSH0003.md).
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
Kernel parameters are not turned into C++ `#define` macros. The `[GpuConstexpr]` attribute
([`GpuConstexprAttribute`](../03_bcl_api/attributes/ConstantAttribute.md)) marks a parameter with candidate constant
values, but the candidates are not used yet: the parameter is passed as an ordinary run-time argument. A later release
compiles one `constexpr` specialization per value.

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

When no arm matches the input, C# throws `SwitchExpressionException`, and so does G# on every path: the managed path runs the C# switch expression, and the native CPU and CUDA builds stop with a run-time error that the host throws as `SwitchExpressionException`, with its message and the place of the switch expression (on CUDA only for a switch expression in the kernel itself, not in a device function it calls). `UnmatchedValue` is not set on the native and CUDA paths. The check stays in the release CUDA build, because it sits in the branch that only an unmatched input reaches. A switch expression with a discard arm (`_`) matches every input and has no such check.

Each arm is converted to the type of the switch expression, as in C#: `x switch { 0 => 1, _ => 2L }` is a `long`, and `n switch { 0 => 1, _ => null }` assigned to an `int?` gives `null` for the discard arm. The input may be a `float`, a `double` or a `Nullable<T>`; it is evaluated once and compared with each constant in order with C# equality, so `double.NaN` matches only a NaN input, `0.0` matches `-0.0`, and `null` matches an input without a value.

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

    // 2. Lambdas capture variables by reference, as in C# (the C++ lambda captures with [&])
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
public static partial class MathDeviceHelpers
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

Transpiled CUDA C++ equivalent, simplified: the real output mangles every name (the kernel becomes
`__gsharp_k_<Namespace>__MathDeviceHelpers__SampleKernel`, the parameter `__gsharp_id_0_data`), and has no comments and
no indentation:
```cpp
// Execution Context: Generated CUDA C++ (Device)
inline __device__ int ComputeStep(int input, int offset, int scale)
{
    return __gsharp_mul_i32(__gsharp_add_i32(input, offset), scale);
}

extern "C" __global__ void SampleKernel(GSharpArray<int> data, unsigned int __gsharp_epoch)
{
    __gsharp_launch_epoch = __gsharp_epoch;
    int tid = (int)threadIdx.x;

    // Call site 1: Default arguments synthesized
    int r1 = ComputeStep(data[tid], 0, 1);

    // Call site 2: Named arguments reordered to parameter order
    int r2 = ComputeStep(data[tid], 5, 2);

    // Call site 3: Partially omitted arguments synthesized
    int r3 = ComputeStep(data[tid], 0, 4);

    data[tid] = __gsharp_add_i32(__gsharp_add_i32(r1, r2), r3);
}
```

## See also



### Language Specification

- [1. Language Specification](00_index.md)
- [5. Limitations and Diagnostics](05_limitations.md)

